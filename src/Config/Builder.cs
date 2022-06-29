namespace KanikoRemote.Config
{
    internal record BuilderConfiguration : ConfigurableSection
    {
        public string Name { get; set; } = Environment.UserName;
        public string CPU { get; set; } = "1";
        public string Memory { get; set; } = "1G";
        public string KanikoImage { get; set; } = "gcr.io/kaniko-project/executor:latest";
        public string SetupImage { get; set; } = "busybox:stable";
        public IDictionary<string, string> AdditionalLabels { get; set; } = new Dictionary<string, string>();
        public IDictionary<string, string> AdditionalAnnotations { get; set; } = new Dictionary<string, string>();
        public IList<string> AdditionalKanikoArgs { get; set; } = new List<string>() { "--use-new-run", "--compressed-caching=false" };
        public int PodStartTimeout { get; set; } = 5 * 60; // 5 minutes
        public int PodTransferPacketSize { get; set; } = 14 * 1024; // 14kB
        public string KeepPod { get; set; } = "false";
    }
}