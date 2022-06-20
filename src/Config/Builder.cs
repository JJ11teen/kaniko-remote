namespace KanikoRemote.Config
{
    internal record BuilderConfiguration : ConfigurableSection
    {
        public string Name { get; init; } = Environment.UserName;
        public string CPU { get; init; } = "1";
        public string Memory { get; init; } = "1G";
        public string KanikoImage { get; init; } = "gcr.io/kaniko-project/executor:latest";
        public string SetupImage { get; init; } = "busybox:stable";
        public IDictionary<string, string> AdditionalLabels { get; init; } = new Dictionary<string, string>();
        public IDictionary<string, string> AdditionalAnnotations { get; init; } = new Dictionary<string, string>();
        public IList<string> AdditionalKanikoArgs { get; init; } = new List<string>() { "--use-new-run" };
        public int PodStartTimeout { get; init; } = 5 * 60; // 5 minutes
        public int PodTransferPacketSize { get; init; } = (int)14e3; // 14kB
        public string KeepPod { get; init; } = "false";
    }
}