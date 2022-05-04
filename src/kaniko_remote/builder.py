import json
import os
from contextlib import AbstractContextManager
from glob import iglob
from signal import SIGINT
from tempfile import TemporaryDirectory
from typing import Callable, List, Optional
from urllib.parse import urlparse

import pathspec

from kaniko_remote.authorisers import KanikoAuthoriser, get_matching_authorisers
from kaniko_remote.config import Config
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
        self.stopped = False

        builder_options = config.get_builder_options()
        self._pod_start_timeout = int(builder_options.pop("pod_start_timeout"))
        self._pod_transfer_packet_size = int(builder_options.pop("pod_transfer_packet_size"))
        pod_spec = K8sSpecs.generate_pod_spec(**builder_options)

        local_context = self._parse_local_context(kaniko_kwargs["context"])
        urls_to_auth = list(kaniko_kwargs["destinations"])
        if local_context:
            kaniko_kwargs.pop("context")
            pod_spec = K8sSpecs.mount_context_for_exec_transfer(pod_spec)
            dockerfile = kaniko_kwargs.get("dockerfile", None)
            if dockerfile and not os.path.isfile(f"{local_context}/{dockerfile}"):
                raise ValueError(f"Could not find dockerfile {dockerfile} within local context {local_context}")
        else:
            urls_to_auth.append(kaniko_kwargs["context"])

        authorisers: List[KanikoAuthoriser] = get_matching_authorisers(urls=urls_to_auth, config=config)
        pod_spec = K8sSpecs.set_kaniko_args(
            pod=pod_spec,
            preparsed_args=config.get_builder_options().pop("kaniko_args", []),
            **kaniko_kwargs,
        )

        docker_config = {}
        for auth in authorisers:
            docker_config = auth.append_auth_to_docker_config(docker_config=docker_config)
            pod_spec = auth.append_auth_to_pod(pod_spec=pod_spec)

        logger.info(f"Configured builder with auth profiles: {''.join([a.url for a in authorisers])}.")
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
        pod_spec = self.k8s.create_pod(body=self._pod_spec)
        self.pod_name = pod_spec.metadata.name
        logger.debug(f"Initialised builder pod with spec: {pod_spec}")

    def _destroy(self):
        if self.pod_name:
            logger.info(f"Deleting pod {self.pod_name}")
            self.k8s.delete_pod(self.pod_name)
            logger.info(f"Deleted pod {self.pod_name}")
        self.stopped = True

    async def setup(self) -> str:
        await self.k8s.wait_for_container_running_state(
            pod_name=self.pod_name, container="setup", timeout_seconds=self._pod_start_timeout
        )

        if self._local_context:
            await self._transfer_local_build_context()
        else:
            logger.debug("Using remote storage for context dir, skipping upload")

        with TemporaryDirectory() as config_dir:
            with open(config_dir + "/config.json", "w") as f:
                json.dump(self._docker_config, f)

            await self.k8s.upload_local_dir_to_container(
                pod_name=self.pod_name,
                container="setup",
                local_files=[f"{config_dir}/config.json"],
                remote_path="/kaniko/.docker",
                relative_local_root=config_dir,
                packet_size=self._pod_transfer_packet_size,
            )

        return self.pod_name

    async def _transfer_local_build_context(self):

        build_context_paths = iglob(f"{self._local_context}/**", recursive=True)

        # Filter with dockerignore if exists
        try:
            with open(os.path.join(self._local_context, ".dockerignore"), "r") as ignore_file:
                spec = pathspec.PathSpec.from_lines("gitwildmatch", ignore_file)
            build_context_paths = (p for p in build_context_paths if not spec.match_file(p))
        except FileNotFoundError:
            pass

        await self.k8s.upload_local_dir_to_container(
            pod_name=self.pod_name,
            container="setup",
            local_files=build_context_paths,
            remote_path="/workspace",
            relative_local_root=self._local_context,
            progress_bar_description="[KANIKO-REMOTE] Sending context",
            packet_size=self._pod_transfer_packet_size,
        )

    async def build(self, log_callback: Callable[[str], None]) -> str:
        await self.k8s.wait_for_container_running_state(pod_name=self.pod_name, container="builder", timeout_seconds=10)
        async for line in self.k8s.tail_container(pod_name=self.pod_name, container="builder"):
            log_callback(line)
            if self.stopped:
                return

        terminated = await self.k8s.wait_for_container_terminated_state(
            pod_name=self.pod_name, container="builder", timeout_seconds=10
        )

        if terminated.exit_code == 0:
            # This will be the newly build image sha at this point
            return terminated.message
        else:
            raise ValueError(f"Kaniko failed to build and/or push image: {terminated}")
