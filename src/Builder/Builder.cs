using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;
using KanikoRemote.Auth;
using KanikoRemote.CLI;
using KanikoRemote.Config;
using KanikoRemote.K8s;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Builder
{
    internal class Builder : IAsyncDisposable
    {
        private NamespacedClient k8sClient;
        private ILogger<Builder> logger;

        private V1Pod pod;
        private string? localContext;
        private JsonObject dockerConfig;
        private int podStartTimeout;
        private int podTransferPacketSize;
        private bool keepPod;

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

        public Builder(
            NamespacedClient k8sClient,
            string contextLocation,
            string? dockerfile,
            IEnumerable<string> destinationTags,
            IEnumerable<Authoriser> authorisers,
            IEnumerable<string> kanikoPassthroughArgs,
            BuilderConfiguration config,
            ILogger<Builder> logger)
        {
            this.k8sClient = k8sClient;
            this.logger = logger;

            this.podStartTimeout = config.PodStartTimeout;
            this.podTransferPacketSize = config.PodTransferPacketSize;
            this.keepPod = config.KeepPod == "true";

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
                    throw new FileNotFoundException($"Could not find dockerfile {localDockerfile}");
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

            this.logger.LogInformation($"Configured builder with the {matchingAuthorisers.Count()} auth profiles");
            this.logger.LogDebug($"Generated docker config for builder: {dockerConfig}");
            this.logger.LogDebug($"Generated pod spec for builder: {this.pod}");
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            if (this.podExistsInKubeApi())
            {
                if (this.keepPod)
                {
                    this.logger.LogWarning($"Not removing builder pod {this.podNameInKube} as 'keepPod' configured");
                }
                else
                {
                    logger.LogInformation($"Deleting builder pod {this.podNameInKube}");
                    await this.k8sClient.DeletePodAsync(this.podNameInKube);
                    logger.LogInformation($"Deleted builder pod {this.podNameInKube}");
                }
            }
            else
            {
                logger.LogDebug("Builder not initialised, so no disposal required");
            }
        }

        private bool ParseIsLocalContext(string contextLocation)
        {
            if (Uri.TryCreate(contextLocation, UriKind.RelativeOrAbsolute, out var uri))
            {
                if (!uri.IsAbsoluteUri)
                {
                    this.logger.LogInformation("Local context detected, the context will be transferred directly to the builder pod");
                    return true;
                }
                else
                {
                    logger.LogInformation("Remote context detected, builder pod will be authorised to access configured remote storage");
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


        public async ValueTask<string> Initialise()
        {
            this.pod = await this.k8sClient.CreatePodAsync(this.pod);
            this.logger.LogDebug($"Initialised builder pod with spec: {this.pod}");
            return this.podNameInKube;
        }

        public async ValueTask<string> Setup()
        {
            await this.k8sClient.AwaitContainerRunningStateAsync(
                podName: this.podNameInKube,
                container: "setup",
                timeoutSeconds: this.podStartTimeout);

            if (this.localContext != null)
            {
                await this.transferLocalBuildContext(this.localContext);
            }
            else
            {
                logger.LogInformation($"Using remote storage as build context, skipping local upload");
            }

            await this.k8sClient.UploadStringAsFileToContiainerAsync(
                podName: this.podNameInKube,
                container: "setup",
                stringData: this.dockerConfig.ToJsonString(),
                fileName: "config.json",
                remoteRoot: "/kaniko/.docker",
                packetSize: this.podTransferPacketSize);

            return this.podNameInKube;
        }

        private async Task transferLocalBuildContext(string localContextPath)
        {
            var contextMatcher = new Matcher();
            contextMatcher.AddInclude("**");

            var dockerIgnorePath = Path.Combine(localContextPath, ".dockerignore");
            if (File.Exists(dockerIgnorePath))
            {
                using var dockerIgnore = new StreamReader(dockerIgnorePath);
                while (!dockerIgnore.EndOfStream)
                {
                    var ignorePattern = await dockerIgnore.ReadLineAsync();
                    if (ignorePattern != null & !ignorePattern!.StartsWith("#"))
                    {
                        contextMatcher.AddExclude(ignorePattern);
                    }
                }
            }

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
                
                this.logger.LogInformation(KanikoRemoteConsoleFormatter.OverwritableEvent, sb.ToString());
            });

            var bytesSent = await this.k8sClient.UploadLocalFilesToContainerAsync(
                podName: this.podNameInKube,
                container: "setup",
                localRoot: localContextPath,
                localAbsoluteFilepaths: localFilesToSend,
                remoteRoot: "/workspace",
                packetSize: this.podTransferPacketSize,
                progress: prog);

            this.logger.LogInformation($"Transferred {fileCount} to builder ({bytesSent / 1024}kB)".PadRight(100));
        }

        public async Task<string> Build()
        {
            var started = this.k8sClient.AwaitContainerRunningStateAsync(
                podName: this.podNameInKube,
                container: "builder",
                timeoutSeconds: 10);
            var failedStart = this.k8sClient.AwaitContainerTerminatedStateAsync(
                podName: this.podNameInKube,
                container: "builder",
                timeoutSeconds: 10);

            var containerState = await Task.WhenAny(started, failedStart);

            if (containerState == failedStart)
            {
                // Try to get logs of why kaniko failed to start
                await foreach (var logLine in this.k8sClient.GetContainerLogsAsync(
                    podName: this.podNameInKube,
                    container: "builder",
                    sinceSeconds: 10))
                {
                    this.logger.LogInformation(logLine);
                }
                throw new KanikoException("Kaniko failed to start, see above logs for erroneous args", failedStart.Result);
            }

            // Else tail for actual build logs
            await foreach (var logLine in this.k8sClient.GetContainerLogsAsync(
                podName: this.podNameInKube,
                container: "builder",
                sinceSeconds: 10,
                tail: true))
            {
                this.logger.LogInformation(logLine);
            }

            var finished = await this.k8sClient.AwaitContainerTerminatedStateAsync(
                podName: this.podNameInKube,
                container: "builder",
                timeoutSeconds: 10);

            if (finished.ExitCode == 0)
            {
                return finished.Message;
            }
            throw new KanikoException("Kaniko failed to build and/or push image, increase verbosity if kaniko logs are not visible above", finished);
        }
    }
}