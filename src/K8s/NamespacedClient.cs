using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Formats.Tar;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using k8s;
using k8s.Models;
using k8s.Autorest;
using k8s.Exceptions;
using ICSharpCode.SharpZipLib.Tar;
using KanikoRemote.Config;
using KanikoRemote.CLI;

namespace KanikoRemote.K8s
{
    internal class NamespacedClient
    {
        private readonly Kubernetes Client;

        public readonly string Namespace;

        private readonly string? Kubeconfig;
        private readonly string? Context;
        private readonly bool runningInCluster;

        public NamespacedClient(KubernetesConfiguration config)
        {
            this.Kubeconfig = config.Kubeconfig;
            this.Context = config.Context;
            this.runningInCluster = KubernetesClientConfiguration.IsInCluster();

            if (this.runningInCluster && (this.Kubeconfig != null || this.Context != null))
            {
                SimpleLogger.WriteWarn("kubernetes.kubeconfig and kubernetes.context are ignored when running in a kubernetes cluster");
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
                try
                {
                    var configModel = KubernetesClientConfiguration.LoadKubeConfig(this.Kubeconfig);

                    clientConfig = KubernetesClientConfiguration.BuildConfigFromConfigObject(configModel, this.Context);
                }
                catch (KubeConfigException)
                {
                    throw new KubernetesConfigException();
                }
            }
            this.Client = new Kubernetes(clientConfig);
        }

        public async Task<V1Pod> CreatePodAsync(V1Pod body, CancellationToken ct = default)
        {
            try
            {
                return await this.Client.CreateNamespacedPodAsync(
                    body: body,
                    namespaceParameter: this.Namespace,
                    cancellationToken: ct);
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to create pods in namespace {this.Namespace}");
            }
        }
        public async Task<V1Pod> ReadPodAsync(string podName, CancellationToken ct = default)
        {
            try
            {
                return await this.Client.ReadNamespacedPodAsync(
                    name: podName,
                    namespaceParameter: this.Namespace,
                    cancellationToken: ct);
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to read pods in namespace {this.Namespace}");
            }
        }
        public async Task<V1Pod> DeletePodAsync(string podName, CancellationToken ct = default)
        {
            try
            {
                return await this.Client.DeleteNamespacedPodAsync(
                    name: podName,
                    namespaceParameter: this.Namespace,
                    cancellationToken: ct);
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to delete pods in namespace {this.Namespace}");
            }
        }

        private async IAsyncEnumerable<V1ContainerState> WatchContainerStatesAsync(string podName, string container, int? timeoutSeconds, [EnumeratorCancellation] CancellationToken ct)
        {
            IAsyncEnumerable<(WatchEventType, V1Pod)> podUpdates;
            try
            {
                var w = this.Client.CoreV1.ListNamespacedPodWithHttpMessagesAsync(
                    namespaceParameter: this.Namespace,
                    fieldSelector: $"metadata.name={podName}",
                    timeoutSeconds: timeoutSeconds,
                    watch: true,
                    cancellationToken: ct);
                podUpdates = w.WatchAsync<V1Pod, V1PodList>();
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to watch pods in namespace {this.Namespace}");
            }
            await foreach (var (watchEventType, pod) in podUpdates.WithCancellation(ct))
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

        public async Task<V1ContainerStateRunning> AwaitContainerRunningStateAsync(string podName, string container, int? timeoutSeconds = null, CancellationToken ct = default)
        {
            await foreach (var state in this.WatchContainerStatesAsync(podName, container, timeoutSeconds, ct))
            {
                SimpleLogger.WriteDebug($"Waiting for container '{container}' in pod '{podName}' to reach running state");
                SimpleLogger.WriteDebugJson<V1ContainerState>("Current state:", state);
                if (state.Running != null)
                {
                    return state.Running;
                }
            }
            throw new TimeoutException($"Container '{container}' in pod '{podName}' did not reach running state before timeout reached");
        }


        public async Task<V1ContainerStateTerminated> AwaitContainerTerminatedStateAsync(string podName, string container, int? timeoutSeconds = null, CancellationToken ct = default)
        {
            await foreach (var state in this.WatchContainerStatesAsync(podName, container, timeoutSeconds, ct))
            {
                SimpleLogger.WriteDebug($"Waiting for container '{container}' in pod '{podName}' to reach terminated state");
                SimpleLogger.WriteDebugJson<V1ContainerState>("Current state:", state);
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
            bool tail = false,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            HttpOperationResponse<Stream>? response;
            try
            {
                response = await this.Client.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                    namespaceParameter: this.Namespace,
                    name: podName,
                    container: container,
                    sinceSeconds: sinceSeconds,
                    follow: tail,
                    cancellationToken: ct);
                ct.ThrowIfCancellationRequested();
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new KubernetesPermissionException($"Unauthorized to get pods/log in namespace {this.Namespace}");
            }
            
            using (var reader = new StreamReader(response.Body))
            {
                var line = await reader.ReadLineAsync().WaitAsync(ct);
                while (line != null && !ct.IsCancellationRequested)
                {
                    yield return line;
                    line = await reader.ReadLineAsync().WaitAsync(ct);
                }
            }
            ct.ThrowIfCancellationRequested();
        }

        private async Task<long> UploadTarToContainerAsync(
            string podName,
            string container,
            string remoteRoot,
            Stream tarStream,
            // Action<TarOutputStream> tarFunc,
            int packetSize,
            IProgress<(long, long)>? progress,
            CancellationToken ct)
        {
            SimpleLogger.WriteDebug("Beginning pod data transfer");
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
                    tty: false,
                    cancellationToken: ct);
                ct.ThrowIfCancellationRequested();
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.Forbidden)
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
                    while (!podStdOut.EndOfStream && !reachedCompleteToken && !ct.IsCancellationRequested)
                    {
                        var logLine = await podStdOut.ReadLineAsync();
                        SimpleLogger.WriteDebug($"pod stdout: {logLine}");
                        if (logLine == completeToken)
                        {
                            reachedCompleteToken = true;
                        }
                    }
                }
            }, ct);

            long totalBytes;
            using (var podWriter = new StreamWriter(podStream, Encoding.ASCII, leaveOpen: true))
            {
                await podWriter.WriteLineAsync($"cat <<EOF > /tmp/{remoteTempFilename}.tar.gz.b64").WaitAsync(ct);
                ct.ThrowIfCancellationRequested();

                using (var memory = new MemoryStream())
                {
                    // Copy tarStream to memory stream, encoding as with gzip then b64
                    using (var base64Writer = new CryptoStream(memory, new ToBase64Transform(), CryptoStreamMode.Write, leaveOpen: true))
                    using (var gzipWriter = new GZipStream(base64Writer, CompressionMode.Compress, leaveOpen: true))
                    // using (var tarWriter = new TarWriter(gzipWriter, leaveOpen: true))
                    // using (var tarWriter = new TarOutputStream(gzipWriter, Encoding.UTF8))
                    {
                        await tarStream.CopyToAsync(gzipWriter);
                    }
                    ct.ThrowIfCancellationRequested();

                    memory.Seek(0, SeekOrigin.Begin);
                    totalBytes = memory.Length;
                    SimpleLogger.WriteDebug($"Transferring {totalBytes} bytes to pod");

                    // Write memory to pod
                    using (var sr = new StreamReader(memory, leaveOpen: true))
                    {
                        var writeBuffer = new char[packetSize];
                        while (!sr.EndOfStream && !ct.IsCancellationRequested)
                        {
                            var amountRead = await sr.ReadBlockAsync(writeBuffer, 0, packetSize).WaitAsync(ct);
                            await podWriter.WriteLineAsync(writeBuffer, 0, amountRead).WaitAsync(ct);
                            progress?.Report((memory.Position, totalBytes));
                        }
                    }
                }

                await podWriter.WriteLineAsync("EOF").WaitAsync(ct);
                await podWriter.WriteLineAsync($"base64 -d /tmp/{remoteTempFilename}.tar.gz.b64 >> /tmp/{remoteTempFilename}.tar.gz").WaitAsync(ct);
                await podWriter.WriteLineAsync($"tar xvf /tmp/{remoteTempFilename}.tar.gz -C {remoteRoot}").WaitAsync(ct);
                await podWriter.WriteLineAsync($"echo {completeToken}").WaitAsync(ct);
                ct.ThrowIfCancellationRequested();
            }

            // Awaiting the read task waits for the completion token, which includes untarring time
            await readTask.WaitAsync(ct);
            podStream.Close();
            ct.ThrowIfCancellationRequested();

            SimpleLogger.WriteDebug($"Completed pod data transfer");
            return totalBytes;
        }

        public async Task<long> UploadLocalFilesToContainerAsync(
            string podName,
            string container,
            string localRoot,
            IEnumerable<string> localAbsoluteFilepaths,
            string remoteRoot,
            int packetSize,
            IProgress<(long, long)>? progress = null,
            CancellationToken ct = default)
        {
            using MemoryStream tarStream = new();
            using (TarWriter writer = new(tarStream, leaveOpen: true))
            {
                foreach (var localFile in localAbsoluteFilepaths)
                {
                    var relPath = Path.GetRelativePath(localRoot, localFile);
                    SimpleLogger.WriteDebug($"Including file in transfer to pod: {relPath}");
                    await writer.WriteEntryAsync(fileName: localFile, entryName: relPath);
                }
            }
            tarStream.Seek(0, SeekOrigin.Begin);

            return await this.UploadTarToContainerAsync(podName, container, remoteRoot, tarStream, packetSize, progress, ct);
        }

        public async Task<long> UploadStringAsFileToContiainerAsync(
            string podName,
            string container,
            string stringData,
            string fileName,
            string remoteRoot,
            int packetSize,
            IProgress<(long, long)>? progress = null,
            CancellationToken ct = default)
        {
            using MemoryStream tarStream = new();
            using (TarWriter tarWriter = new(tarStream, leaveOpen: true))
            {
                PaxTarEntry entry = new(TarEntryType.RegularFile, fileName);
                using MemoryStream rawStream = new();
                using (StreamWriter streamWriter = new(rawStream, Encoding.UTF8, leaveOpen: true))
                {
                    await streamWriter.WriteAsync(stringData);
                }
                rawStream.Seek(0, SeekOrigin.Begin);

                entry.DataStream = rawStream;
                await tarWriter.WriteEntryAsync(entry);
            }
            tarStream.Seek(0, SeekOrigin.Begin);

            return await this.UploadTarToContainerAsync(podName, container, remoteRoot, tarStream, packetSize, progress, ct);
            // return await this.UploadTarToContainerAsync(podName, container, remoteRoot, (tarStream) =>
            // {
            //     // Form tar header
            //     var tarHeader = new TarHeader();
            //     TarEntry.NameTarHeader(tarHeader, fileName);
            //     var tarEntry = new TarEntry(tarHeader);
            //     tarEntry.Size = Encoding.UTF8.GetByteCount(stringData);
            //     // Write header & data to stream
            //     tarStream.PutNextEntry(tarEntry);
            //     tarStream.Write(Encoding.UTF8.GetBytes(stringData));
            //     tarStream.CloseEntry();
            // }, packetSize, progress, ct);
        }
    }
}