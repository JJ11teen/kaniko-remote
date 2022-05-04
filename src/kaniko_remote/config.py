import getpass
import os
import re
from typing import Optional

from deflatabledict import DeflatableDict
from ruamel.yaml import YAML

from kaniko_remote.logging import getLogger

logger = getLogger(__name__)

_default_builder_options = dict(
    name=getpass.getuser(),
    cpu="1",
    memory="1G",
    kaniko_image="gcr.io/kaniko-project/executor:latest",
    setup_image="busybox:stable",
    additional_labels={},
    additional_annotations={},
    kaniko_args=[
        "--use-new-run",
    ],
    pod_start_timeout=5 * 60,
    pod_transfer_packet_size=9e3,
)

_default_auth_options = dict(
    type="pod-only",
    service_account=None,
    env=[],
    volumes=[],
)

_config_env_var = "KANIKO_REMOTE_CONFIG"
_config_name = ".kaniko-remote.yaml"


class Config:
    _snake_caseify_regex = re.compile(r"(?<!^)(?=[A-Z])")

    def __init__(self) -> None:
        self.y = {}
        config_location = os.environ.get(_config_env_var, None)
        if config_location is None:
            relative_config = f"{os.getcwd()}/{_config_name}"
            user_config = os.path.expanduser(f"~/{_config_name}")
            if os.path.exists(relative_config):
                config_location = relative_config
            elif os.path.exists(user_config):
                config_location = user_config
            else:
                logger.warning(
                    'Could not find a kaniko-remote config file, attempting to run with unauthorised registry access. Configure registry authorisation and other builder options with "kaniko-remote config".'
                )
        if config_location is not None:
            with open(config_location, "r") as yaml_file:
                parser = YAML(typ="safe")
                self.y = DeflatableDict(d=parser.load(yaml_file))
            logger.debug(f"Parsed config as: {self.y}")
        self.config_location = config_location

    def _snake_caseify_dict(self, d: dict) -> dict:
        return {self._snake_caseify_regex.sub("_", k).lower(): v for k, v in d.items()}

    def get_kubeconfig(self) -> Optional[str]:
        return self.y.get("kubernetes.kubeconfig", None)

    def get_context(self) -> Optional[str]:
        return self.y.get("kubernetes.context", None)

    def get_namespace(self) -> str:
        return self.y.get("kubernetes.namespace", "default")

    def get_builder_options(self) -> dict:
        return {**_default_builder_options, **(self._snake_caseify_dict(self.y.get("builder", {})))}

    def list_all_authorisers(self) -> list:
        return [a["url"] for a in self.y.get("auth", [])]

    def list_always_mount_authorisers(self) -> list:
        return [a["url"] for a in self.y.get("auth", []) if a.get("mount", "").lower() == "always"]

    def get_authoriser_options(self, url) -> dict:
        matching = [a for a in self.y.get("auth", []) if a["url"] == url]
        if len(matching) == 0:
            return _default_auth_options
        auth = {**_default_auth_options, **self._snake_caseify_dict(matching[0])}
        auth["env"] = [self._snake_caseify_dict(e) for e in auth["env"]]
        auth["volumes"] = [self._snake_caseify_dict(v) for v in auth["volumes"]]
        return auth
