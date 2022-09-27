using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using KanikoRemote.CLI;

namespace KanikoRemote.Auth
{
    internal class GCRAuth : PodOnlyAuth
    {
        private string project; // gcr.io/$project/
        public GCRAuth(JsonObject options) : base(options)
        {
            try
            {
                var project = options["project"]?.GetValue<string>();
                if (project != null)
                {
                    this.project = project;
                }
                else
                {
                    if (this.AlwaysMount())
                    {
                        throw KanikoRemoteConfigException.WithJson<JsonObject>($"Invalid configuration for GCR auth, must have 'project' specified or parsable from url", options);
                    }
                    this.project = new UriBuilder(this.URLToMatch!).Path.Split("/").First();
                }
            }
            catch (KeyNotFoundException)
            {
                throw KanikoRemoteConfigException.WithJson<JsonObject>($"Invalid configuration for GCR auth", options);
            }
        }

        public override JsonObject AppendAuthToDockerConfig(JsonObject dockerConfig)
        {
            if (dockerConfig["credHelpers"] == null)
            {
                dockerConfig["credHelpers"] = new JsonObject();
            }
            dockerConfig["credHelpers"]![$"https://gcr.io/{this.project}/"] = "gcr-env";

            return dockerConfig;
        }
    }
}