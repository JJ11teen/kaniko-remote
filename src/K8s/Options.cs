namespace KanikoRemote.K8s
{
    internal class KubernetesOptions
    {
        public string? Kubeconfig { get; set; }
        public string? Context { get; set; }
        public string Namespace { get; set; }
        public KubernetesOptions()
        {
            Kubeconfig = null;
            Context = null;
            Namespace = "default";
        }
    }
}