import base64
import logging
import os
import tarfile
from contextlib import AbstractContextManager
from math import ceil
from pathlib import Path
from tempfile import TemporaryFile
from typing import Generator, Optional

from anyio import sleep
from kubernetes import client, config
from kubernetes.client.api_client import ApiClient
from kubernetes.client.configuration import logging as k8s_logging
from kubernetes.client.models import V1Pod
from kubernetes.stream import stream
from kubernetes.watch import Watch
from tqdm import tqdm

from kaniko_remote.logging import TRACE, getLogger

logger = getLogger(__name__)


class K8sWrapper(AbstractContextManager):
    def __init__(
        self,
        kubeconfig: str,
        namespace: str,
    ) -> None:
        self.kubeconfig = kubeconfig
        self.namespace = namespace

    def __enter__(self) -> "K8sWrapper":
        config.load_kube_config(config_file=self.kubeconfig)
        k8s_logging.basicConfig(level=k8s_logging.WARN if logging.root.level > TRACE else k8s_logging.DEBUG)
        self._k8s_api = ApiClient()
        self.v1 = client.CoreV1Api(self._k8s_api)
        return self

    def __exit__(self, *exc) -> bool:
        self._k8s_api.close()
        return False

    def create_pod(self, body: V1Pod) -> V1Pod:
        return self.v1.create_namespaced_pod(namespace=self.namespace, body=body)

    def read_pod(self, pod: str) -> V1Pod:
        return self.v1.read_namespaced_pod(namespace=self.namespace, name=pod)

    def delete_pod(self, pod: str) -> V1Pod:
        return self.v1.delete_namespaced_pod(namespace=self.namespace, name=pod)

    async def wait_for_container(self, pod: str, container: str, timeout: int = 30 * 60):
        # Firstly we check that a container of the given name is expected
        response = self.read_pod(pod=pod)
        spec_containers = [
            c for c in (response.spec.containers) + (response.spec.init_containers or []) if c.name == container
        ]
        if len(spec_containers) != 1:
            raise ValueError(f"Pod '{pod}' does not have a specific container '{container}'")

        # Then we poll for state
        count = 0
        while count < timeout:
            count += 1
            states = [
                c.state
                for c in (
                    (response.status.container_statuses or [])
                    + (response.status.ephemeral_container_statuses or [])
                    + (response.status.init_container_statuses or [])
                )
                if c.name == container
            ]
            if len(states) == 1:
                logger.debug(f"Waiting for container '{container}' in pod '{pod}', current state: {states[0]}")
                # Note this will be true if the container is either running or terminated
                if states[0].waiting is None:
                    break
            await sleep(1)
            response = self.read_pod(pod=pod)

    async def tail_container(self, pod: str, container: str) -> Generator[str, None, None]:
        # Watch doesn't seem to use websockets, are we better off using stream here?
        # (stream does use websockets)
        w = Watch()
        for line in w.stream(
            self.v1.read_namespaced_pod_log,
            namespace=self.namespace,
            name=pod,
            container=container,
        ):
            # We yield execution here so that this watch can be interupted (eg by ctrl-c)
            await sleep(0)
            yield line

    async def upload_local_dir_to_container(
        self,
        pod: str,
        container: str,
        local_path: Path,
        remote_path: Path,
        progress_bar_description: Optional[str] = None,
    ) -> None:
        s = stream(
            self.v1.connect_get_namespaced_pod_exec,
            namespace=self.namespace,
            name=pod,
            command=["sh"],
            container=container,
            stdin=True,
            stdout=True,
            stderr=True,
            tty=False,
            _preload_content=False,
        )
        remote_temp_filename = hash(local_path)
        with TemporaryFile() as tar_buffer:
            with tarfile.open(fileobj=tar_buffer, mode="w:gz") as tar:
                tar.add(local_path, arcname="/")

            tar_buffer.seek(0)
            tar_size = os.path.getsize(tar_buffer.name)
            progress = tqdm(
                desc=progress_bar_description,
                total=ceil(tar_size / 100) + 4,
                disable=progress_bar_description is None,
            )

            # TODO: batch packets more efficiently than just per line
            def command_gen():
                yield f"cat <<EOF > /tmp/{remote_temp_filename}.tar.gz.b64"
                while tar_buffer.peek():
                    # TODO: find good default for packet size, make configurable
                    data = tar_buffer.read(int(100))
                    b64_str = str(base64.b64encode(data), "utf-8")
                    yield b64_str
                yield "EOF"
                yield f"base64 -d /tmp/{remote_temp_filename}.tar.gz.b64 >> /tmp/{remote_temp_filename}.tar.gz"
                yield f"tar xvf /tmp/{remote_temp_filename}.tar.gz -C {remote_path}"

            commands = command_gen()

            while s.is_open():
                s.update(timeout=1)
                if s.peek_stdout():
                    logger.debug(f"STDOUT: {s.read_stdout()}")
                if s.peek_stderr():
                    logger.debug(f"STDERR: {s.read_stderr()}")
                next_command = next(commands, None)
                if next_command:
                    logger.trace(f"sending: {next_command}")
                    s.write_stdin(next_command + "\n")
                else:
                    break
                progress.update()
        s.close()
        progress.close()
