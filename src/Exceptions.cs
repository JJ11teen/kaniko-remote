using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using k8s.Models;
using KanikoRemote.CLI;

namespace KanikoRemote
{
    internal class InvalidConfigException : Exception
    {
        private string relevantJson;
        public InvalidConfigException(string message, string relevantJson) : base(message)
        {
            this.relevantJson = relevantJson;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(this.GetType().ToString());
            sb.AppendLine(this.Message);
            sb.AppendLine(relevantJson);
            return sb.ToString();
        }
    }

    internal class KanikoException : Exception
    {
        private V1ContainerStateTerminated terminatedContainerState;
        public KanikoException(string message, V1ContainerStateTerminated terminatedContainerState) : base(message)
        {
            this.terminatedContainerState = terminatedContainerState;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(this.GetType().ToString());
            sb.Append(": ");
            sb.AppendLine(this.Message);
            sb.Append("Terminated container state: ");
            sb.AppendLine(JsonSerializer.Serialize(terminatedContainerState, typeof(V1ContainerStateTerminated), LoggerSerialiserContext.Default));
            return sb.ToString();
        }
    }
}