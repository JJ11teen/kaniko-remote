using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;
using KanikoRemote.CLI;

namespace KanikoRemote
{
    internal class KanikoRemoteException : Exception
    {
        public KanikoRemoteException(string? message) : base(message) { }

        // KanikoRemote specific exceptions don't show stack traces
        public override string? StackTrace => null;
    }
    internal class KanikoRemoteConfigException : KanikoRemoteException
    {
        const string mainMessage = "Invalid config in kaniko-remote.yaml";
        public KanikoRemoteConfigException(string value)
            : base($"{mainMessage} - {value}") { }
        private KanikoRemoteConfigException(string value, string json)
            : base($"{mainMessage} - {value}\n{json}") { }

        public static KanikoRemoteConfigException WithJson<T>(string value, object? json)
        {
            if (json == null)
                return new KanikoRemoteConfigException(value, "null");
            if (json is JsonObject jsonO)
                return new KanikoRemoteConfigException(value, jsonO.ToJsonString());
            if (json is JsonNode jsonN)
                return new KanikoRemoteConfigException(value, jsonN.ToJsonString());
            
            return new KanikoRemoteConfigException(value,
                JsonSerializer.Serialize(json, typeof(T), PrecompiledSerialiser.Default));
        }
    }

    internal class KubernetesConfigException : KanikoRemoteException
    {
        public KubernetesConfigException() : base("Error reading kubeconfig") { }
    }

    internal class KubernetesPermissionException : KanikoRemoteException
    {
        private const string permissionNote = "kaniko-remote requires access to the following namespaced k8s apis: create, get, watch, delete for pods, pods/exec, pods/log";
        public KubernetesPermissionException(string message)
            : base($"{message} - {permissionNote}") { }
    }

    internal class LocalContextException : KanikoRemoteException
    {
        private const string mainMessage = "Error with local context";
        public LocalContextException(string details)
            : base($"{mainMessage} - {details}") { }
    }

    internal class KanikoRuntimeException : KanikoRemoteException
    {
        private const string message = "Kaniko failed to build and/or push image, increase verbosity if kaniko logs are not visible above";
        public KanikoRuntimeException(string message) : base(message) { }
        public KanikoRuntimeException(string message, V1ContainerStateTerminated terminatedContainerState)
            : base(message + "\nTerminated container state: " +
                JsonSerializer.Serialize(terminatedContainerState, typeof(V1ContainerStateTerminated), PrecompiledSerialiser.Default))
        { }
    }
}