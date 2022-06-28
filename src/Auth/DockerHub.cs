using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Auth
{
    internal class DockerHubAuth : Authoriser
    {
        private string username;
        private string password;
        public DockerHubAuth(JsonObject options, ILogger<DockerHubAuth> logger) : base(options, logger)
        {
            try
            {
                var username = options["username"]?.GetValue<string>();
                var password = options["password"]?.GetValue<string>();
                if (username == null || password == null)
                {
                    throw new InvalidConfigException($"Invalid configuration for DockerHub auth, must have 'username' and 'password' specified", options.ToJsonString());
                }
                this.username = username;
                this.password = password;
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidConfigException($"Invalid configuration for ACR auth", options.ToJsonString());
            }
        }

        public override JsonObject AppendAuthToDockerConfig(JsonObject dockerConfig)
        {
            if (dockerConfig["auths"] == null)
            {
                dockerConfig["auths"] = new JsonObject();
            }
            dockerConfig["auths"]!["https://index.docker.io/v1/"] = new JsonObject()
            {
                { "auth", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{this.username}:{this.password}")) }
            };
            return dockerConfig;
        }

        public override V1Pod AppendAuthToPod(V1Pod pod)
        {
            return pod;
        }
    }
}