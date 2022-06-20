using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Auth
{
    internal class ACRAuth : PodOnlyAuth
    {
        private string registryHostname; // {name}.azurecr.io
        private string? acrToken;
        public ACRAuth(JsonObject options, ILogger<ACRAuth> logger) : base(options, logger)
        {
            try
            {
                var registry = options["registry"]?.GetValue<string>();
                if (registry != null)
                {
                    this.registryHostname = registry;
                }
                else
                {
                    if (this.AlwaysMount())
                    {
                        throw new InvalidConfigException($"Invalid configuration for ACR auth, must have 'registry' specified or parsable from url", options.ToJsonString());
                    }
                    this.registryHostname = new UriBuilder(this.URLToMatch!).Host;
                }
                this.acrToken = options["token"]?.GetValue<string>();
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidConfigException($"Invalid configuration for ACR auth", options.ToJsonString());
            }
        }

        public override JsonObject AppendAuthToDockerConfig(JsonObject dockerConfig)
        {
            if (this.acrToken != null)
            {
                this.logger.LogWarning("Writing ARC auth token directly into docker config.");
                if (dockerConfig["auths"] == null)
                {
                    dockerConfig["auths"] = new JsonObject();
                }
                dockerConfig["auths"]![this.registryHostname] = new JsonObject()
                {
                    { "auth", Convert.ToBase64String(Encoding.ASCII.GetBytes($"00000000-0000-0000-0000-000000000000:{this.acrToken}")) }
                };
            }

            if (dockerConfig["credHelpers"] == null)
            {
                dockerConfig["credHelpers"] = new JsonObject();
            }
            dockerConfig["credHelpers"]![this.registryHostname] = "acr-env";

            return dockerConfig;
        }
    }
}