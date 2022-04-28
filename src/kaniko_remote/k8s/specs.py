from __future__ import annotations

from typing import List

from kubernetes.client.models import (
    V1ConfigMapEnvSource,
    V1Container,
    V1EnvFromSource,
    V1EnvVar,
    V1Pod,
    V1PodSpec,
    V1SecretReference,
    V1SecretVolumeSource,
    V1Volume,
    V1VolumeMount,
)

from kaniko_remote import __version__


class K8sSpecs:
    @classmethod
    def generate_pod_spec(
        cls,
        name: str,
        cpu: str,
        memory: str,
        kaniko_image: str,
        setup_image: str,
        additional_labels: dict,
        additional_annotations: dict,
        **kwargs,
    ) -> V1Pod:
        docker_config_volume_mounts = [V1VolumeMount(name="config", mount_path="/kaniko/.docker")]

        resources = dict(
            limits=dict(cpu=cpu, memory=memory),
            requests=dict(cpu=cpu, memory=memory),
        )

        # Additional labels/annotations could actually be a DeflatableDict at this point,
        # which openapi-generator throws up about, so force each into dict before adding
        # 'system' labels/annotations
        labels = {
            **additional_labels,
            **{
                "app.kubernetes.io/name": "kaniko-remote",
                "app.kubernetes.io/component": "builder",
                "kaniko-remote/builder-name": name,
            },
        }
        annotations = {
            **additional_annotations,
            **{
                "kaniko-remote/version": __version__,
            },
        }

        return V1Pod(
            api_version="v1",
            kind="Pod",
            metadata=dict(
                generateName=f"kaniko-remote-{name}-",
                labels=labels,
                annotations=annotations,
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
    def set_kaniko_args(cls, pod: V1Pod, preparsed_args: List[str], **kwargs) -> V1Pod:
        required_args = ["destinations"]

        for required_arg in required_args:
            if required_arg not in kwargs:
                raise ValueError(f"Missing required kaniko argument --{required_arg}")

        # Start with defaults which may be overriden
        args = {
            "dockerfile": "Dockerfile",
            "digest-file": "/dev/termination-log",
        }

        # Pop multi-args so they can be expanded later
        destinations = kwargs.pop("destinations")
        build_args = kwargs.pop("build_args", ())
        labels = kwargs.pop("labels", ())

        args.update(kwargs)
        args = [f"--{k}={v}" for k, v in args.items() if v is not None]

        args.extend(preparsed_args)

        for d in destinations:
            args.append(f"--destination={d}")
        for ba in build_args:
            args.append("--build-arg")
            args.append(ba)
        for l in labels:
            args.append(f"--label={l}")

        pod.spec.containers[0].command = None
        pod.spec.containers[0].args = args
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
    def append_env_from_config_map(cls, pod: V1Pod, config_map_name: str) -> V1Pod:
        container: V1Container = pod.spec.containers[0]
        if not container.env_from:
            container.env_from = []
        container.env_from.append(V1EnvFromSource(config_map_ref=V1ConfigMapEnvSource(name=config_map_name)))
        return pod

    @classmethod
    def append_env_var(cls, pod: V1Pod, env_var_name: str, env_var_value) -> V1Pod:
        container: V1Container = pod.spec.containers[0]
        if not container.env:
            container.env = []
        container.env.append(V1EnvVar(name=env_var_name, value=env_var_value))
        return pod

    @classmethod
    def append_volume_from_secret(cls, pod: V1Pod, secret_name: str, mount_path: str) -> V1Pod:
        mounts: List[V1VolumeMount] = pod.spec.containers[0].volume_mounts
        volumes: List[V1Volume] = pod.spec.volumes
        mounts.append(V1VolumeMount(name=secret_name, mount_path=mount_path))
        volumes.append(V1Volume(name=secret_name, secret=V1SecretVolumeSource(secret_name=secret_name)))
        return pod

    @classmethod
    def append_volume_from_config_map(cls, pod: V1Pod, config_map_name: str, mount_path: str) -> V1Pod:
        mounts: List[V1VolumeMount] = pod.spec.containers[0].volume_mounts
        volumes: List[V1Volume] = pod.spec.volumes
        mounts.append(V1VolumeMount(name=config_map_name, mount_path=mount_path))
        volumes.append(V1Volume(name=config_map_name, config_map=V1ConfigMapEnvSource(name=config_map_name)))
        return pod
