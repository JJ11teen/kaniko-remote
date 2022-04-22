import getpass
import os
from pathlib import Path

import pytest

from kaniko_remote.config import Config

_local_config = os.getcwd() + "/.kaniko-remote.yaml"
_user_config = os.path.expanduser("~/.kaniko-remote.yaml")


def _write_test_config(location: Path, namespace):
    with open(location, "w") as f:
        f.write(
            f"""
        kubernetes:
            namespace: {namespace}
        """
        )


@pytest.fixture(scope="function")
def remove_configs():
    try:
        os.remove(_local_config)
    except FileNotFoundError:
        pass
    try:
        os.remove(_user_config)
    except FileNotFoundError:
        pass
    try:
        os.environ.pop("KANIKO_REMOTE_CONFIG")
    except KeyError:
        pass


@pytest.mark.usefixtures("remove_configs")
class ConfigTests:
    def test_config_location_preference(self, tmp_path):
        env_var_config = tmp_path / "some-other-config.yaml"

        _write_test_config(_user_config, "user-config")
        _write_test_config(env_var_config, "env-var-config")

        # If no local config, default to user config
        config = Config()
        assert config.get_namespace() == "user-config"

        # If now a local config, use that
        _write_test_config(_local_config, "local-config")
        config = Config()
        assert config.get_namespace() == "local-config"

        # If env var specified, use that
        os.environ["KANIKO_REMOTE_CONFIG"] = str(env_var_config)
        config = Config()
        assert config.get_namespace() == "env-var-config"

    def test_default_config(self):
        config = Config()
        assert config.get_kubeconfig() is None
        assert config.get_namespace() == "default"
        assert len(config.list_all_authorisers()) == 0

        builder = config.get_builder_options()
        assert builder["instance_id"] == getpass.getuser()
        assert builder["cpu"] == "1"
        assert builder["memory"] == "1G"
        assert builder["kaniko_image"] == "gcr.io/kaniko-project/executor:latest"
        assert builder["setup_image"] == "busybox:stable"
        assert len(builder["additional_labels"]) == 0
        assert len(builder["additional_annotations"]) == 0
