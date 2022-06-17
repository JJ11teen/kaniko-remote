using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KanikoRemote.Auth;
using KanikoRemote.Builder;
using KanikoRemote.K8s;
using KanikoRemote.Tagger;
using Microsoft.Extensions.Logging;

namespace KanikoRemote
{
    internal class Config
    {
        private ILoggerFactory loggerFactory;
        private ILogger<Config> logger;

        public readonly string? ConfigLocation;
        public KubernetesOptions KubernetesOptions { get; set; }
        public BuilderOptions BuilderOptions { get; set; }
        public TaggerOptions TaggerOptions { get; set; }
        public IList<Authoriser> Authorisers { get; set; }

        private const string ConfigLocationEnvVar = "KANIKO_REMOTE_CONFIG";
        private const string ConfigFileName = ".kaniko-remote.yaml";


        public Config(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<Config>();
            this.loggerFactory = loggerFactory;


            var envVarLocation = Environment.GetEnvironmentVariable(ConfigLocationEnvVar);
            var cwdLocation = Path.Combine(Environment.CurrentDirectory, ConfigFileName);
            var userLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ConfigFileName);

            if (envVarLocation != null)
            {
                if (File.Exists(envVarLocation))
                {
                    this.ConfigLocation = envVarLocation;
                }
                else
                {
                    this.logger.LogWarning($"No configuration file found at {envVarLocation}");
                }
            }
            else if (File.Exists(cwdLocation))
            {
                this.ConfigLocation = cwdLocation;
            }
            else if (File.Exists(userLocation))
            {
                this.ConfigLocation = userLocation;
            }

            JsonObject json;
            if (this.ConfigLocation != null)
            {
                this.logger.LogInformation($"Using configuration file {this.ConfigLocation}");
                json = LoadYamlFileAsJsonObject(this.ConfigLocation);
            }
            else
            {
                this.logger.LogInformation("Using default configuration");
                json = new JsonObject();
            }

            var jsonOptions = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            this.KubernetesOptions = json["kubernetes"].Deserialize<KubernetesOptions>(jsonOptions) ?? new KubernetesOptions();
            this.BuilderOptions = json["builder"].Deserialize<BuilderOptions>(jsonOptions) ?? new BuilderOptions();
            this.TaggerOptions = json["tags"].Deserialize<TaggerOptions>(jsonOptions) ?? new TaggerOptions();
            this.Authorisers = this.ParseAuthorisers(json["auth"]?.AsArray());
        }

        private List<Authoriser> ParseAuthorisers(JsonArray? authJson)
        {
            var authorisers = new List<Authoriser>();
            foreach (var authJsonNode in authJson ?? new JsonArray())
            {
                if (authJsonNode == null)
                {
                    throw new InvalidConfigException($"'auth' must be an array of non empty options", authJson!);
                }

                var authOption = authJsonNode.AsObject();
                var authType = authOption["type"]?.GetValue<string>();

                Authoriser auth;
                if (authType != null || authType == "pod-only")
                {
                    auth = new PodOnlyAuth(authOption, this.loggerFactory.CreateLogger<PodOnlyAuth>());
                }
                else if (authType == "acr")
                {
                    auth = new ACRAuth(authOption, this.loggerFactory.CreateLogger<ACRAuth>());
                }
                else
                {
                    throw new InvalidConfigException($"Unknown auth type {authType}", authOption);
                }

                authorisers.Add(auth);
            }
            return authorisers;
        }

        private JsonObject LoadYamlFileAsJsonObject(string file)
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