namespace KanikoRemote.Builder
{
    internal class BuilderOptions
    {
        public string Name { get; private set; }
        public string CPU { get; private set; }
        public string Memory { get; private set; }
        public string KanikoImage { get; private set; }
        public string SetupImage { get; private set; }
        public IDictionary<string, string> AdditionalLabels { get; private set; }
        public IDictionary<string, string> AdditionalAnnotations { get; private set; }
        public IList<string> AdditionalKanikoArgs { get; private set; }
        public int PodStartTimeout { get; private set; }
        public int PodTransferPacketSize { get; private set; }
        public BuilderOptions()
        {
            Name = Environment.UserName;
            CPU = "1";
            Memory = "1G";
            KanikoImage = "gcr.io/kaniko-project/executor:latest";
            SetupImage = "busybox:stable";
            AdditionalLabels = new Dictionary<string, string>();
            AdditionalAnnotations = new Dictionary<string, string>();
            AdditionalKanikoArgs = new List<string>() { "--use-new-run" };
            PodStartTimeout = 5 * 60; // 5 minutes
            PodTransferPacketSize = (int)14e3; // 14kB
        }
    }
}