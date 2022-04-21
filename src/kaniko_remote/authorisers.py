import base64
from abc import ABC
from typing import List
from urllib.parse import urlparse

from kubernetes.client.models import V1Pod

from kaniko_remote.config.config import Config
from kaniko_remote.k8s.specs import K8sSpecs
from kaniko_remote.logging import getLogger

logger = getLogger(__name__)


class KanikoAuthoriser(ABC):
    def append_auth_to_pod(self, pod_spec: V1Pod) -> V1Pod:
        pass

    def append_auth_to_docker_config(self, docker_config: dict) -> dict:
        pass


def get_matching_authorisers(urls: List[str], config: Config) -> List[KanikoAuthoriser]:
    """
    We take lists in & out so we can do some basic validation (ie only one service account specified)
    """
    authoriser_names = config.list_always_mount_authorisers() + [
        an for an in config.list_all_authorisers() if any([u.startswith(an) for u in urls])
    ]
    authoriser_configs = [config.get_authoriser_options(an) for an in authoriser_names]

    service_accounts = [c["service_account"] for c in authoriser_configs if c["service_account"] is not None]
    if len(set(service_accounts)) > 1:
        logger.warning(
            f"Found multiple matching authorisers with service accounts specified. Using '{service_accounts[-1]}'."
        )

    authorisers = []
    for auth_config in authoriser_configs:
        auth_type = auth_config.pop("type")

        if auth_type == "pod-only":
            authorisers.append(PodOnlyAuth(**auth_config))
        elif auth_type == "acr":
            authorisers.append(ACR(**auth_config))
        else:
            raise ValueError(f"Unknown auth registry type: {auth_type}")

    return authorisers


class PodOnlyAuth(KanikoAuthoriser):
    def __init__(self, **kwargs) -> None:
        self.url = kwargs.pop("url")
        self._service_account = kwargs.pop("service_account")
        self._secret_as_env_vars = kwargs.pop("secret_as_env_vars")
        self._secret_as_file = kwargs.pop("secret_as_file")
        kwargs.pop("always_mount")

        if len(kwargs) > 0:
            raise ValueError(f"Invalid auth config for '{self.url}' specified: {kwargs}")

    def append_auth_to_pod(self, pod_spec: V1Pod) -> V1Pod:
        if self._service_account:
            pod_spec = K8sSpecs.replace_service_account(pod=pod_spec, service_account_name=self._service_account)
        if self._secret_as_env_vars:
            pod_spec = K8sSpecs.append_env_from_secret(pod=pod_spec, secret_name=self._secret_as_env_vars)
        if self._secret_as_file:
            pod_spec = K8sSpecs.append_file_mount_from_secret(pod=pod_spec, secret_name=self._secret_as_file)
        return pod_spec

    def append_auth_to_docker_config(self, docker_config: dict) -> dict:
        return docker_config


class ACR(PodOnlyAuth):
    def __init__(self, **kwargs) -> None:
        self._token = kwargs.pop("token", None)
        super().__init__(**kwargs)

    def generate_docker_config(self, docker_config: dict) -> dict:
        hostname = urlparse(f"https://{self.url}").hostname
        if self._token:
            logger.warning("Writing ACR auth token directly into docker config.")
            if "auths" not in docker_config:
                docker_config["auths"] = {}
            docker_config["auths"][hostname] = {
                "auth": base64.b64encode(f"00000000-0000-0000-0000-000000000000:{self._token}".encode("utf-8")).decode(
                    "utf-8"
                )
            }

        if "credHelpers" not in docker_config:
            docker_config["credHelpers"] = {}
        docker_config["credHelpers"][hostname] = "acr-env"

        return docker_config


# TODO: other registry providers
