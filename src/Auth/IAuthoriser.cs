using System.Text.Json.Nodes;
using k8s.Models;

namespace KanikoRemote.Auth
{
    internal interface IAuthoriser
    {
        public string GetName();
        public JsonObject AppendAuthToDockerConfig(JsonObject dockerConfig);
        public V1Pod AppendAuthToPod(V1Pod pod);

    }
}