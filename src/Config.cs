using KanikoRemote.K8s;

namespace KanikoRemote
{
    internal static class ConfigLoader
    {

    }


    internal class TagOptions
    {
        public string? Default { get; private set; }
        public string? Static { get; private set; }
        public string? Prefix { get; private set; }
        public Dictionary<string, string>? Regexes { get; private set; }
    }

    internal class Config
    {
        public KubernetesOptions KubernetesOptions { get; set; }
        public BuilderOptions BuilderOptions { get; set; }

        public static Config GetDefaultConfig()
        {
            return new Config(
                new KubernetesOptions(),
                new BuilderOptions()
            );
        }

        public Config(
            KubernetesOptions kubernetesOptions,
            BuilderOptions builderOptions)
        {
            this.KubernetesOptions = kubernetesOptions;
            this.BuilderOptions = builderOptions;
        }
    }
}