using System.Text.Json;
using System.Text.Json.Nodes;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Auth
{
    internal class PodOnlyAuth : Authoriser
    {
        private enum ResourceKind
        {
            Secret,
            ConfigMap,
        }

        private string? serviceAccount;
        private Dictionary<string, string> rawEnvVars;
        private Dictionary<string, ResourceKind> mountedEnvs;
        private Dictionary<string, Tuple<string, ResourceKind>> mountedVols;

        public PodOnlyAuth(JsonObject options, ILogger<PodOnlyAuth> logger) : base(options, logger)
        {
            this.serviceAccount = null;
            this.rawEnvVars = new Dictionary<string, string>();
            this.mountedEnvs = new Dictionary<string, ResourceKind>();
            this.mountedVols = new Dictionary<string, Tuple<string, ResourceKind>>();

            // might need this?
            // this.serviceAccount = options.TryGetProperty("service_account", out var sa) ? sa.GetString() : null;
            this.serviceAccount = options["serviceAccount"]?.GetValue<string>();
            foreach (var envNode in options["env"]?.AsArray() ?? new JsonArray())
            {
                if (envNode == null) continue;
                var env = envNode.AsObject();

                if (env.Count == 1 && env.Single().Key == "fromSecret")
                {
                    var secret = env.Single().Value?.GetValue<string>();
                    if (secret == null)
                    {
                        throw new InvalidConfigException("Invalid 'fromSecret' value", env);
                    }
                    this.mountedEnvs.Add(secret, ResourceKind.Secret);
                }
                else if (env.Count == 1 && env.Single().Key == "fromConfigMap")
                {
                    var configMap = env.Single().Value?.GetValue<string>();
                    if (configMap == null)
                    {
                        throw new InvalidConfigException("Invalid 'fromConfigMap' value", env);
                    }
                    this.mountedEnvs.Add(configMap, ResourceKind.ConfigMap);
                }
                else if (env.Count == 2 && env.Any(p => p.Key == "name") && env.Any(p => p.Key == "value"))
                {
                    var rawName = env.Single(p => p.Key == "name").Value?.GetValue<string>();
                    var rawValue = env.Single(p => p.Key == "value").Value?.GetValue<string>();
                    if (rawName == null)
                    {
                        throw new InvalidConfigException("Invalid 'name' value", env);
                    }
                    if (rawValue == null)
                    {
                        throw new InvalidConfigException("Invalid 'value' value", env);
                    }
                    this.rawEnvVars.Add(rawName, rawValue);
                }
                else
                {
                    throw new InvalidConfigException($"Env must only have 'fromSecret' or 'fromConfigMap' or ('name' and 'value')", env);
                }
            }
            foreach (var volNode in options["vol"]?.AsArray() ?? new JsonArray())
            {
                if (volNode == null) continue;
                var vol = volNode.AsObject();

                if (vol.Count != 2 || !vol.Any(p => p.Key == "mountPath"))
                {
                    throw new InvalidConfigException($"env must have 'mountPath' and ('fromSecret' or 'fromConfigMap')", vol);
                }

                var mountPath = vol.Single(p => p.Key == "mountPath").Value?.GetValue<string>();
                if (mountPath == null)
                {
                    throw new InvalidConfigException($"Invalid 'mountPath' value", vol);
                }

                if (vol.Any(p => p.Key == "fromSecret"))
                {
                    var secret = vol.Single(p => p.Key == "fromSecret").Value?.GetValue<string>();
                    if (secret == null)
                    {
                        throw new InvalidConfigException("Invalid 'fromSecret' value", vol);
                    }
                    this.mountedVols.Add(secret, Tuple.Create(mountPath, ResourceKind.Secret));
                }
                if (vol.Any(p => p.Key == "fromConfigMap"))
                {
                    var configMap = vol.Single(p => p.Key == "fromConfigMap").Value?.GetValue<string>();
                    if (configMap == null)
                    {
                        throw new InvalidConfigException("Invalid 'fromConfigMap' value", vol);
                    }
                    this.mountedVols.Add(configMap, Tuple.Create(mountPath, ResourceKind.Secret));
                }
                else
                {
                    throw new InvalidConfigException($"Env must have 'mountPath' and ('fromSecret' or 'fromConfigMap')", vol);
                }
            }
        }

        public override JsonObject AppendAuthToDockerConfig(JsonObject dockerConfig) => dockerConfig;

        public override V1Pod AppendAuthToPod(V1Pod pod)
        {
            if (this.serviceAccount != null)
            {
                pod = K8s.Specs.ReplaceServiceAccount(pod, this.serviceAccount);
            }
            foreach (var (name, value) in this.rawEnvVars)
            {
                pod = K8s.Specs.AppendEnvVar(pod, name, value);
            }
            foreach (var (resource, kind) in this.mountedEnvs)
            {
                if (kind == ResourceKind.Secret)
                {
                    pod = K8s.Specs.AppendEnvFromSecret(pod, resource);
                }
                else
                {
                    pod = K8s.Specs.AppendEnvFromConfigMap(pod, resource);
                }
            }
            foreach (var (resource, (mountPath, resourceKind)) in this.mountedVols)
            {
                if (resourceKind == ResourceKind.Secret)
                {
                    pod = K8s.Specs.AppendVolumeFromSecret(pod, resource, mountPath);
                }
                else
                {
                    pod = K8s.Specs.AppendVolumeFromConfigMap(pod, resource, mountPath);
                }
            }
            return pod;
        }
    }
}