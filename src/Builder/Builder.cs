using System.Text.Json.Nodes;
using k8s.Models;
using KanikoRemote.Auth;
using KanikoRemote.K8s;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Builder
{
    internal class Builder : IAsyncDisposable
    {

        private NamespacedClient k8sClient;
        private BuilderOptions options;
        private BuilderArguments arguments;
        private ILogger<Builder> logger;

        private V1Pod pod;
        private string? localContext;
        private JsonObject dockerConfig;

        private bool podExistsInKubeApi() => !string.IsNullOrEmpty(this.pod.Metadata.Name);
        private string podNameInKube
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
            BuilderOptions configOptions,
            BuilderArguments arguments,
            ILogger<Builder> logger)
        {
            this.k8sClient = k8sClient;
            this.options = configOptions;
            this.arguments = arguments;
            this.logger = logger;

            this.pod = Specs.GeneratePodSpec(
                name: options.Name,
                cpu: options.CPU,
                memory: options.Memory,
                kanikoImage: options.KanikoImage,
                setupImage: options.SetupImage,
                additionalLabels: options.AdditionalLabels,
                additionalAnnotations: options.AdditionalAnnotations);

            IEnumerable<string> kanikoArgs;
            var urlsToAuth = new List<string>(arguments.DestinationTags);
            if (ParseIsLocalContext())
            {
                this.localContext = arguments.ContextLocation;
                kanikoArgs = GenerateKanikoArgumentList();
                this.pod = Specs.MountContextForExecTransfer(this.pod);
                // TODO: check dockerfile exists
            }
            else
            {
                kanikoArgs = GenerateKanikoArgumentList(arguments.ContextLocation);
                urlsToAuth.Add(arguments.ContextLocation);
            }

            var authorisers = AuthMatcher.GetMatchingAuthorisers(urlsToAuth);
            this.pod = Specs.SetKanikoArgs(this.pod, kanikoArgs);

            this.dockerConfig = new JsonObject();
            foreach (var authoriser in authorisers)
            {
                this.dockerConfig = authoriser.AppendAuthToDockerConfig(this.dockerConfig);
                this.pod = authoriser.AppendAuthToPod(this.pod);
            }

            var authNames = string.Join(" ", authorisers.Select(a => a.GetName()));
            this.logger.LogInformation($"Configured builder with the following auth profiles: {authNames}");
            this.logger.LogDebug($"Generated docker config for builder: {dockerConfig}");
            this.logger.LogDebug($"Generated pod spec for builder: {this.pod}");
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            if (this.podExistsInKubeApi())
            {
                logger.LogInformation($"Deleting builder pod {this.podNameInKube}");
                await this.k8sClient.DeletePodAsync(this.podNameInKube);
                logger.LogInformation($"Deleted builder pod {this.podNameInKube}");
            }
            else
            {
                logger.LogDebug("Builder not initialised, so no disposal required");
            }
        }

        private bool ParseIsLocalContext()
        {
            // TODO: actually confirm if local & handle accordingly
            return true;
        }

        private IEnumerable<string> GenerateKanikoArgumentList(string? remoteContext = null)
        {
            var args = new Dictionary<string, string>()
            {
                {"dockerfile", "Dockerfile"},
                {"digest-file", "/dev/termination-log"}
            };

            if (arguments.RelativeDockfilePath == null)
            {
                throw new ArgumentNullException("dockerfile", "Dockerfile path cannot be null");
            }
            args["dockerfile"] = arguments.RelativeDockfilePath;

            if (arguments.Target != null)
            {
                args.Add("target", arguments.Target);
            }
            if (arguments.Platform != null)
            {
                args.Add("platform", arguments.Platform);
            }

            if (remoteContext != null)
            {
                args.Add("context", remoteContext);
            }

            return args
                .Select((k, v) => $"--{k}={v}")
                .Concat(options.AdditionalKanikoArgs)
                .Concat(arguments.MetadataLabels.Select(l => $"--label={l}"))
                .Concat(arguments.BuildArgVariables.SelectMany(ba => new List<string>() { "--build-arg", ba }))
                .Concat(arguments.DestinationTags.Select(d => $"--destination={d}"));
        }

        public async ValueTask<string> Setup()
        {
            this.pod = await this.k8sClient.CreatePodAsync(this.pod);
            this.logger.LogDebug($"Initialised builder pod with spec: {this.pod}");

            await this.k8sClient.AwaitContainerRunningStateAsync(
                podName: this.podNameInKube,
                container: "setup",
                timeoutSeconds: options.PodStartTimeout);

            if (this.localContext != null)
            {
                await this.transferLocalBuildContext(this.localContext);
            }
            else
            {
                logger.LogInformation($"Using remote storage as build context, skipping local upload");
            }

            // TODO: upload docker config

            return this.podNameInKube;
        }

        private async Task transferLocalBuildContext(string localContextPath)
        {
            var contextMatcher = new Matcher();
            contextMatcher.AddInclude("**");
            var localFilesToSend = contextMatcher.GetResultsInFullPath(localContextPath);

            // TODO: filter with dockerignore if it exists

            await this.k8sClient.UploadLocalFilesToContainerAsync(
                podName: this.podNameInKube,
                container: "setup",
                localFiles: localFilesToSend,
                remotePath: "/workspace",
                relativeLocalRoot: localContextPath,
                packetSize: options.PodTransferPacketSize,
                progressBarDescription: "todo");
        }
    }
}