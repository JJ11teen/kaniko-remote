using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;
using KanikoRemote.Auth;
using KanikoRemote.Config;
using KanikoRemote.K8s;
using Microsoft.Extensions.FileSystemGlobbing;

namespace KanikoRemote.Builder
{
    internal class Builder : IAsyncDisposable
    {
        private NamespacedClient k8sClient;

        private V1Pod pod;
        private string? localContext;
        private JsonObject dockerConfig;
        private int podStartTimeout;
        private int podTransferPacketSize;
        private bool keepPod;
        private Stopwatch timer;

        private bool podExistsInKubeApi() => !string.IsNullOrEmpty(this.pod.Metadata.Name);
        public string podNameInKube
        {
            get
            {
                if (!this.podExistsInKubeApi())
                {
                    throw new InvalidOperationException("Cannot get pod name before Pod is initialised");
                }
                return this.pod.Metadata.Name;
            }
        }
        public string builderName => $"{this.k8sClient.Namespace}/{this.podNameInKube}";

        private string timerStr => $"{this.timer.Elapsed.TotalSeconds:F2} seconds";

        public Builder(
            NamespacedClient k8sClient,
            string contextLocation,
            string? dockerfile,
            IEnumerable<string> destinationTags,
            IEnumerable<Authoriser> authorisers,
            IEnumerable<string> kanikoPassthroughArgs,
            BuilderConfiguration config)
        {
            this.k8sClient = k8sClient;

            this.podStartTimeout = config.PodStartTimeout;
            this.podTransferPacketSize = config.PodTransferPacketSize;
            this.keepPod = config.KeepPod == "true";
            this.timer = new Stopwatch();

            this.pod = Specs.GeneratePodSpec(
                name: config.Name,
                cpu: config.CPU,
                memory: config.Memory,
                kanikoImage: config.KanikoImage,
                setupImage: config.SetupImage,
                additionalLabels: config.AdditionalLabels,
                additionalAnnotations: config.AdditionalAnnotations);

            IEnumerable<string> kanikoArgs;
            var urlsToAuth = new List<string>(destinationTags);
            if (ParseIsLocalContext(contextLocation))
            {
                this.localContext = contextLocation;
                var localDockerfile = Path.Combine(this.localContext, dockerfile ?? "Dockerfile");
                if (!File.Exists(localDockerfile))
                {
                    throw new LocalContextException($"Could not find dockerfile {localDockerfile}");
                }
                kanikoArgs = GenerateKanikoArgumentList(
                    dockerfile,
                    destinationTags,
                    kanikoPassthroughArgs.Concat(config.AdditionalKanikoArgs));
                this.pod = Specs.MountContextForExecTransfer(this.pod);
            }
            else
            {
                kanikoArgs = GenerateKanikoArgumentList(
                    dockerfile,
                    destinationTags,
                    kanikoPassthroughArgs.Concat(config.AdditionalKanikoArgs),
                    contextLocation);
                urlsToAuth.Add(contextLocation);
            }

            var matchingAuthorisers = authorisers
                .Where(a => a.AlwaysMount() || urlsToAuth.Any(u => u == a.URLToMatch));

            this.pod = Specs.SetKanikoArgs(this.pod, kanikoArgs);

            this.dockerConfig = new JsonObject();
            foreach (var authoriser in authorisers)
            {
                this.dockerConfig = authoriser.AppendAuthToDockerConfig(this.dockerConfig);
                this.pod = authoriser.AppendAuthToPod(this.pod);
            }

            SimpleLogger.WriteInfo($"Configured builder with the {matchingAuthorisers.Count()} auth profiles");
            SimpleLogger.WriteDebug($"Generated docker config for builder: {dockerConfig}");
            SimpleLogger.WriteDebug($"Generated pod spec for builder: {this.pod}");
        }

        public async ValueTask DisposeAsync()
        {
            if (this.podExistsInKubeApi())
            {
                if (this.keepPod)
                {
                    SimpleLogger.WriteInfo($"Not removing builder pod {this.podNameInKube} as 'keepPod' configured");
                }
                else
                {
                    SimpleLogger.WriteInfo($"Removing builder pod {this.podNameInKube}");
                    await this.k8sClient.DeletePodAsync(this.podNameInKube);
                    SimpleLogger.WriteInfo($"Removed builder pod {this.podNameInKube}");
                }
            }
            else
            {
                SimpleLogger.WriteInfo("Builder not initialised, so no disposal required");
            }
        }

        private bool ParseIsLocalContext(string contextLocation)
        {
            if (Uri.TryCreate(contextLocation, UriKind.RelativeOrAbsolute, out var uri))
            {
                if (!uri.IsAbsoluteUri)
                {
                    SimpleLogger.WriteInfo("Local context detected, the context will be transferred directly to the builder pod");
                    return true;
                }
                else
                {
                    SimpleLogger.WriteInfo("Remote context detected, builder pod will be authorised to access configured remote storage");
                    return false;
                }
            }
            throw new ArgumentOutOfRangeException("Unable to parse context location");
        }

        static private IEnumerable<string> GenerateKanikoArgumentList(
            string? dockerfile,
            IEnumerable<string> destinationTags,
            IEnumerable<string> parsedKanikoArgs,
            string? remoteContext = null)
        {
            var args = new Dictionary<string, string?>()
            {
                {"digest-file", "/dev/termination-log"}
            };

            args["dockerfile"] = dockerfile;
            args.Add("context", remoteContext);

            return args
                .Where((kvp, i) => kvp.Value != null)
                .Select((kvp, i) => $"--{kvp.Key}={kvp.Value}")
                .Concat(destinationTags.Select(d => $"--destination={d}"))
                .Concat(parsedKanikoArgs);
        }

        public async Task<string> Initialise(CancellationToken ct)
        {
            this.pod = await this.k8sClient.CreatePodAsync(this.pod, ct);
            ct.ThrowIfCancellationRequested();
            SimpleLogger.WriteDebug($"Initialised builder pod with spec: {this.pod}");
            SimpleLogger.WriteInfo($"Builder {this.builderName} created, pending scheduling");
            return this.podNameInKube;
        }

        public async ValueTask<string> Setup(CancellationToken ct)
        {
            this.timer.Reset();
            this.timer.Start();
            await this.k8sClient.AwaitContainerRunningStateAsync(
                podName: this.podNameInKube,
                container: "setup",
                timeoutSeconds: this.podStartTimeout,
                ct: ct);
            ct.ThrowIfCancellationRequested();

            if (this.localContext != null)
            {
                await this.transferLocalBuildContext(this.localContext, ct);
                ct.ThrowIfCancellationRequested();
            }
            else
            {
                SimpleLogger.WriteDebug($"Using remote storage as build context, skipping local upload");
            }

            await this.k8sClient.UploadStringAsFileToContiainerAsync(
                podName: this.podNameInKube,
                container: "setup",
                stringData: this.dockerConfig.ToJsonString(),
                fileName: "config.json",
                remoteRoot: "/kaniko/.docker",
                packetSize: this.podTransferPacketSize,
                ct: ct);
            ct.ThrowIfCancellationRequested();

            this.timer.Stop();
            SimpleLogger.WriteInfo($"Builder {this.builderName} setup in {this.timerStr}, streaming logs:");
            return this.podNameInKube;
        }

        private async Task transferLocalBuildContext(string localContextPath, CancellationToken ct)
        {
            var contextMatcher = new Matcher();
            contextMatcher.AddInclude("**");

            var dockerIgnorePath = Path.Combine(localContextPath, ".dockerignore");
            if (File.Exists(dockerIgnorePath))
            {
                using var dockerIgnore = new StreamReader(dockerIgnorePath);
                while (!dockerIgnore.EndOfStream && !ct.IsCancellationRequested)
                {
                    var ignorePattern = await dockerIgnore.ReadLineAsync().WaitAsync(ct);
                    if (ignorePattern != null & !ignorePattern!.StartsWith("#"))
                    {
                        contextMatcher.AddExclude(ignorePattern);
                    }
                }
            }
            ct.ThrowIfCancellationRequested();

            var localFilesToSend = contextMatcher.GetResultsInFullPath(localContextPath);
            var fileCount = localFilesToSend.Count();

            var prog = new Progress<(long, long)>(update => {
                var (current, total) = update;
                var percent = ((double)current / (double)total) * 40;
                var sb = new StringBuilder();
                
                sb.Append("Transferring ");
                sb.Append(fileCount);
                sb.Append(" files to builder [");
                sb.Append(new string('#', (int)percent));
                sb.Append(new string('-', 40 - (int)percent));
                sb.Append("] (");
                sb.Append(current / 1024);
                sb.Append("/");
                sb.Append(total / 1024);
                sb.Append("kB)");
                
                SimpleLogger.WriteProgression(sb.ToString());
            });

            var bytesSent = await this.k8sClient.UploadLocalFilesToContainerAsync(
                podName: this.podNameInKube,
                container: "setup",
                localRoot: localContextPath,
                localAbsoluteFilepaths: localFilesToSend,
                remoteRoot: "/workspace",
                packetSize: this.podTransferPacketSize,
                progress: prog,
                ct: ct);
            ct.ThrowIfCancellationRequested();

            SimpleLogger.WriteInfo($"Transferred {fileCount} files to builder ({bytesSent / 1024}kB)".PadRight(100));
        }

        public async Task<string> Build(CancellationToken ct)
        {
            this.timer.Reset();
            this.timer.Start();
            
            var started = this.k8sClient.AwaitContainerRunningStateAsync(
                podName: this.podNameInKube,
                container: "builder",
                timeoutSeconds: 10,
                ct: ct);
            var failed = this.k8sClient.AwaitContainerTerminatedStateAsync(
                podName: this.podNameInKube,
                container: "builder",
                timeoutSeconds: 10,
                ct: ct);

            var containerState = await Task.WhenAny(started, failed);
            ct.ThrowIfCancellationRequested();

            if (containerState != started)
            {
                // Try to get logs of why kaniko failed to start
                await foreach (var logLine in this.k8sClient.GetContainerLogsAsync(
                    podName: this.podNameInKube,
                    container: "builder",
                    sinceSeconds: 10,
                    ct: ct))
                {
                    SimpleLogger.WriteInfo(logLine);
                }
                throw new KanikoRuntimeException("Kaniko failed to start, see above logs for more information", failed.Result);
            }

            // Else tail for actual build logs
            await foreach (var logLine in this.k8sClient.GetContainerLogsAsync(
                podName: this.podNameInKube,
                container: "builder",
                sinceSeconds: 10,
                tail: true,
                ct: ct))
            {
                SimpleLogger.WriteInfo(logLine);
            }
            ct.ThrowIfCancellationRequested();

            var finished = await this.k8sClient.AwaitContainerTerminatedStateAsync(
                podName: this.podNameInKube,
                container: "builder",
                timeoutSeconds: 10,
                ct: ct);
            ct.ThrowIfCancellationRequested();

            this.timer.Stop();
            if (finished.ExitCode == 0)
            {
                SimpleLogger.WriteInfo($"Builder {this.builderName} complete in {this.timerStr}");
                return finished.Message;
            }

            throw new KanikoRuntimeException("Kaniko failed to build and/or push image, increase verbosity if kaniko logs are not visible above");
        }
    }
}