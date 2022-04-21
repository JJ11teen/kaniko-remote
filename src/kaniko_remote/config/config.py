import getpass
import os
import re
from typing import Optional

from deflatabledict import DeflatableDict
from ruamel.yaml import YAML

from kaniko_remote.logging import getLogger

logger = getLogger(__name__)

_default_builder_options = dict(
    instance_id=getpass.getuser(),
    use_debug_image=False,
    cpu="1",
    memory="1G",
    additional_labels={},
    additional_annotations={},
)

_default_auth_options = dict(
    type="pod-only",
    service_account=None,
    secret_as_env_vars=None,
    secret_as_file=None,
)

_config_env_var = "KANIKO_REMOTE_CONFIG"
_config_name = ".kaniko-remote.yaml"


class Config:
    _snake_caseify_regex = re.compile(r"(?<!^)(?=[A-Z])")

    def __init__(self) -> None:
        config_location = os.environ.get(_config_env_var, None)
        if config_location is None:
            relative_config = f"{os.getcwd()}/{_config_name}"
            user_config = os.path.abspath(f"~/{_config_name}")
            if os.path.exists(relative_config):
                config_location = relative_config
            elif os.path.exists(user_config):
                config_location = user_config
            else:
                raise ValueError(
                    'Could not find kaniko-remote config file. Please create one with "kaniko-remote configure"'
                )
        logger.info(f"Using config file: {config_location}")

        with open(config_location, "r") as yaml_file:
            parser = YAML(typ="safe")
            self.c = DeflatableDict(d=parser.load(yaml_file))
        logger.debug(f"Parsed config as: {self.c}")

    def _snake_caseify_dict(self, d: dict) -> dict:
        return {self._snake_caseify_regex.sub("_", k).lower(): v for k, v in d.items()}

    def get_kubeconfig(self) -> Optional[str]:
        return self.c.get("kubernetes.kubeconfig", None)

    def get_namespace(self) -> str:
        return self.c.get("kubernetes.namespace", "default")

    def get_builder_options(self) -> dict:
        return _default_builder_options | self._snake_caseify_dict(self.c.get("builder", {}))

    def list_all_authorisers(self) -> list:
        return [a["url"] for a in self.c.get("auth", [])]

    def list_always_mount_authorisers(self) -> list:
        return [a for a in self.list_all_authorisers() if self.c.get(f"auth.{a}.always_mount", False)]

    def get_authoriser_options(self, url) -> dict:
        matching = [a for a in self.c.get("auth", []) if a["url"] == url]
        if len(matching) == 0:
            return _default_auth_options
        return _default_auth_options | self._snake_caseify_dict(matching[0])
