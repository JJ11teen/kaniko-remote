using System.Text.Json;
using k8s.Models;
using KanikoRemote.CLI;

namespace KanikoRemote
{
    internal class KanikoRemoteException : Exception
    {
        public KanikoRemoteException(params string[] messages) : base(string.Join("\n", messages)) { }

        // KanikoRemote specific exceptions don't show stack traces
        public override string? StackTrace => null;
    }
    internal class InvalidConfigException : KanikoRemoteException
    {
        public InvalidConfigException(string message, string relevantJson) : base(message, relevantJson) { }
    }

    internal class KubernetesPermissionException : KanikoRemoteException
    {
        private const string RequiredPermissionsText = @"kaniko-remote requires: create, get, watch, delete for pods, pods/exec, pods/log in it's configured namespace";
        public KubernetesPermissionException(string message): base(message, RequiredPermissionsText) { }
    }

    internal class KanikoException : KanikoRemoteException
    {
        public KanikoException(string message, V1ContainerStateTerminated terminatedContainerState) : base(
            message,
            "Terminated container state: ",
            JsonSerializer.Serialize(terminatedContainerState, typeof(V1ContainerStateTerminated), LoggerSerialiserContext.Default))
        { }
    }
}