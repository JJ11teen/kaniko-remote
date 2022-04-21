import base64
import json
from contextlib import AbstractContextManager
from signal import SIGINT
from tempfile import TemporaryDirectory
from typing import Callable, List, Optional
from urllib.parse import urlparse

from kaniko_remote.authorisers import KanikoAuthoriser, get_matching_authorisers
from kaniko_remote.config.config import Config
from kaniko_remote.k8s.k8s import K8sWrapper
from kaniko_remote.k8s.specs import K8sSpecs
from kaniko_remote.logging import getLogger

logger = getLogger(__name__)


class Builder(AbstractContextManager):
    def __init__(
        self,
        k8s_wrapper: K8sWrapper,
        config: Config,
        **kaniko_kwargs,
    ) -> None:
        self.k8s = k8s_wrapper

        self.pod_name = None
        self.stopped = False

        local_context = self._parse_local_context(kaniko_kwargs["context"])
        pod_spec = K8sSpecs.generate_pod_spec(**config.get_builder_options())
        urls_to_auth = [kaniko_kwargs["destination"]]

        if local_context:
            pod_spec = K8sSpecs.mount_context_for_exec_transfer(pod_spec)
        else:
            urls_to_auth.append(kaniko_kwargs["context"])

        authorisers: List[KanikoAuthoriser] = get_matching_authorisers(urls=urls_to_auth, config=config)
        docker_config = {}
        pod_spec = K8sSpecs.add_kaniko_args(pod_spec, **kaniko_kwargs)

        for auth in authorisers:
            docker_config = auth.append_auth_to_docker_config(docker_config=docker_config)
            pod_spec = auth.append_auth_to_pod(pod_spec=pod_spec)

        logger.info(f"Configuring builder with auth for {[a.url for a in authorisers]}")
        logger.debug(f"Generated docker config for builder: {docker_config}")
        logger.debug(f"Generated pod spec for builder: {pod_spec}")

        self._local_context = local_context
        self._docker_config = docker_config
        self._pod_spec = pod_spec

    @classmethod
    def _parse_local_context(cls, context: str) -> Optional[str]:
        _urlparse = urlparse(context)
        if not _urlparse.scheme:
            logger.info("Local context detected, the context will be transferred directly to the builder pod.")
            return context
        else:
            logger.info("Remote context detected, builder pod will be authorised to access configured remote storage.")
            return None

    def __enter__(self) -> "Builder":
        # self._loop = asyncio.get_event_loop()
        self._create()
        # self._loop.add_signal_handler(SIGINT, self.__exit__)
        return self

    def __exit__(self, *exc) -> bool:
        self._destroy()
        # self._loop.remove_signal_handler(SIGINT)
        return False

    def _create(self) -> None:
        logger.info(f"Creating builder pod in '{self.k8s.namespace}' namespace.")
        pod_spec = self.k8s.create_pod(body=self._pod_spec)
        self.pod_name = pod_spec.metadata.name
        logger.info(f"Created builder pod '{self.k8s.namespace}/{self.pod_name}'.")
        logger.debug(f"Created builder pod with spec: {pod_spec}")

    def _destroy(self):
        if self.pod_name:
            logger.warn(f"Deleting pod {self.pod_name}")
            self.k8s.delete_pod(self.pod_name)
            logger.warn(f"Deleted pod {self.pod_name}")
        self.stopped = True

    async def setup(self) -> None:
        await self.k8s.wait_for_container(pod=self.pod_name, container="setup")

        if self._local_context:
            await self.k8s.upload_local_dir_to_container(
                pod=self.pod_name,
                container="setup",
                local_path=self._local_context,
                remote_path="/workspace",
                progress_bar_description="Sending context dir: ",
            )
        else:
            logger.info("Using remote storage for context dir, skipping upload")

        with TemporaryDirectory() as config_dir:
            with open(config_dir + "/config.json", "w") as f:
                json.dump(self._docker_config, f)

            await self.k8s.upload_local_dir_to_container(
                pod=self.pod_name,
                container="setup",
                local_path=config_dir,
                remote_path="/kaniko/.docker",
            )

        logger.info(f"Setup builder pod {self.pod_name}.")

    async def build(self, log_callback: Callable[[str], None]) -> None:
        logger.info("Connecting to logs...")
        await self.k8s.wait_for_container(pod=self.pod_name, container="builder", timeout=10)
        async for line in self.k8s.tail_container(pod=self.pod_name, container="builder"):
            log_callback(line)
            if self.stopped:
                return
