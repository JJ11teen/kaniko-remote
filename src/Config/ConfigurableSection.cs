using System.Text.Json;
using System.Text.Json.Serialization;

namespace KanikoRemote.Config
{
    internal record ConfigurableSection
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraJson { get; set; }

        [JsonIgnore]
        public bool HasExtraJson => ExtraJson != null;
    }
}