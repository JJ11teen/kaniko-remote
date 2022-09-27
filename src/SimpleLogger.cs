using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using k8s.Models;

namespace KanikoRemote
{
    internal static class SimpleLogger
    {
        public static bool DebugEnabled = false;
        public static bool InfoEnabled = true;
        public static bool WarnEnabled = true;
        public static bool ErrorEnabled = true;

        public static void WritePlainText(string text)
        {
            Console.Write(text);
        }

        public static void WriteProgression(string text)
        {
            Console.Write(text);
            Console.Write("\r");
        }

        private const string LogPrefix = "[KANIKO-REMOTE] ";

        private static void WriteSeverityTag(string tag, ConsoleColor color)
        {
            Console.Write("(");
            Console.ForegroundColor = color;
            Console.Write(tag);
            Console.ResetColor();
            Console.Write(") ");
        }

        public static void WriteDebug(string text)
        {
            if (!DebugEnabled) return;
            Console.Write(LogPrefix);
            WriteSeverityTag("debug", ConsoleColor.Gray);
            Console.WriteLine(text);
        }

        public static void WriteInfo(string text)
        {
            if (!InfoEnabled) return;
            Console.Write(LogPrefix);
            WriteSeverityTag("info", ConsoleColor.Blue);
            Console.WriteLine(text);
        }

        public static void WriteWarn(string text)
        {
            if (!WarnEnabled) return;
            Console.Write(LogPrefix);
            WriteSeverityTag("warning", ConsoleColor.Yellow);
            Console.WriteLine(text);
        }

        public static void WriteError(string text)
        {
            if (!ErrorEnabled) return;
            Console.Write(LogPrefix);
            WriteSeverityTag("error", ConsoleColor.Red);
            Console.WriteLine(text);
        }

        public static void WriteDebugJson<T>(string label, object? json)
        {
            string jsonString;
            if (json == null)
                jsonString = "null";
            else if (json is JsonObject jsonO)
                jsonString = jsonO.ToJsonString();
            else if (json is JsonNode jsonN)
                jsonString = jsonN.ToJsonString();
            else
                jsonString = JsonSerializer.Serialize(json, typeof(T), PrecompiledSerialiser.Default);
            
            WriteDebug($"{label} {jsonString}");
        }
    }

    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(V1ContainerState))]
    [JsonSerializable(typeof(V1ContainerStateTerminated))]
    internal partial class PrecompiledSerialiser : JsonSerializerContext { }
}