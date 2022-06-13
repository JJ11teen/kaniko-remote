namespace KanikoRemote.Builder
{
    internal class BuilderArguments
    {
        public IList<string> DestinationTags;
        public string? RelativeDockfilePath;
        public IList<string> BuildArgVariables;
        public IList<string> MetadataLabels;
        public string? Target;
        public string? Platform;
        public bool Quiet;
        public string? IIDFile;
        public string ContextLocation;

        public BuilderArguments(string contextLocation)
        {
            this.DestinationTags = new List<string>();
            this.BuildArgVariables = new List<string>();
            this.MetadataLabels = new List<string>();
            this.Quiet = false;
            this.ContextLocation = contextLocation;
        }
    }
}