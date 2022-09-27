using KanikoRemote.CLI;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KanikoRemote.Auth;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Config
{
    internal readonly record struct Config(KubernetesConfiguration Kubernetes, BuilderConfiguration Builder, TaggerConfiguration Tagger, IList<Authoriser> Authorisers);

    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(KubernetesConfiguration))]
    [JsonSerializable(typeof(BuilderConfiguration))]
    [JsonSerializable(typeof(TaggerConfiguration))]
    internal partial class ConfigSerialiserContext : JsonSerializerContext { }

    internal class ConfigLoader
    {
        private const string ConfigLocationEnvVar = "KANIKO_REMOTE_CONFIG";
        private const string ConfigFileName = ".kaniko-remote.yaml";

        public static Config LoadConfig()
        {
            string? configLocation = null;
            var envVarLocation = Environment.GetEnvironmentVariable(ConfigLocationEnvVar);
            var cwdLocation = Path.Combine(Environment.CurrentDirectory, ConfigFileName);
            var userLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ConfigFileName);

            if (envVarLocation != null)
            {
                if (File.Exists(envVarLocation))
                {
                    configLocation = envVarLocation;
                }
                else
                {
                    SimpleLogger.WriteWarn($"No configuration file found at {envVarLocation}");
                }
            }
            else if (File.Exists(cwdLocation))
            {
                configLocation = cwdLocation;
            }
            else if (File.Exists(userLocation))
            {
                configLocation = userLocation;
            }

            JsonObject json;
            if (configLocation != null)
            {
                SimpleLogger.WriteInfo($"Using configuration file {configLocation}");
                json = LoadYamlFileAsJsonObject(configLocation);
            }
            else
            {
                SimpleLogger.WriteInfo("Using default configuration");
                json = new JsonObject();
            }

            var config = new Config(
                Kubernetes: ParseAndRemoveStaticConfigSection<KubernetesConfiguration>(json, "kubernetes"),
                Builder: ParseAndRemoveStaticConfigSection<BuilderConfiguration>(json, "builder"),
                Tagger: ParseAndRemoveStaticConfigSection<TaggerConfiguration>(json, "tags"),
                Authorisers: ParseAuthorisers(json));
            
            if (json.Count > 0)
            {
                throw KanikoRemoteConfigException.WithJson<JsonObject>("Allowed top level options are 'kubernetes', 'builder', 'tags' and 'auth'", json);
            }

            if (config.Authorisers.Count == 0)
            {
                SimpleLogger.WriteWarn("No auth configured. This is unlikely to work in a production environment.");
            }
            return config;
        }

        private static T ParseAndRemoveStaticConfigSection<T>(JsonObject rootConfig, string sectionName) where T : ConfigurableSection, new()
        {
            var jsonOptions = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                
            };

            if (rootConfig.Remove(sectionName, out var sectionNode))
            {
                T? deserialised = (T?)JsonSerializer.Deserialize(sectionNode, typeof(T), ConfigSerialiserContext.Default);
                if (sectionNode == null || deserialised == null)
                {
                    throw KanikoRemoteConfigException.WithJson<JsonObject>($"Empty configuration section '{sectionName}'", rootConfig);
                }
                if (deserialised.HasExtraJson)
                {
                    throw KanikoRemoteConfigException.WithJson<JsonObject>($"Unknown properties in '{sectionName}'", sectionNode);
                }
                return deserialised;
            }
            return new T();
        }

        private static List<Authoriser> ParseAuthorisers(JsonObject rootConfig)
        {
            var authorisers = new List<Authoriser>();

            if (rootConfig.Remove("auth", out var authNode))
            {
                foreach (var authJsonNode in authNode!.AsArray())
                {
                    if (authJsonNode == null)
                    {
                        throw KanikoRemoteConfigException.WithJson<JsonNode>($"'auth' must be an array of non empty options", authNode);
                    }

                    var authOption = authJsonNode.AsObject();
                    var authType = authOption["type"]?.GetValue<string>();

                    Authoriser auth;
                    if (authType != null || authType == "pod-only")
                    {
                        auth = new PodOnlyAuth(authOption);
                    }
                    else if (authType == "acr")
                    {
                        auth = new ACRAuth(authOption);
                    }
                    else if (authType == "docker-hub")
                    {
                        auth = new DockerHubAuth(authOption);
                    }
                    else if (authType == "gcr")
                    {
                        auth = new GCRAuth(authOption);
                    }
                    else
                    {
                        throw KanikoRemoteConfigException.WithJson<JsonNode>($"Unknown auth type {authType}", authOption);
                    }

                    authorisers.Add(auth);
                }
            }
            return authorisers;
        }

        private static JsonObject LoadYamlFileAsJsonObject(string file)
        {
            using var sr = new StreamReader(file);
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
            var yamlObject = deserializer.Deserialize(sr);

            if (yamlObject == null)
            {
                throw new FileLoadException("Configuration file does not contain valid yaml");
            }

            var serializer = new YamlDotNet.Serialization.SerializerBuilder().JsonCompatible().Build();
            var jsonString = serializer.Serialize(yamlObject);

            if (jsonString == null)
            {
                throw new FileLoadException("Configuration file does not contain valid yaml");
            }

            return JsonNode.Parse(jsonString)?.AsObject() ?? new JsonObject();
        }        
    }
}