using System.CommandLine;
using System.Diagnostics;
using KanikoRemote.Builder;
using KanikoRemote.CLI;
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

            var rootCommand = new RootCommand(description: @"
                Build an image from a Dockerfile on a k8s cluster using kaniko

                This tool can be explicitly invoked as 'kaniko-remote'.
                If optionally installed, this tool can additionally be invoked as 'docker'.");
            var buildCommandBinder = new BuildCommandBinder(rootCommand);

            rootCommand.SetHandler(Build, buildCommandBinder, new LoggingBinder());

            return await rootCommand.InvokeAsync(args);
        }

        static async Task Build(BuilderArguments buildCommandArgs, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<Program>();

            var config = new Config(loggerFactory);

            var k8sClient = new NamespacedClient(
                options: config.KubernetesOptions,
                logger: loggerFactory.CreateLogger<NamespacedClient>());

            var timer = new Stopwatch();
            await using (var builder = new Builder.Builder(
                k8sClient: k8sClient,
                authorisers: config.Authorisers,
                configOptions: config.BuilderOptions,
                arguments: buildCommandArgs,
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
            }
        }
    }


}