using Microsoft.Extensions.Logging;
using k8s;
using k8s.Models;
using ICSharpCode.SharpZipLib.Tar;
using System.Text;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using KanikoRemote.Config;
using KanikoRemote.CLI;
using System.Net;

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

        public NamespacedClient(KubernetesConfiguration config, ILogger<NamespacedClient> logger)
        {
            this.Kubeconfig = config.Kubeconfig;
            this.Context = config.Context;
            this.logger = logger;
            this.runningInCluster = KubernetesClientConfiguration.IsInCluster();

            if (this.runningInCluster && (this.Kubeconfig != null || this.Context != null))
            {
                logger.LogWarning("kubernetes.kubeconfig and kubernetes.context are ignored when running in a kubernetes cluster");
            }

            if (config.Namespace != null)
            {
                // If namespace explicitly set, use that
                this.Namespace = config.Namespace;
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

            KubernetesClientConfiguration clientConfig;
            if (this.runningInCluster)
            {
                clientConfig = KubernetesClientConfiguration.InClusterConfig();
            }
            else
            {
                var configModel = KubernetesClientConfiguration.LoadKubeConfig(this.Kubeconfig);

                clientConfig = KubernetesClientConfiguration.BuildConfigFromConfigObject(configModel, this.Context);
            }
            this.Client = new Kubernetes(clientConfig);
        }

        public Task<V1Pod> CreatePodAsync(V1Pod body)
        {
            try
            {
                return this.Client.CreateNamespacedPodAsync(body: body, namespaceParameter: this.Namespace);
            }
            catch (k8s.Autorest.HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to create pods in namespace {this.Namespace}");
            }
        }
        public Task<V1Pod> ReadPodAsync(string podName)
        {
            try
            {
                return this.Client.ReadNamespacedPodAsync(name: podName, namespaceParameter: this.Namespace);
            }
            catch (k8s.Autorest.HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to read pods in namespace {this.Namespace}");
            }
        }
        public Task<V1Pod> DeletePodAsync(string podName)
        {
            try
            {
                return this.Client.DeleteNamespacedPodAsync(name: podName, namespaceParameter: this.Namespace);
            }
            catch (k8s.Autorest.HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to delete pods in namespace {this.Namespace}");
            }
        }

        private async IAsyncEnumerable<V1ContainerState> WatchContainerStatesAsync(string podName, string container, int? timeoutSeconds)
        {
            IAsyncEnumerable<(WatchEventType, V1Pod)> podUpdates;
            try
            {
                var w = this.Client.CoreV1.ListNamespacedPodWithHttpMessagesAsync(
                    namespaceParameter: this.Namespace,
                    fieldSelector: $"metadata.name={podName}",
                    timeoutSeconds: timeoutSeconds,
                    watch: true);
                podUpdates = w.WatchAsync<V1Pod, V1PodList>();
            }
            catch (k8s.Autorest.HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to watch pods in namespace {this.Namespace}");
            }
            await foreach (var (watchEventType, pod) in podUpdates)
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
                this.logger.LogDebug($"Waiting for container '{container}' in pod '{podName}' to reach running state, current state: {JsonSerializer.Serialize(state, typeof(V1ContainerState), LoggerSerialiserContext.Default)}");
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
                this.logger.LogDebug($"Waiting for container '{container}' in pod '{podName}' to reach terminated state, current state: {JsonSerializer.Serialize(state, typeof(V1ContainerState), LoggerSerialiserContext.Default)}");
                if (state.Terminated != null)
                {
                    return state.Terminated;
                }
            }
            throw new TimeoutException($"Container '{container}' in pod '{podName}' did not reach terminated state before timeout reached");
        }

        public async IAsyncEnumerable<string> GetContainerLogsAsync(
            string podName,
            string container,
            int? sinceSeconds = null,
            bool tail = false)
        {
            k8s.Autorest.HttpOperationResponse<Stream>? response;
            try
            {
                response = await this.Client.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                    namespaceParameter: this.Namespace,
                    name: podName,
                    container: container,
                    sinceSeconds: sinceSeconds,
                    follow: tail);
            }
            catch (k8s.Autorest.HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to get pods/log in namespace {this.Namespace}");
            }
            

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

        private async Task<long> UploadTarToContainerAsync(string podName, string container, string remoteRoot, Action<TarOutputStream> tarFunc, int packetSize, IProgress<(long, long)>? progress)
        {
            this.logger.LogDebug("Beginning pod data transfer");
            var remoteTempFilename = Guid.NewGuid().ToString()[..8];
            var completeToken = $"<<{remoteTempFilename}COMPLETETOKEN>>";
            System.Net.WebSockets.WebSocket ws;
            try
            {
                ws = await this.Client.WebSocketNamespacedPodExecAsync(
                    name: podName,
                    @namespace: this.Namespace,
                    command: "sh",
                    container: container,
                    stdin: true,
                    stdout: true,
                    stderr: true,
                    tty: false).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to create pods/exec in namespace {this.Namespace}");
            }
            var demux = new StreamDemuxer(ws);
            demux.Start();

            var podStream = demux.GetStream(ChannelIndex.StdOut, ChannelIndex.StdIn);

            var readTask = Task.Run(async () =>
            {
                using (var podStdOut = new StreamReader(podStream, leaveOpen: true))
                {
                    var reachedCompleteToken = false;
                    while (!podStdOut.EndOfStream && !reachedCompleteToken)
                    {
                        var logLine = await podStdOut.ReadLineAsync();
                        this.logger.LogDebug($"pod stdout: {logLine}");
                        if (logLine == completeToken)
                        {
                            reachedCompleteToken = true;
                        }
                    }
                }
            });

            long totalBytes;
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
                    totalBytes = memory.Length;
                    logger.LogDebug($"Transferring {totalBytes} bytes to pod");

                    // Write memory to pod
                    using (var sr = new StreamReader(memory, leaveOpen: true))
                    {
                        var writeBuffer = new char[packetSize];
                        while (!sr.EndOfStream)
                        {
                            var amountRead = await sr.ReadBlockAsync(writeBuffer, 0, packetSize);
                            await podWriter.WriteLineAsync(writeBuffer, 0, amountRead);
                            progress?.Report((memory.Position, totalBytes));
                        }
                    }
                }

                await podWriter.WriteLineAsync("EOF");
                await podWriter.WriteLineAsync($"base64 -d /tmp/{remoteTempFilename}.tar.gz.b64 >> /tmp/{remoteTempFilename}.tar.gz");
                await podWriter.WriteLineAsync($"tar xvf /tmp/{remoteTempFilename}.tar.gz -C {remoteRoot}");
                await podWriter.WriteLineAsync($"echo {completeToken}");
            }

            // Awaiting the read task waits for the completion token, which includes untarring time
            await readTask;
            podStream.Close();

            this.logger.LogDebug($"Completed pod data transfer");
            return totalBytes;
        }

        public Task<long> UploadLocalFilesToContainerAsync(string podName, string container, string localRoot, IEnumerable<string> localAbsoluteFilepaths, string remoteRoot, int packetSize, IProgress<(long, long)>? progress = null)
        {
            var entries = new List<TarEntry>();
            foreach (var localFile in localAbsoluteFilepaths)
            {
                this.logger.LogDebug($"Including file in transfer to pod: {localFile}");
                var entry = TarEntry.CreateEntryFromFile(localFile);
                entries.Add(entry);
            }
            this.logger.LogDebug($"Transferring {entries.Count} files to pod");
            return this.UploadTarToContainerAsync(podName, container, remoteRoot, (tarStream) =>
            {
                var tarArchive = TarArchive.CreateOutputTarArchive(tarStream);
                tarArchive.RootPath = localRoot;
                foreach (var te in entries)
                {
                    tarArchive.WriteEntry(te, false);
                }
            }, packetSize, progress);
        }

        public Task<long> UploadStringAsFileToContiainerAsync(string podName, string container, string stringData, string fileName, string remoteRoot, int packetSize, IProgress<(long, long)>? progress = null)
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