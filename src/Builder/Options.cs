using System.Text.Json.Serialization;

namespace KanikoRemote.Builder
{
    internal class BuilderOptions
    {
        public string Name { get; set; }
        public string CPU { get; set; }
        public string Memory { get; set; }
        public string KanikoImage { get; set; }
        public string SetupImage { get; set; }
        public IDictionary<string, string> AdditionalLabels { get; set; }
        public IDictionary<string, string> AdditionalAnnotations { get; set; }
        public IList<string> AdditionalKanikoArgs { get; set; }
        public int PodStartTimeout { get; set; }
        public int PodTransferPacketSize { get; set; }
        public string KeepPod { get; set; }

        [JsonIgnore]
        public bool KeepPodValue => KeepPod == "true";

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
            KeepPod = "false";
        }
    }
}