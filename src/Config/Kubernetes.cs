namespace KanikoRemote.Config
{
    internal record KubernetesConfiguration : ConfigurableSection
    {
        public string? Kubeconfig { get; init; }
        public string? Context { get; init; }
        public string Namespace { get; init; } = "default";
    }
}