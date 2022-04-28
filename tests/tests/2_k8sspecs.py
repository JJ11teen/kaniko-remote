from __future__ import annotations

from importlib import resources

import pytest
from kubernetes.client.models import V1Container, V1Pod

from kaniko_remote import __version__ as kaniko_version
from kaniko_remote.k8s.specs import K8sSpecs


class K8sSpecTests:
    def test_generate_pod_spec(self):
        pod = K8sSpecs.generate_pod_spec(
            name="my-cool-instance-id-000",
            cpu="800m",
            memory="2G",
            kaniko_image="kaniko",
            setup_image="setup",
            additional_labels={},
            additional_annotations={},
        )

        assert pod.kind == "Pod"
        assert "my-cool-instance-id-000" in pod.metadata["generateName"]
        assert pod.metadata["labels"] == {
            "app.kubernetes.io/name": "kaniko-remote",
            "app.kubernetes.io/component": "builder",
            "kaniko-remote/instance": "my-cool-instance-id-000",
        }
        assert pod.metadata["annotations"] == {
            "kaniko-remote/version": kaniko_version,
        }
        assert len(pod.spec.containers) == 1
        assert len(pod.spec.init_containers) == 1
        assert len(pod.spec.volumes) == 1

        setup_container: V1Container = pod.spec.init_containers[0]
        assert setup_container.image == "setup"
        assert setup_container.command == ["sh", "-c"]
        assert setup_container.args == ["until [ -e /kaniko/.docker/config.json ]; do sleep 1; done"]
        assert setup_container.resources["requests"]["cpu"] == "800m"
        assert setup_container.resources["limits"]["cpu"] == "800m"
        assert setup_container.resources["requests"]["memory"] == "2G"
        assert setup_container.resources["limits"]["memory"] == "2G"
        assert len(setup_container.volume_mounts) == 1

        build_container: V1Container = pod.spec.containers[0]
        assert build_container.image == "kaniko"
        assert build_container.command is None
        assert build_container.args is None
        assert build_container.resources["requests"]["cpu"] == "800m"
        assert build_container.resources["limits"]["cpu"] == "800m"
        assert build_container.resources["requests"]["memory"] == "2G"
        assert build_container.resources["limits"]["memory"] == "2G"
        assert len(build_container.volume_mounts) == 1

    def test_generate_pod_spec_labels_and_annotations(self):
        pod = K8sSpecs.generate_pod_spec(
            name="instance-with-additional-labels",
            cpu="800m",
            memory="2G",
            kaniko_image="kaniko",
            setup_image="setup",
            additional_labels={
                "some-kool": "l.a.b.e.l.s",
            },
            additional_annotations={
                "and://lets": "not/forget/about.annotations",
                "yeah": "we://like.them.too",
            },
        )

        assert pod.metadata["labels"] == {
            "some-kool": "l.a.b.e.l.s",
            "app.kubernetes.io/name": "kaniko-remote",
            "app.kubernetes.io/component": "builder",
            "kaniko-remote/instance": "instance-with-additional-labels",
        }
        assert pod.metadata["annotations"] == {
            "and://lets": "not/forget/about.annotations",
            "yeah": "we://like.them.too",
            "kaniko-remote/version": kaniko_version,
        }

        pod_with_default_labels = K8sSpecs.generate_pod_spec(
            name="instance-with-default-labels",
            cpu="800m",
            memory="2G",
            kaniko_image="kaniko",
            setup_image="setup",
            additional_labels={
                "woo": "url://to.a/website",
                "app.kubernetes.io/name": "woah",
            },
            additional_annotations={
                "kaniko-remote/version": "version-one-thousand",
            },
        )

        assert pod_with_default_labels.metadata["labels"] == {
            "woo": "url://to.a/website",
            "app.kubernetes.io/name": "kaniko-remote",
            "app.kubernetes.io/component": "builder",
            "kaniko-remote/instance": "instance-with-default-labels",
        }
        assert pod_with_default_labels.metadata["annotations"] == {
            "kaniko-remote/version": kaniko_version,
        }

    def test_mount_context_for_exec_transfer(self, base_pod: V1Pod):
        base_pod = K8sSpecs.mount_context_for_exec_transfer(base_pod)
        setup_container: V1Container = base_pod.spec.init_containers[0]
        build_container: V1Container = base_pod.spec.containers[0]

        assert len(base_pod.spec.volumes) == 2
        assert len(build_container.volume_mounts) == 2
        assert len(setup_container.volume_mounts) == 2

    def test_set_kaniko_args(self, base_pod: V1Pod):
        with pytest.raises(ValueError, match="Missing required kaniko argument"):
            K8sSpecs.set_kaniko_args(pod=base_pod)
        with pytest.raises(ValueError, match="Missing required kaniko argument"):
            K8sSpecs.set_kaniko_args(pod=base_pod, context="needs/context/and/destination")
        with pytest.raises(ValueError, match="Missing required kaniko argument"):
            K8sSpecs.set_kaniko_args(pod=base_pod, destination="needs/context/and/destination")

        base_pod = K8sSpecs.set_kaniko_args(
            pod=base_pod,
            context="dir",
            destination="tag",
        )
        build_container: V1Container = base_pod.spec.containers[0]

        assert build_container.command is None
        assert len(build_container.args) == 3
        assert '--context="dir"' in build_container.args
        assert '--destination="tag"' in build_container.args
        assert '--dockerfile="."' in build_container.args

        base_pod = K8sSpecs.set_kaniko_args(
            pod=base_pod,
            context="dir",
            destination="tag",
            dockerfile="unusual",
        )
        build_container: V1Container = base_pod.spec.containers[0]

        assert build_container.command is None
        assert len(build_container.args) == 3
        assert '--context="dir"' in build_container.args
        assert '--destination="tag"' in build_container.args
        assert '--dockerfile="unusual"' in build_container.args

        base_pod = K8sSpecs.set_kaniko_args(
            pod=base_pod,
            **{
                "context": "dir",
                "destination": "tag",
                "build-arg": "MY_ARG=VALUE",
            },
        )
        build_container: V1Container = base_pod.spec.containers[0]

        assert build_container.command is None
        assert len(build_container.args) == 4
        assert '--context="dir"' in build_container.args
        assert '--destination="tag"' in build_container.args
        assert '--dockerfile="."' in build_container.args
        assert '--build-arg="MY_ARG=VALUE"' in build_container.args

    def test_replace_service_account(self, base_pod: V1Pod):
        assert base_pod.spec.automount_service_account_token == False

        base_pod = K8sSpecs.replace_service_account(base_pod, "service-account-one")
        assert base_pod.spec.automount_service_account_token == True
        assert base_pod.spec.service_account_name == "service-account-one"

        base_pod = K8sSpecs.replace_service_account(base_pod, "service-account-two")
        assert base_pod.spec.automount_service_account_token == True
        assert base_pod.spec.service_account_name == "service-account-two"

    def test_append_env_from_secret(self, base_pod: V1Pod):
        build_container: V1Container = base_pod.spec.containers[0]

        assert build_container.env_from is None

        base_pod = K8sSpecs.append_env_from_secret(base_pod, "secret-name-one")
        build_container: V1Container = base_pod.spec.containers[0]
        assert len(build_container.env_from) == 1
        assert build_container.env_from[0].secret_ref.name == "secret-name-one"

        base_pod = K8sSpecs.append_env_from_secret(base_pod, "secret-name-two")
        build_container: V1Container = base_pod.spec.containers[0]
        assert len(build_container.env_from) == 2
        assert build_container.env_from[0].secret_ref.name == "secret-name-one"
        assert build_container.env_from[1].secret_ref.name == "secret-name-two"

    def test_append_env_from_config_map(self, base_pod: V1Pod):
        build_container: V1Container = base_pod.spec.containers[0]

        assert build_container.env_from is None

        base_pod = K8sSpecs.append_env_from_config_map(base_pod, "cm-name-one")
        build_container: V1Container = base_pod.spec.containers[0]
        assert len(build_container.env_from) == 1
        assert build_container.env_from[0].config_map_ref.name == "cm-name-one"

        base_pod = K8sSpecs.append_env_from_config_map(base_pod, "cm-name-two")
        build_container: V1Container = base_pod.spec.containers[0]
        assert len(build_container.env_from) == 2
        assert build_container.env_from[0].config_map_ref.name == "cm-name-one"
        assert build_container.env_from[1].config_map_ref.name == "cm-name-two"

    def test_append_env_var(self, base_pod: V1Pod):
        build_container: V1Container = base_pod.spec.containers[0]

        assert build_container.env is None

        base_pod = K8sSpecs.append_env_var(base_pod, "KEY_ONE", "VALUE_A")
        build_container: V1Container = base_pod.spec.containers[0]
        assert len(build_container.env) == 1
        assert build_container.env[0].name == "KEY_ONE"
        assert build_container.env[0].value == "VALUE_A"

        base_pod = K8sSpecs.append_env_var(base_pod, "KEY_TWO", "VALUE_B")
        build_container: V1Container = base_pod.spec.containers[0]
        assert len(build_container.env) == 2
        assert build_container.env[0].name == "KEY_ONE"
        assert build_container.env[0].value == "VALUE_A"
        assert build_container.env[1].name == "KEY_TWO"
        assert build_container.env[1].value == "VALUE_B"

    def test_append_volume_from_secret(self, base_pod: V1Pod):
        build_container: V1Container = base_pod.spec.containers[0]

        assert len(base_pod.spec.volumes) == 1
        assert len(build_container.volume_mounts) == 1

        base_pod = K8sSpecs.append_volume_from_secret(base_pod, "secret-name", "/mnt/path/1")

        assert len(base_pod.spec.volumes) == 2
        assert len(build_container.volume_mounts) == 2
        assert base_pod.spec.volumes[1].secret.secret_name == "secret-name"
        assert build_container.volume_mounts[1].mount_path == "/mnt/path/1"
        assert base_pod.spec.volumes[1].name == build_container.volume_mounts[1].name

        base_pod = K8sSpecs.append_volume_from_secret(base_pod, "secret-two", "/mnt/path/2")

        assert len(base_pod.spec.volumes) == 3
        assert len(build_container.volume_mounts) == 3
        assert base_pod.spec.volumes[2].secret.secret_name == "secret-two"
        assert build_container.volume_mounts[2].mount_path == "/mnt/path/2"
        assert base_pod.spec.volumes[2].name == build_container.volume_mounts[2].name

    def test_append_volume_from_config_map(self, base_pod: V1Pod):
        build_container: V1Container = base_pod.spec.containers[0]

        assert len(base_pod.spec.volumes) == 1
        assert len(build_container.volume_mounts) == 1

        base_pod = K8sSpecs.append_volume_from_config_map(base_pod, "cm-name", "/mnt/path/1")

        assert len(base_pod.spec.volumes) == 2
        assert len(build_container.volume_mounts) == 2
        assert base_pod.spec.volumes[1].config_map.name == "cm-name"
        assert build_container.volume_mounts[1].mount_path == "/mnt/path/1"
        assert base_pod.spec.volumes[1].name == build_container.volume_mounts[1].name

        base_pod = K8sSpecs.append_volume_from_config_map(base_pod, "cm-two", "/mnt/path/2")

        assert len(base_pod.spec.volumes) == 3
        assert len(build_container.volume_mounts) == 3
        assert base_pod.spec.volumes[2].config_map.name == "cm-two"
        assert build_container.volume_mounts[2].mount_path == "/mnt/path/2"
        assert base_pod.spec.volumes[2].name == build_container.volume_mounts[2].name
