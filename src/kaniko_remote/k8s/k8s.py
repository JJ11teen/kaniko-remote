import base64
import logging
import os
import tarfile
from contextlib import AbstractContextManager
from math import ceil
from pathlib import Path
from tempfile import TemporaryFile
from typing import Generator, Optional, Tuple

from anyio import sleep
from kubernetes import client, config
from kubernetes.client.api_client import ApiClient
from kubernetes.client.configuration import logging as k8s_logging
from kubernetes.client.models import (
    V1ContainerStateRunning,
    V1ContainerStateTerminated,
    V1Pod,
)
from kubernetes.stream import stream
from kubernetes.watch import Watch
from tqdm import tqdm

from kaniko_remote.logging import TRACE, getLogger

logger = getLogger(__name__)


class K8sWrapper(AbstractContextManager):
    def __init__(
        self,
        kubeconfig: str,
        context: str,
        namespace: str,
    ) -> None:
        self.kubeconfig = kubeconfig
        self.context = context
        self.namespace = namespace

    def __enter__(self) -> "K8sWrapper":
        config.load_kube_config(config_file=self.kubeconfig, context=self.context)
        k8s_logging.basicConfig(level=k8s_logging.WARN if logging.root.level > TRACE else k8s_logging.DEBUG)
        self._k8s_api = ApiClient()
        self.v1 = client.CoreV1Api(self._k8s_api)
        return self

    def __exit__(self, *exc) -> bool:
        self._k8s_api.close()
        return False

    def create_pod(self, body: V1Pod) -> V1Pod:
        return self.v1.create_namespaced_pod(namespace=self.namespace, body=body)

    def read_pod(self, pod_name: str) -> V1Pod:
        return self.v1.read_namespaced_pod(namespace=self.namespace, name=pod_name)

    def delete_pod(self, pod_name: str) -> V1Pod:
        return self.v1.delete_namespaced_pod(namespace=self.namespace, name=pod_name)

    def _watch_container_states(self, pod_name: str, container: str, timeout_seconds: int):
        w = Watch()
        for event in w.stream(
            self.v1.list_namespaced_pod,
            namespace=self.namespace,
            field_selector=f"metadata.name={pod_name}",
            timeout_seconds=timeout_seconds,
        ):
            pod_status = event["object"].status
            states = [
                c.state
                for c in (
                    (pod_status.container_statuses or [])
                    + (pod_status.ephemeral_container_statuses or [])
                    + (pod_status.init_container_statuses or [])
                )
                if c.name == container
            ]
            if len(states) == 1:
                yield states[0]
            elif len(states) > 1:
                raise ValueError(f"Pod '{pod_name}' has more than one container '{container}'")

    async def wait_for_container_running_state(
        self, pod_name: str, container: str, timeout_seconds: int
    ) -> V1ContainerStateRunning:
        for state in self._watch_container_states(pod_name, container, timeout_seconds):
            logger.trace(
                f"Waiting for container '{container}' in pod '{pod_name}' to reach running state, current state: {state}"
            )
            if state.running is not None:
                return state.running

    async def wait_for_container_terminated_state(
        self, pod_name: str, container: str, timeout_seconds: int
    ) -> V1ContainerStateTerminated:
        for state in self._watch_container_states(pod_name, container, timeout_seconds):
            logger.trace(
                f"Waiting for container '{container}' in pod '{pod_name}' to reach terminated state, current state: {state}"
            )
            if state.terminated is not None:
                return state.terminated

    async def tail_container(self, pod_name: str, container: str) -> Generator[str, None, None]:
        # Watch doesn't seem to use websockets, are we better off using stream here?
        # (stream does use websockets)
        w = Watch()
        for line in w.stream(
            self.v1.read_namespaced_pod_log,
            namespace=self.namespace,
            name=pod_name,
            container=container,
        ):
            # We yield execution here so that this watch can be interupted (eg by ctrl-c)
            await sleep(0)
            yield line

    async def upload_local_dir_to_container(
        self,
        pod_name: str,
        container: str,
        local_path: Path,
        remote_path: Path,
        progress_bar_description: Optional[str] = None,
    ) -> float:
        s = stream(
            self.v1.connect_get_namespaced_pod_exec,
            namespace=self.namespace,
            name=pod_name,
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
                ascii=True,
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
