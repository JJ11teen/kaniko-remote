import logging
import os

import pytest
from _pytest.python import Metafunc
from kubernetes.client.models import V1Pod

from kaniko_remote.config import Config
from kaniko_remote.k8s.specs import K8sSpecs


@pytest.fixture()
def local_config_location():
    return os.getcwd() + "/.kaniko-remote.yaml"


@pytest.fixture()
def user_config_location():
    return os.path.expanduser("~/.kaniko-remote.yaml")


@pytest.fixture(scope="function")
def remove_configs(local_config_location, user_config_location):
    try:
        os.remove(local_config_location)
    except FileNotFoundError:
        pass
    try:
        os.remove(user_config_location)
    except FileNotFoundError:
        pass
    try:
        os.environ.pop("KANIKO_REMOTE_CONFIG")
    except KeyError:
        pass


@pytest.fixture()
def base_pod() -> V1Pod:
    return K8sSpecs.generate_pod_spec(
        name="regular-instance",
        cpu="1",
        memory="1",
        kaniko_image="kaniko",
        setup_image="setup",
        additional_labels={},
        additional_annotations={},
    )


def pytest_generate_tests(metafunc: Metafunc):
    for param in metafunc.fixturenames:
        if param.startswith("test_config_"):
            config_name = param[12:]
            os.environ["KANIKO_REMOTE_CONFIG"] = os.path.abspath(
                f"{metafunc.config.rootdir}/tests/configs/{config_name}.yaml"
            )
            metafunc.parametrize(
                param,
                [Config()],
            )
