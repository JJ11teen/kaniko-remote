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

        public static Config LoadConfig(ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<ConfigLoader>();

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
                    logger.LogWarning($"No configuration file found at {envVarLocation}");
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
                logger.LogInformation($"Using configuration file {configLocation}");
                json = LoadYamlFileAsJsonObject(configLocation);
            }
            else
            {
                logger.LogInformation("Using default configuration");
                json = new JsonObject();
            }

            var config = new Config(
                Kubernetes: ParseAndRemoveStaticConfigSection<KubernetesConfiguration>(json, "kubernetes"),
                Builder: ParseAndRemoveStaticConfigSection<BuilderConfiguration>(json, "builder"),
                Tagger: ParseAndRemoveStaticConfigSection<TaggerConfiguration>(json, "tags"),
                Authorisers: ParseAuthorisers(json, loggerFactory));
            
            if (json.Count > 0)
            {
                throw new InvalidConfigException("Allowed top level options are 'kubernetes', 'builder', 'tags' and 'auth'", json.ToJsonString());
            }

            if (config.Authorisers.Count == 0)
            {
                logger.LogInformation("No auth configured. This is unlikely to work in a production environment.");
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
                    throw new InvalidConfigException($"Empty configuration section '{sectionName}'", rootConfig.ToJsonString());
                }
                if (deserialised.HasExtraJson)
                {
                    throw new InvalidConfigException($"Unknown properties in '{sectionName}'", sectionNode.ToJsonString());
                }
                return deserialised;
            }
            return new T();
        }

        private static List<Authoriser> ParseAuthorisers(JsonObject rootConfig, ILoggerFactory loggerFactory)
        {
            var authorisers = new List<Authoriser>();

            if (rootConfig.Remove("auth", out var authNode))
            {
                foreach (var authJsonNode in authNode!.AsArray())
                {
                    if (authJsonNode == null)
                    {
                        throw new InvalidConfigException($"'auth' must be an array of non empty options", authNode.ToJsonString());
                    }

                    var authOption = authJsonNode.AsObject();
                    var authType = authOption["type"]?.GetValue<string>();

                    Authoriser auth;
                    if (authType != null || authType == "pod-only")
                    {
                        auth = new PodOnlyAuth(authOption, loggerFactory.CreateLogger<PodOnlyAuth>());
                    }
                    else if (authType == "acr")
                    {
                        auth = new ACRAuth(authOption, loggerFactory.CreateLogger<ACRAuth>());
                    }
                    else
                    {
                        throw new InvalidConfigException($"Unknown auth type {authType}", authOption.ToJsonString());
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