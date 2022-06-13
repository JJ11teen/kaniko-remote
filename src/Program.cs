using System.CommandLine;
using System.CommandLine.Binding;
using KanikoRemote.Builder;
using KanikoRemote.K8s;
using KanikoRemote.Parser;
using Microsoft.Extensions.Logging;

namespace KanikoRemote
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
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
            var config = Config.GetDefaultConfig();
            var k8sClient = new NamespacedClient(
                options: config.KubernetesOptions,
                logger: loggerFactory.CreateLogger<NamespacedClient>());
            await using (var builder = new Builder.Builder(
                k8sClient: k8sClient,
                configOptions: config.BuilderOptions,
                arguments: buildCommandArgs,
                logger: loggerFactory.CreateLogger<Builder.Builder>()))
            {
                await builder.Setup();
            }
        }
    }

    public class LoggingBinder : BinderBase<ILoggerFactory>
    {
        protected override ILoggerFactory GetBoundValue(BindingContext bindingContext)
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(
                builder => builder.AddConsole());

            return loggerFactory;
        }
    }
}