using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Auth
{
    internal abstract class Authoriser
    {
        public string? URLToMatch { private set; get; }
        public bool AlwaysMount() => URLToMatch == null;

        abstract public JsonObject AppendAuthToDockerConfig(JsonObject dockerConfig);
        abstract public V1Pod AppendAuthToPod(V1Pod pod);

        protected ILogger<Authoriser> logger;

        public Authoriser(JsonObject options, ILogger<Authoriser> logger)
        {
            this.logger = logger;

            var url = options["url"]?.GetValue<string>();
            var mountStr = options["mount"]?.GetValue<string>();
            var alwaysMount = false;

            if (mountStr != null)
            {
                if (mountStr == "always")
                {
                    alwaysMount = true;
                }
                else if (mountStr != "onMatch")
                {
                    throw new InvalidConfigException($"auth 'mount' must be one of 'onMatch' or 'always'", options.ToJsonString());
                }
            }

            if ((url != null && alwaysMount) || (url == null && !alwaysMount))
            {
                throw new InvalidConfigException($"auth 'url' must be set unless 'mount' set to 'always'", options.ToJsonString());
            }
            this.URLToMatch = url;
        }
    }
}