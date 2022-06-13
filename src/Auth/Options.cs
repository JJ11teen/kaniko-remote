namespace KanikoRemote.Auth
{
    internal class AuthEnvOption
    {
        public string? FromSecret { private set; get; }
        public string? FromConfigMap { private set; get; }
    }
    internal class AuthVolumeOption
    {
        public string? FromSecret { private set; get; }
        public string? FromConfigMap { private set; get; }
        public string? MountPath { private set; get; }
    }

    internal class AuthOptions
    {
        public string? URL { private set; get; }
        public string? Mount { private set; get; }
        public string? Type { private set; get; }
        public List<AuthEnvOption> Env { private set; get; }
        public List<AuthVolumeOption> Volumes { private set; get; }

        public AuthOptions()
        {
            Env = new List<AuthEnvOption>();
            Volumes = new List<AuthVolumeOption>();
        }
    }
}