using k8s.Models;

namespace KanikoRemote.K8s
{
    internal static class Specs
    {
        public static V1Pod GeneratePodSpec(
            string name,
            string cpu,
            string memory,
            string kanikoImage,
            string setupImage,
            IDictionary<string, string> additionalLabels,
            IDictionary<string, string> additionalAnnotations)
        {
            var dockerConfigVolumeMounts = new List<V1VolumeMount>()
            {
                new V1VolumeMount
                {
                    Name = "config",
                    MountPath = "/kaniko/.docker"
                }
            };

            var resourcesDict = new Dictionary<string, ResourceQuantity>()
            {
                { "cpu", new ResourceQuantity(cpu) },
                { "memory", new ResourceQuantity(memory) }
            };
            var resources = new V1ResourceRequirements()
            {
                Requests = resourcesDict,
                Limits = resourcesDict,
            };

            var labels = new Dictionary<string, string>(additionalLabels);
            labels["app.kubernetes.io/name"] = "kaniko-remote";
            labels["app.kubernetes.io/component"] = "builder";
            labels["kaniko-remote/builder-name"] = name;

            var annotations = new Dictionary<string, string>(additionalAnnotations);
            // TODO:
            // labels["kanaiko-remote/version"] = __version__;

            return new V1Pod
            {
                Metadata = new V1ObjectMeta
                {
                    GenerateName = $"kaniko-remote-{name}-",
                    Labels = labels,
                    Annotations = annotations,
                },
                Spec = new V1PodSpec
                {
                    AutomountServiceAccountToken = false,
                    InitContainers = new List<V1Container>()
                    {
                        new V1Container
                        {
                            Name = "setup",
                            Image = setupImage,
                            Command = new List<string> { "sh", "-c" },
                            // Args = new List<string> { "trap : TERM INT; sleep 9999999999d & wait" },
                            Args = new List<string> { "until [ -e /kaniko/.docker/config.json ]; do sleep 1; done" },
                            VolumeMounts = dockerConfigVolumeMounts,
                            Resources = resources,
                        }
                    },
                    Containers = new List<V1Container>()
                    {
                        new V1Container
                        {
                            Name = "builder",
                            Image = kanikoImage,
                            VolumeMounts = dockerConfigVolumeMounts,
                            Resources = resources
                        }
                    },
                    Volumes = new List<V1Volume>()
                    {
                        new V1Volume()
                        {
                            Name = "config",
                            EmptyDir = new V1EmptyDirVolumeSource()
                        }
                    }
                }
            };
        }

        public static V1Pod MountContextForExecTransfer(V1Pod pod)
        {
            pod.Spec.Volumes.Add(new V1Volume()
            {
                Name = "context",
                EmptyDir = new V1EmptyDirVolumeSource()
            });

            pod.Spec.Containers.First().VolumeMounts.Add(new V1VolumeMount()
            {
                Name = "context",
                MountPath = "/workspace"
            });
            return pod;
        }

        public static V1Pod SetKanikoArgs(V1Pod pod, IEnumerable<string> args)
        {
            pod.Spec.Containers.First().Command = null;
            pod.Spec.Containers.First().Args = args.ToList();

            return pod;
        }

        public static V1Pod ReplaceServiceAccount(V1Pod pod, string serviceAccountName)
        {
            pod.Spec.ServiceAccountName = serviceAccountName;
            pod.Spec.AutomountServiceAccountToken = true;
            return pod;
        }

        public static V1Pod AppendEnvFromSecret(V1Pod pod, string secretName)
        {
            var container = pod.Spec.Containers.First();
            if (container.EnvFrom == null)
            {
                container.EnvFrom = new List<V1EnvFromSource>();
            }
            container.EnvFrom.Add(new V1EnvFromSource()
            {
                SecretRef = new V1SecretEnvSource()
                {
                    Name = secretName,
                },
            });
            return pod;
        }

        public static V1Pod AppendEnvFromConfigMap(V1Pod pod, string configMapName)
        {
            var container = pod.Spec.Containers.First();
            if (container.EnvFrom == null)
            {
                container.EnvFrom = new List<V1EnvFromSource>();
            }
            container.EnvFrom.Add(new V1EnvFromSource()
            {
                ConfigMapRef = new V1ConfigMapEnvSource()
                {
                    Name = configMapName,
                }
            });
            return pod;
        }

        public static V1Pod AppendEnvVar(V1Pod pod, string envVarName, string envVarValue)
        {
            var container = pod.Spec.Containers.First();
            if (container.Env == null)
            {
                container.Env = new List<V1EnvVar>();
            }
            container.Env.Add(new V1EnvVar()
            {
                Name = envVarName,
                Value = envVarValue
            });
            return pod;
        }

        public static V1Pod AppendVolumeFromSecret(V1Pod pod, string secretName, string mountPath)
        {
            var mounts = pod.Spec.Containers.First().VolumeMounts;
            var volumes = pod.Spec.Volumes;
            mounts.Add(new V1VolumeMount()
            {
                Name = secretName,
                MountPath = mountPath,
                ReadOnlyProperty = true,
            });
            volumes.Add(new V1Volume()
            {
                Name = secretName,
                Secret = new V1SecretVolumeSource()
                {
                    SecretName = secretName,
                }
            });
            return pod;
        }

        public static V1Pod AppendVolumeFromConfigMap(V1Pod pod, string configMapName, string mountPath)
        {
            var mounts = pod.Spec.Containers.First().VolumeMounts;
            var volumes = pod.Spec.Volumes;
            mounts.Add(new V1VolumeMount()
            {
                Name = configMapName,
                MountPath = mountPath,
                ReadOnlyProperty = true,
            });
            volumes.Add(new V1Volume()
            {
                Name = configMapName,
                ConfigMap = new V1ConfigMapVolumeSource()
                {
                    Name = configMapName
                }
            });
            return pod;
        }
    }
}