using Microsoft.Extensions.Logging;
using k8s;
using k8s.Models;
using ICSharpCode.SharpZipLib.Tar;
using System.Text;
using System.Security.Cryptography;
using System.IO.Compression;
using ShellProgressBar;
using System.Text.Json;

namespace KanikoRemote.K8s
{
    internal class NamespacedClient
    {
        private readonly Kubernetes Client;
        private readonly ILogger<NamespacedClient> logger;

        public readonly string Namespace;

        private readonly string? Kubeconfig;
        private readonly string? Context;
        private readonly bool runningInCluster;

        public NamespacedClient(KubernetesOptions options, ILogger<NamespacedClient> logger)
        {
            this.Kubeconfig = options.Kubeconfig;
            this.Context = options.Context;
            this.logger = logger;
            this.runningInCluster = KubernetesClientConfiguration.IsInCluster();

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

            KubernetesClientConfiguration config;
            if (this.runningInCluster)
            {
                config = KubernetesClientConfiguration.InClusterConfig();
            }
            else
            {
                var configModel = KubernetesClientConfiguration.LoadKubeConfig(this.Kubeconfig);

                config = KubernetesClientConfiguration.BuildConfigFromConfigObject(configModel, this.Context);
            }
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
                    state = (pod.Status.ContainerStatuses ?? Enumerable.Empty<V1ContainerStatus>())
                        .Concat(pod.Status.InitContainerStatuses ?? Enumerable.Empty<V1ContainerStatus>())
                        .Concat(pod.Status.EphemeralContainerStatuses ?? Enumerable.Empty<V1ContainerStatus>())
                        .SingleOrDefault(s => s?.Name == container, null)
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
                this.logger.LogDebug($"Waiting for container '{container}' in pod '{podName}' to reach running state, current state: {JsonSerializer.Serialize(state)}");
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
                this.logger.LogDebug($"Waiting for container '{container}' in pod '{podName}' to reach terminated state, current state: {JsonSerializer.Serialize(state)}");
                if (state.Terminated != null)
                {
                    return state.Terminated;
                }
            }
            throw new TimeoutException($"Container '{container}' in pod '{podName}' did not reach terminated state before timeout reached");
        }

        public async IAsyncEnumerable<string> TailContainerLogsAsync(string podName, string container, int? sinceSeconds = null)
        {
            var response = await this.Client.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                namespaceParameter: this.Namespace,
                name: podName,
                container: container,
                sinceSeconds: sinceSeconds,
                follow: true);

            using (var reader = new StreamReader(response.Body))
            {
                var line = await reader.ReadLineAsync();
                while (line != null)
                {
                    yield return line;
                    line = await reader.ReadLineAsync();
                }
            }
        }

        private async Task UploadTarToContainerAsync(string podName, string container, string remoteRoot, Action<TarOutputStream> tarFunc, int packetSize, IProgressBar? progress)
        {
            this.logger.LogDebug("Beginning pod data transfer");
            var remoteTempFilename = Guid.NewGuid().ToString()[..8];
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

            var podStream = demux.GetStream(ChannelIndex.StdOut, ChannelIndex.StdIn);

            var readTask = Task.Run(async () =>
            {
                using (var reader = new StreamReader(podStream, leaveOpen: true))
                {
                    while (!reader.EndOfStream)
                    {
                        var response = await reader.ReadLineAsync();
                        this.logger.LogDebug($"pod stdout: {response}");
                    }
                }
            });

            using (var podWriter = new StreamWriter(podStream, Encoding.ASCII, leaveOpen: true))
            {
                await podWriter.WriteLineAsync($"cat <<EOF > /tmp/{remoteTempFilename}.tar.gz.b64");

                using (var memory = new MemoryStream())
                {
                    // Write files to memory stream, encoding as files -> tar -> gzip -> b64
                    using (var base64Writer = new CryptoStream(memory, new ToBase64Transform(), CryptoStreamMode.Write, leaveOpen: true))
                    using (var gzipWriter = new GZipStream(base64Writer, CompressionMode.Compress))
                    using (var tarWriter = new TarOutputStream(gzipWriter, Encoding.UTF8))
                    {
                        tarFunc(tarWriter);
                        await tarWriter.FlushAsync();
                    }

                    memory.Position = 0;
                    logger.LogDebug($"Transferring {memory.Length} bytes to pod");
                    if (progress != null)
                    {
                        progress.MaxTicks = (int)memory.Length;
                    }

                    // Write memory to pod
                    using (var sr = new StreamReader(memory, leaveOpen: true))
                    {
                        var writeBuffer = new char[packetSize];
                        while (!sr.EndOfStream)
                        {
                            var amountRead = await sr.ReadBlockAsync(writeBuffer, 0, packetSize);
                            await podWriter.WriteLineAsync(writeBuffer, 0, amountRead);
                            progress?.Tick((int)memory.Position);
                        }
                    }
                }

                await podWriter.WriteLineAsync("EOF");
                await podWriter.WriteLineAsync($"base64 -d /tmp/{remoteTempFilename}.tar.gz.b64 >> /tmp/{remoteTempFilename}.tar.gz");
                await podWriter.WriteLineAsync($"tar xvf /tmp/{remoteTempFilename}.tar.gz -C {remoteRoot}");
            }

            podStream.Close();

            // TODO: look into cancelling/awaiting/cleaning up read task
            // Probably need to wait until this in .NET 7: https://github.com/dotnet/runtime/issues/20824
            // await readTask;
            // Until then, a slight delay helps keep things somewhat synchronised as we just
            // leave the reader task running
            await Task.Delay(200);

            this.logger.LogDebug($"Completed pod data transfer");
        }

        public Task UploadLocalFilesToContainerAsync(string podName, string container, IEnumerable<string> localFiles, string remoteRoot, int packetSize, IProgressBar? progress = null)
        {
            var entries = new List<TarEntry>();
            foreach (var localFile in localFiles)
            {
                this.logger.LogDebug($"Including file in transfer to pod: {localFile}");
                var entry = TarEntry.CreateEntryFromFile(localFile);
                entries.Add(entry);
            }
            this.logger.LogInformation($"Transferring {entries.Count} files to pod");
            return this.UploadTarToContainerAsync(podName, container, remoteRoot, (tarStream) =>
            {
                // await Task.Delay(0);
                var tarArchive = TarArchive.CreateOutputTarArchive(tarStream);
                foreach (var te in entries)
                {
                    tarArchive.WriteEntry(te, false);
                }
            }, packetSize, progress);
        }

        public Task UploadStringAsFileToContiainerAsync(string podName, string container, string stringData, string fileName, string remoteRoot, int packetSize, IProgressBar? progress = null)
        {
            return this.UploadTarToContainerAsync(podName, container, remoteRoot, (tarStream) =>
            {
                // Form tar header
                var tarHeader = new TarHeader();
                TarEntry.NameTarHeader(tarHeader, fileName);
                var tarEntry = new TarEntry(tarHeader);
                tarEntry.Size = Encoding.UTF8.GetByteCount(stringData);
                // Write header & data to stream
                tarStream.PutNextEntry(tarEntry);
                tarStream.Write(Encoding.UTF8.GetBytes(stringData));
                tarStream.CloseEntry();
            }, packetSize, progress);
        }
    }
}