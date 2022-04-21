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
        use_debug_image: bool,
        cpu: str,
        memory: str,
        additional_labels: dict,
        additional_annotations: dict,
    ) -> V1Pod:
        kaniko_image = (
            "gcr.io/kaniko-project/executor:debug" if use_debug_image else "gcr.io/kaniko-project/executor:latest"
        )
        setup_image = "busybox:stable"

        docker_config_volume_mounts = [V1VolumeMount(name="config", mount_path="/kaniko/.docker")]
        resources = dict(
            limits=dict(cpu=cpu, memory=memory),
            requests=dict(cpu=cpu, memory=memory),
        )

        # Additional labels could actually be a DeflatableDict at this point,
        # which openapi-generator throws up about, so force into dict
        labels = dict(**additional_labels) | {
            "app.kubernetes.io/name": "kaniko-remote",
            "app.kubernetes.io/component": "builder",
            "kaniko-remote/instance": instance_id,
            "kaniko-remote/version": __version__,
        }

        return V1Pod(
            api_version="v1",
            kind="Pod",
            metadata=dict(
                generateName=f"kaniko-remote-{instance_id}-",
                labels=labels,
                # Additional annotations could actually be a DeflatableDict at this point,
                # which openapi-generator throws up about, so force into dict
                annotations=dict(**additional_annotations),
            ),
            spec=V1PodSpec(
                automount_service_account_token=False,
                init_containers=[
                    V1Container(
                        name="setup",
                        image=setup_image,
                        command=["sh", "-c"],
                        # args=["trap : TERM INT; sleep 9999999999d & wait"],
                        args=["until [ -e /kaniko/.docker/config.json ]; do sleep 1; done"],
                        volume_mounts=docker_config_volume_mounts,
                        resources=resources,
                    )
                ],
                containers=[
                    V1Container(
                        name="builder",
                        image=kaniko_image,
                        # args=kaniko_args,
                        # command=["/busybox/sh", "-c"],
                        # args=["trap : TERM INT; sleep 9999999999d & wait"],
                        volume_mounts=docker_config_volume_mounts,
                        resources=resources,
                    )
                ],
                volumes=[V1Volume(name="config", empty_dir={})],
            ),
        )

    @classmethod
    def mount_context_for_exec_transfer(cls, pod: V1Pod) -> V1Pod:
        pod.spec.volumes.append(V1Volume(name="context", empty_dir={}))

        # For some reason appending to either the main container or the init container appends to both,
        # so we only append to one.
        pod.spec.containers[0].volume_mounts.append(V1VolumeMount(name="context", mount_path="/workspace"))
        return pod

    @classmethod
    def add_kaniko_args(cls, pod: V1Pod, **kwargs) -> V1Pod:
        required_args = ["context", "destination"]
        default_args = [("dockerfile", ".")]

        for required_arg in required_args:
            if required_arg not in kwargs:
                raise ValueError(f"Missing required kaniko argument --{required_arg}")

        for key, value in default_args:
            if key not in kwargs:
                kwargs[key] = value

        pod.spec.containers[0].command = None
        pod.spec.containers[0].args = [f"--{k}={v}" for k, v in kwargs.items()]
        return pod

    @classmethod
    def replace_service_account(cls, pod: V1Pod, service_account_name: str) -> V1Pod:
        pod.spec.service_account_name = service_account_name
        pod.spec.automount_service_account_token = True
        return pod

    @classmethod
    def append_env_from_secret(cls, pod: V1Pod, secret_name: str) -> V1Pod:
        container: V1Container = pod.spec.containers[0]
        if not container.env_from:
            container.env_from = []
        container.env_from.append(V1EnvFromSource(secret_ref=V1SecretReference(name=secret_name)))
        return pod

    @classmethod
    def append_file_mount_from_secret(cls, pod: V1Pod, secret_name: str) -> V1Pod:
        raise NotImplementedError()
