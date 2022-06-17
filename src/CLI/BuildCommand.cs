using System.CommandLine;
using System.CommandLine.Binding;
using KanikoRemote.Builder;

namespace KanikoRemote.CLI
{
    internal class BuildCommandBinder : BinderBase<BuilderArguments>
    {
        private Option<IList<string>> tags;
        private Option<string?> file;
        private Option<IList<string>> buildArgs;
        private Option<IList<string>> labels;
        private Option<string?> target;
        private Option<string?> platform;
        private Option<bool> quiet;
        private Option<string?> iidFile;
        private Argument<string> path;

        public BuildCommandBinder(Command commandToBind)
        {
            tags = new Option<IList<string>>(
                aliases: new string[] { "-t", "--tag" },
                description: "Name and tag in the \"name:tag\" format");
            file = new Option<string?>(
                aliases: new string[] { "-f", "--file" },
                description: "Path to the dockerfile within context (default \"Dockerfile\")",
                getDefaultValue: () => "Dockerfile");
            buildArgs = new Option<IList<string>>(
                name: "--build-arg",
                description: "Set build-time ARG variables");
            labels = new Option<IList<string>>(
                name: "--label",
                description: "Set metadata for an image");
            target = new Option<string?>(
                name: "--target",
                description: "Set the target build stage to build");
            platform = new Option<string?>(
                name: "--platform",
                description: "Set platform if server is multi-platform capable");
            quiet = new Option<bool>(
                name: "--quiet",
                description: "Suppress build output and print image ID on success",
                getDefaultValue: () => false);
            iidFile = new Option<string?>(
                name: "--iidfile",
                description: "Write the image ID to the file");
            path = new Argument<string>(
                name: "path",
                description: "Path to build context");

            commandToBind.AddOption(tags);
            commandToBind.AddOption(file);
            commandToBind.AddOption(buildArgs);
            commandToBind.AddOption(labels);
            commandToBind.AddOption(target);
            commandToBind.AddOption(platform);
            commandToBind.AddOption(quiet);
            commandToBind.AddOption(iidFile);
            commandToBind.AddArgument(path);
        }
        protected override BuilderArguments GetBoundValue(BindingContext bindingContext)
        {
            return new BuilderArguments(bindingContext.ParseResult.GetValueForArgument(path))
            {
                DestinationTags = bindingContext.ParseResult.GetValueForOption(tags) ?? new List<string>(),
                RelativeDockfilePath = bindingContext.ParseResult.GetValueForOption(file),
                BuildArgVariables = bindingContext.ParseResult.GetValueForOption(buildArgs) ?? new List<string>(),
                MetadataLabels = bindingContext.ParseResult.GetValueForOption(labels) ?? new List<string?>(),
                Target = bindingContext.ParseResult.GetValueForOption(target),
                Platform = bindingContext.ParseResult.GetValueForOption(platform),
                Quiet = bindingContext.ParseResult.GetValueForOption(quiet),
                IIDFile = bindingContext.ParseResult.GetValueForOption(iidFile),
            };
        }
    }
}