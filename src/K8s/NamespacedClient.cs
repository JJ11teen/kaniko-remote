using Microsoft.Extensions.Logging;
using k8s;
using k8s.Models;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;

namespace KanikoRemote.K8s
{
    internal class NamespacedClient
    {
        private readonly Kubernetes Client;
        private readonly ILogger<NamespacedClient> logger;


        private readonly string? Kubeconfig;
        private readonly string? Context;
        private readonly string Namespace;
        private readonly bool runningInCluster;

        public NamespacedClient(KubernetesOptions options, ILogger<NamespacedClient> logger)
        {
            this.Kubeconfig = options.Kubeconfig;
            this.Context = options.Context;
            this.logger = logger;
            this.runningInCluster = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") != null;

            if (this.runningInCluster && (this.Kubeconfig != null || this.Context != null))
            {
                logger.LogWarning("kubernetes.kubeconfig and kubernetes.context are ignored when running in a kubernetes cluster");
            }

            if (options.Namespace != null)
            {
                // If namespace explicitly set, use that
                this.Namespace = options.Namespace;
            }
            else if (this.runningInCluster)
            {
                // If namespace not specified explicitly and we are in cluster, use current SA namespace:
                this.Namespace = File.ReadAllText("/var/run/secrets/kubernetes.io/serviceaccount/namespace");
            }
            else
            {
                // If namespace not specific explicitly and we are not in cluster, use default
                this.Namespace = "default";
            }

            var config = this.runningInCluster ? KubernetesClientConfiguration.InClusterConfig() : KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigPath: this.Kubeconfig, currentContext: this.Context);
            this.Client = new Kubernetes(config);
        }

        public Task<V1Pod> CreatePodAsync(V1Pod body)
        {
            return this.Client.CreateNamespacedPodAsync(body: body, namespaceParameter: this.Namespace);
        }
        public Task<V1Pod> ReadPodAsync(string podName)
        {
            return this.Client.ReadNamespacedPodAsync(name: podName, namespaceParameter: this.Namespace);
        }
        public Task<V1Pod> DeletePodAsync(string podName)
        {
            return this.Client.DeleteNamespacedPodAsync(name: podName, namespaceParameter: this.Namespace);
        }

        private async IAsyncEnumerable<V1ContainerState> WatchContainerStatesAsync(string podName, string container, int? timeoutSeconds)
        {
            var w = this.Client.CoreV1.ListNamespacedPodWithHttpMessagesAsync(
                namespaceParameter: this.Namespace,
                fieldSelector: $"metadata.name={podName}",
                timeoutSeconds: timeoutSeconds,
                watch: true);
            await foreach (var (watchEventType, pod) in w.WatchAsync<V1Pod, V1PodList>())
            {
                V1ContainerState? state;
                try
                {
                    state = pod.Status.ContainerStatuses
                        .Concat(pod.Status.InitContainerStatuses)
                        .Concat(pod.Status.EphemeralContainerStatuses)
                        .SingleOrDefault(s => s?.Name == podName, null)
                        ?.State;
                }
                catch (InvalidOperationException e)
                {
                    throw new InvalidOperationException($"Pod '{podName}' has more than one container named '{container}'", e);
                }

                if (state != null)
                {
                    yield return state;
                }
            }
        }

        public async Task<V1ContainerStateRunning> AwaitContainerRunningStateAsync(string podName, string container, int? timeoutSeconds = null)
        {
            await foreach (var state in this.WatchContainerStatesAsync(podName, container, timeoutSeconds))
            {
                this.logger.LogTrace($"Waiting for container '{container}' in pod '{podName}' to reach running state, current state: {state}");
                if (state.Running != null)
                {
                    return state.Running;
                }
            }
            throw new TimeoutException($"Container '{container}' in pod '{podName}' did not reach running state before timeout reached");
        }


        public async Task<V1ContainerStateTerminated> AwaitContainerTerminatedStateAsync(string podName, string container, int? timeoutSeconds = null)
        {
            await foreach (var state in this.WatchContainerStatesAsync(podName, container, timeoutSeconds))
            {
                this.logger.LogTrace($"Waiting for container '{container}' in pod '{podName}' to reach terminated state, current state: {state}");
                if (state.Terminated != null)
                {
                    return state.Terminated;
                }
            }
            throw new TimeoutException($"Container '{container}' in pod '{podName}' did not reach terminated state before timeout reached");
        }

        public async IAsyncEnumerable<string> TailContainerLogsAsync(string podName, string container)
        {
            var response = await this.Client.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                namespaceParameter: this.Namespace,
                name: podName,
                container: container,
                follow: true);

            var reader = new StreamReader(response.Body);

            var line = await reader.ReadLineAsync();
            while (line != null)
            {
                yield return line;
                line = await reader.ReadLineAsync();
            }
        }


        public async Task UploadTarGzStreamToContainerAsync(string podName, string container, string remotePath, Stream tarGzStream, int packetSize, string? progressBarDescription)
        {
            var ws = await this.Client.WebSocketNamespacedPodExecAsync(
                name: podName,
                @namespace: this.Namespace,
                command: "sh",
                container: container,
                stdin: true,
                stdout: true,
                stderr: true,
                tty: false).ConfigureAwait(false);
            var demux = new StreamDemuxer(ws);
            demux.Start();
            var stream = demux.GetStream(ChannelIndex.StdOut, ChannelIndex.StdIn);

            IEnumerable<char[]> commandGenerator()
            {
                var remoteTempFilename = new Guid().ToString();
                yield return $"cat <<EOF > /tmp/{remoteTempFilename}.tar.gz.b64".ToCharArray();
                while (tarGzStream.Position < tarGzStream.Length)
                {
                    yield return tarGzStream.ReadAsync()
                }
                yield return "EOF".ToCharArray();
                yield return $"base64 -d /tmp/{remoteTempFilename}.tar.gz.b64 >> /tmp/{remoteTempFilename}.tar.gz".ToCharArray();
                yield return $"tar xvf /tmp/{remoteTempFilename}.tar.gz -C {remotePath}".ToCharArray();
            }


# TODO: batch packets more efficiently than just per line
            def command_gen():
                yield f"cat <<EOF > /tmp/{remote_temp_filename}.tar.gz.b64"
                while tar_buffer.peek():
                    # TODO: find good default for packet size, make configurable
                    data = tar_buffer.read(packet_size)
                    b64_str = str(base64.b64encode(data), "utf-8")
                    yield b64_str
                yield "EOF"
                yield f"base64 -d /tmp/{remote_temp_filename}.tar.gz.b64 >> /tmp/{remote_temp_filename}.tar.gz"
                yield f"tar xvf /tmp/{remote_temp_filename}.tar.gz -C {remote_path}"
        }

        public async Task UploadLocalFilesToContainerAsync(string podName, string container, IEnumerable<string> localFiles, string remotePath, string relativeLocalRoot, int packetSize, string? progressBarDescription)
        {
            var remoteTempFilename = new Guid().ToString();
            var ws = await this.Client.WebSocketNamespacedPodExecAsync(
                name: podName,
                @namespace: this.Namespace,
                command: "sh",
                container: container,
                stdin: true,
                stdout: true,
                stderr: true,
                tty: false).ConfigureAwait(false);
            var demux = new StreamDemuxer(ws);
            demux.Start();
            var stream = demux.GetStream(ChannelIndex.StdOut, ChannelIndex.StdIn);

            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipOutputStream(memoryStream))
                using (var tarArchive = TarArchive.CreateOutputTarArchive(gzipStream))
                {
                    var fileCount = 0;
                    foreach (var localFile in localFiles)
                    {
                        logger.LogDebug($"Including file in transfer to pod: {localFile}");
                        var tarEntry = TarEntry.CreateEntryFromFile(localFile);
                        tarArchive.WriteEntry(tarEntry, false);
                        fileCount++;
                    }
                    logger.LogInformation($"Including {fileCount} files in transfer to pod");
                }
                memoryStream.Seek(0, SeekOrigin.Begin);
                logger.LogInformation($"Transferring {memoryStream.Length} bytes to pod");


            }
        }
    }
}