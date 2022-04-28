from __future__ import annotations

import getpass
import os
from pathlib import Path

import pytest

from kaniko_remote.config import Config


def _write_test_config(location: Path, namespace):
    with open(location, "w") as f:
        f.write(
            f"""
        kubernetes:
            namespace: {namespace}
        """
        )


@pytest.mark.usefixtures("remove_configs")
class ConfigTests:
    def test_config_location_preference(self, tmp_path, user_config_location, local_config_location):
        env_var_config = tmp_path / "some-other-config.yaml"

        _write_test_config(user_config_location, "user-config")
        _write_test_config(env_var_config, "env-var-config")

        # If no local config, default to user config
        config = Config()
        assert config.get_namespace() == "user-config"

        # If now a local config, use that
        _write_test_config(local_config_location, "local-config")
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

        builder = config.get_builder_options()
        assert builder["name"] == getpass.getuser()
        assert builder["cpu"] == "1"
        assert builder["memory"] == "1G"
        assert builder["kaniko_image"] == "gcr.io/kaniko-project/executor:latest"
        assert builder["setup_image"] == "busybox:stable"
        assert len(builder["additional_labels"]) == 0
        assert len(builder["additional_annotations"]) == 0

        assert len(config.list_always_mount_authorisers()) == 0
        assert len(config.list_all_authorisers()) == 0

    def test_builder_config(self, test_config_builder: Config):
        assert test_config_builder.get_kubeconfig() is None
        assert test_config_builder.get_namespace() == "builder-config"
        assert len(test_config_builder.list_all_authorisers()) == 0

        builder = test_config_builder.get_builder_options()
        assert builder["name"] == "tests"
        assert builder["cpu"] == "200m"
        assert builder["memory"] == "200m"
        assert builder["kaniko_image"] == "mycool.registry/kaniko-image"
        assert builder["setup_image"] == "mycool.registry/busybox-image"

        labels = builder["additional_labels"]
        assert len(labels) == 2
        assert labels["test-label-one"] == "yes-here-is-one"
        assert labels["test-label-two"] == "https://github.com/JJ11teen/kaniko-remote"

        annotations = builder["additional_annotations"]
        assert len(annotations) == 2
        assert annotations["test-annotation-one"] == "yes-here-is-one"
        assert annotations["test-annotation-two"] == "https://github.com/JJ11teen/kaniko-remote"

        assert len(test_config_builder.list_always_mount_authorisers()) == 0
        assert len(test_config_builder.list_all_authorisers()) == 0

    def test_pod_only_config_auths_lists_correctly(self, test_config_pod_only_auth: Config):
        assert test_config_pod_only_auth.get_kubeconfig() is None
        assert test_config_pod_only_auth.get_namespace() == "pod-only-auth-config"

        auths = test_config_pod_only_auth.list_all_authorisers()
        assert auths == [
            "my.reg/env-explicit-key-values",
            "my.reg/env-from-k8s-resources",
            "my.reg/volumes-from-k8s-resources",
            "my.reg/everything-k8s-resources",
        ]

        always_auths = test_config_pod_only_auth.list_always_mount_authorisers()
        assert always_auths == ["my.reg/env-explicit-key-values"]

    def test_pod_only_config_env_explicit_key_values(self, test_config_pod_only_auth: Config):
        auth = test_config_pod_only_auth.get_authoriser_options("my.reg/env-explicit-key-values")

        assert auth["url"] == "my.reg/env-explicit-key-values"
        assert auth["type"] == "pod-only"
        assert auth["env"] == [
            {"key": "SOME_KEY_1", "value": "big-secret"},
            {"key": "SOME_KEY_2", "value": "not-so-big-secret"},
            {"key": "SOME_OTHER_VALUE", "value": "true"},
        ]
        assert auth["volumes"] == []

    def test_pod_only_config_env_from_k8s_resources(self, test_config_pod_only_auth: Config):
        auth = test_config_pod_only_auth.get_authoriser_options("my.reg/env-from-k8s-resources")

        assert auth["url"] == "my.reg/env-from-k8s-resources"
        assert auth["type"] == "pod-only"
        assert auth["env"] == [
            {"from_secret": "my-k8s-secret-with-env-vars"},
            {"from_config_map": "my-k8s-config-map-with-env-vars"},
            {"from_secret": "my-k8s-secret-with-env-vars-two"},
        ]
        assert auth["volumes"] == []

    def test_pod_only_config_volumes_from_k8s_resources(self, test_config_pod_only_auth: Config):
        auth = test_config_pod_only_auth.get_authoriser_options("my.reg/volumes-from-k8s-resources")

        assert auth["url"] == "my.reg/volumes-from-k8s-resources"
        assert auth["type"] == "pod-only"
        assert auth["env"] == []
        assert auth["volumes"] == [
            {"from_secret": "my-k8s-secret-with-file", "mount_path": "/etc/secret-file"},
            {"from_config_map": "my-k8s-config-map-with-file", "mount_path": "/etc/config-map-file"},
        ]

    def test_pod_only_config_everything_k8s_resources(self, test_config_pod_only_auth: Config):
        auth = test_config_pod_only_auth.get_authoriser_options("my.reg/everything-k8s-resources")

        assert auth["url"] == "my.reg/everything-k8s-resources"
        assert auth["type"] == "pod-only"
        assert auth["env"] == [
            {"from_secret": "my-k8s-secret-with-env-vars"},
            {"from_config_map": "my-k8s-config-map-with-env-vars"},
            {"from_secret": "my-k8s-secret-with-env-vars-two"},
        ]
        assert auth["volumes"] == [
            {"from_secret": "my-k8s-secret-with-file", "mount_path": "/etc/secret-file"},
            {"from_config_map": "my-k8s-config-map-with-file", "mount_path": "/etc/config-map-file"},
        ]
