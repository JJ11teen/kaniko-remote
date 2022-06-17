using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;

namespace KanikoRemote
{
    internal class InvalidConfigException : Exception
    {
        private JsonNode node;
        public InvalidConfigException(string message, JsonNode node) : base(message)
        {
            this.node = node;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(this.GetType().ToString());
            sb.Append(": ");
            sb.AppendLine(this.Message);
            sb.Append("Relevant config section: ");
            sb.AppendLine(node.ToString());
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
            sb.AppendLine(JsonSerializer.Serialize(terminatedContainerState, new JsonSerializerOptions() { WriteIndented = true }));
            return sb.ToString();
        }
    }
}