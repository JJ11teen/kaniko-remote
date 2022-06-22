using System.CommandLine;
using System.Diagnostics;
using KanikoRemote.Builder;
using KanikoRemote.CLI;
using KanikoRemote.Config;
using KanikoRemote.K8s;
using Microsoft.Extensions.Logging;

namespace KanikoRemote
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                cts.Cancel();
                // e.Cancel = true;
            };

            var loggerBinder = new LoggerBinder();
            var rootCommand = new RootCommand(description: "Build an image from a Dockerfile on a k8s cluster using kaniko\n\n"
                + "kaniko-remote matches the docker CLI usage for building container images, "
                + "acting as a shim between a docker build command and kaniko running on a (possibly remote) "
                + "kubernetes cluster. It additionally provides a no-op command for docker push.");

            var buildCommand = new Command("build", "Build and push an image to a repository from a Dockerfile");
            var buildCommandBinder = new BuildCommandBinder(buildCommand);
            buildCommand.SetHandler(Build, buildCommandBinder, loggerBinder);

            var pushCommand = new Command("push", "A no-op as a successful build will push automatically");
            pushCommand.SetHandler((loggerFactory) => {
                var logger = loggerFactory.CreateLogger<Program>();
                logger.LogWarning("push is a no-op as kaniko-remote pushes successful builds automatically");
            }, loggerBinder);

            var versionCommand = new Command("version", "Show the kaniko-remote version information");
            versionCommand.SetHandler((loggerFactory) => {
                var logger = loggerFactory.CreateLogger<Program>();
                logger.LogWarning(new EventId(1000, "version"), GetVersionString());
            }, loggerBinder);

            rootCommand.AddCommand(buildCommand);
            rootCommand.AddCommand(pushCommand);
            rootCommand.AddCommand(versionCommand);

            return await rootCommand.InvokeAsync(args);
        }

        static async Task Build(BuildArguments buildCommandArgs, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<Program>();

            var config = ConfigLoader.LoadConfig(loggerFactory);

            var tagger = new Tagger.Tagger(config.Tagger, loggerFactory.CreateLogger<Tagger.Tagger>());

            var k8sClient = new NamespacedClient(config.Kubernetes, loggerFactory.CreateLogger<NamespacedClient>());

            var timer = new Stopwatch();
            await using (var builder = new Builder.Builder(
                k8sClient: k8sClient,
                contextLocation: buildCommandArgs.ContextLocation,
                dockerfile: buildCommandArgs.RelativeDockfilePath,
                destinationTags: tagger.TransformTags(buildCommandArgs.DestinationTags),
                authorisers: config.Authorisers,
                kanikoPassthroughArgs: buildCommandArgs.SerialiseKanikoPassthroughArgs(),
                config: config.Builder,
                logger: loggerFactory.CreateLogger<Builder.Builder>()))
            {
                var podName = await builder.Initialise();
                logger.LogInformation($"Builder {k8sClient.Namespace}/{podName} created, pending scheduling");

                timer.Start();
                await builder.Setup();
                timer.Stop();
                logger.LogInformation($"Builder {k8sClient.Namespace}/{podName} setup in {timer.Elapsed.TotalSeconds:F2} seconds, streaming logs:");

                timer.Restart();
                var imageDigest = await builder.Build();
                timer.Stop();
                logger.LogInformation($"Builder {k8sClient.Namespace}/{podName} complete in {timer.Elapsed.TotalSeconds:F2} seconds");
                logger.LogInformation($"Built image digest: {imageDigest}");
                logger.LogInformation("The newly built image has been pushed to your container registry and is not available locally");
            }
        }

        public static string GetVersionString()
        {
            return ThisAssembly.Git.SemVer.Major + "." + ThisAssembly.Git.SemVer.Minor + "." + ThisAssembly.Git.SemVer.Patch + "+" + ThisAssembly.Git.Commit;
        }

    }


}