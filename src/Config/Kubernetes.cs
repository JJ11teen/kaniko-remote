namespace KanikoRemote.Config
{
    internal record KubernetesConfiguration : ConfigurableSection
    {
        public string? Kubeconfig { get; set; }
        public string? Context { get; set; }
        public string? Namespace { get; set; }
    }
}