namespace KanikoRemote.Builder
{
    internal record BuildArguments
    {
        public IList<string> DestinationTags { get; init; } = new List<string>();
        public string? RelativeDockfilePath { get; init; }
        public IList<string> BuildArgVariables { get; init; } = new List<string>();
        public IList<string> MetadataLabels { get; init; } = new List<string>();
        public string? Target { get; init; }
        public string? Platform { get; init; }
        public bool Quiet { get; init; } = false;
        public string? IIDFile { get; init; }
        public string ContextLocation { get; init; }

        public BuildArguments(string contextLocation)
        {
            ContextLocation = contextLocation;
        }

        public IEnumerable<string> SerialiseKanikoPassthroughArgs()
        {
            var args = new List<string>();

            if (this.Target != null)
            {
                args.Add("--target");
                args.Add(this.Target);
            }
            if (this.Platform != null)
            {
                args.Add("--customPlatform");
                args.Add(this.Platform);
            }

            return args
                .Concat(this.MetadataLabels.Select(l => $"--label={l}"))
                .Concat(this.BuildArgVariables.SelectMany(ba => new List<string>() { "--build-arg", ba }));
        }

        // public BuilderArguments(string contextLocation)
        // {
        //     this.DestinationTags = new List<string>();
        //     this.BuildArgVariables = new List<string>();
        //     this.MetadataLabels = new List<string>();
        //     this.Quiet = false;
        //     this.ContextLocation = contextLocation;
        // }
    }
}