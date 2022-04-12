from typing import Optional

from kubernetes.client.models import (
    V1Container,
    V1EnvFromSource,
    V1Pod,
    V1PodSpec,
    V1SecretReference,
    V1Volume,
    V1VolumeMount,
)

from kaniko_remote import __version__


class K8sSpecs:
    @classmethod
    def generate_pod_spec(
        cls,
        instance_id: str,
        kaniko_args: list[str],
        use_debug_image: bool,
        service_account_name: Optional[str],
    ) -> V1Pod:
        kaniko_image = (
            "gcr.io/kaniko-project/executor:debug" if use_debug_image else "gcr.io/kaniko-project/executor:latest"
        )
        setup_image = "busybox:stable"

        return V1Pod(
            api_version="v1",
            kind="Pod",
            metadata=dict(
                generateName=f"kaniko-remote-{instance_id}-",
                labels={
                    "app.kubernetes.io/name": "kaniko-remote",
                    "app.kubernetes.io/component": "builder",
                    "kaniko-remote/instance": instance_id,
                    "kaniko-remote/version": __version__,
                },
            ),
            spec=V1PodSpec(
                service_account_name=service_account_name,
                automount_service_account_token=service_account_name is not None,
                init_containers=[
                    V1Container(
                        name="setup",
                        image=setup_image,
                        command=["sh", "-c"],
                        # args=["trap : TERM INT; sleep 9999999999d & wait"],
                        args=["until [ -e /kaniko/.docker/config.json ]; do sleep 1; done"],
                        volume_mounts=[
                            V1VolumeMount(name="context", mount_path="/workspace"),
                            V1VolumeMount(name="config", mount_path="/kaniko/.docker"),
                        ],
                    )
                ],
                containers=[
                    V1Container(
                        name="builder",
                        image=kaniko_image,
                        args=kaniko_args,
                        # command=["/busybox/sh", "-c"],
                        # args=["trap : TERM INT; sleep 9999999999d & wait"],
                        volume_mounts=[
                            V1VolumeMount(name="context", mount_path="/workspace"),
                            V1VolumeMount(name="config", mount_path="/kaniko/.docker"),
                        ],
                    )
                ],
                volumes=[
                    V1Volume(name="context", empty_dir={}),
                    V1Volume(name="config", empty_dir={}),
                ],
            ),
        )

    @classmethod
    def build_kaniko_args(
        cls,
        **kwargs,
    ) -> list[str]:
        required_args = ["context", "destination"]
        default_args = [("dockerfile", ".")]

        for required_arg in required_args:
            if required_arg not in kwargs:
                raise ValueError(f"Missing required kaniko argument --{required_arg}")

        for key, value in default_args:
            if key not in kwargs:
                kwargs[key] = value

        return [f"--{k}={v}" for k, v in kwargs.items()]

    @classmethod
    def add_env_from_secret(cls, pod: V1Pod, secret_name: str) -> V1Pod:
        for container in pod.spec.containers:
            container: V1Container
            if not container.env_from:
                container.env_from = []
            container.env_from.append(V1EnvFromSource(secret_ref=V1SecretReference(name=secret_name)))
        return pod
