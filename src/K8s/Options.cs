namespace KanikoRemote.K8s
{
    internal class KubernetesOptions
    {
        public string? Kubeconfig { get; private set; }
        public string? Context { get; private set; }
        public string Namespace { get; private set; }
        public KubernetesOptions()
        {
            Kubeconfig = null;
            Context = null;
            Namespace = "default";
        }
    }
}