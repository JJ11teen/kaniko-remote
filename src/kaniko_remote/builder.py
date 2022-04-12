import base64
import json
from contextlib import AbstractContextManager
from pathlib import Path
from signal import SIGINT
from tempfile import TemporaryDirectory
from typing import Callable
from urllib.parse import urlparse

from kaniko_remote.k8s.k8s import K8sWrapper
from kaniko_remote.k8s.specs import K8sSpecs
from kaniko_remote.logging import getLogger

logger = getLogger(__name__)


class Builder(AbstractContextManager):
    def __init__(self, k8s_wrapper: K8sWrapper, instance_id: str, context: Path, destination: str, **kwargs) -> None:
        self.k8s = k8s_wrapper
        self.instance_id = instance_id
        self.context = context
        self.destination = destination

        self.pod_name = None
        self.stopped = False

        self.acr_token = kwargs.pop("acr_token", None)

        pod_spec = K8sSpecs.generate_pod_spec(
            instance_id=self.instance_id,
            use_debug_image=kwargs.pop("use_debug_image", False),
            service_account_name=kwargs.pop("service_account_name", None),
        )

        if "env_from_secret" in kwargs:
            pod_spec = K8sSpecs.add_env_from_secret(pod_spec, kwargs.pop("env_from_secret"))

        kwargs.update(context=context, destination=destination)
        pod_spec = K8sSpecs.add_kaniko_args(pod_spec, **kwargs)

        self._pod_spec = pod_spec
        logger.debug("Generated pod spec for builder: ", self._pod_spec)

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
        logger.info(f"Created builder pod {self.pod_name}.")
        logger.debug("Created builder pod with spec: ", pod_spec)

    def _destroy(self):
        if self.pod_name:
            logger.warn(f"Deleting pod {self.pod_name}")
            self.k8s.delete_pod(self.pod_name)
            logger.warn(f"Deleted pod {self.pod_name}")
        self.stopped = True

    async def setup(self) -> None:
        await self.k8s.wait_for_container(pod=self.pod_name, container="setup")

        await self.k8s.upload_local_dir_to_container(
            pod=self.pod_name,
            container="setup",
            local_path=self.context,
            remote_path="/workspace",
            progress_bar_description="Sending context dir: ",
        )

        registry_url = urlparse(f"http://{self.destination}")
        docker_config = {"credHelpers": {registry_url.hostname: "acr-env"}}
        if self.acr_token:
            docker_config = {
                "auths": {
                    registry_url.hostname: {
                        "auth": base64.b64encode(
                            f"00000000-0000-0000-0000-000000000000:{self.acr_token}".encode("utf-8")
                        ).decode("utf-8")
                    }
                }
            }

        logger.debug(f"Creating docker config.json: {docker_config}")
        with TemporaryDirectory() as config_dir:
            with open(config_dir + "/config.json", "w") as f:
                json.dump(docker_config, f)

            await self.k8s.upload_local_dir_to_container(
                pod=self.pod_name,
                container="setup",
                local_path=config_dir,
                remote_path="/kaniko/.docker",
            )

        logger.info(f"Setup builder pod {self.pod_name}.")

    async def build(self, log_callback: Callable[[str], None]) -> None:
        await self.k8s.wait_for_container(pod=self.pod_name, container="builder", timeout=10)
        async for line in self.k8s.tail_container(pod=self.pod_name, container="builder"):
            log_callback(line)
            if self.stopped:
                return
