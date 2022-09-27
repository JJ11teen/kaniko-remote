using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;
using Microsoft.Extensions.Logging;
using KanikoRemote.CLI;

namespace KanikoRemote.Auth
{
    internal abstract class Authoriser
    {
        public string? URLToMatch { private set; get; }
        public bool AlwaysMount() => URLToMatch == null;

        abstract public JsonObject AppendAuthToDockerConfig(JsonObject dockerConfig);
        abstract public V1Pod AppendAuthToPod(V1Pod pod);

        public Authoriser(JsonObject options)
        {
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
                    throw KanikoRemoteConfigException.WithJson<JsonObject>($"auth 'mount' must be one of 'onMatch' or 'always'", options);
                }
            }

            if ((url != null && alwaysMount) || (url == null && !alwaysMount))
            {
                throw KanikoRemoteConfigException.WithJson<JsonObject>($"auth 'url' must be set unless 'mount' set to 'always'", options);
            }
            this.URLToMatch = url;
        }
    }
}