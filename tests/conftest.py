import logging

import pytest

from kaniko_remote.k8s.k8s import K8sWrapper


@pytest.fixture(scope="session")
def k8s():
    return K8sWrapper(kubeconfig=None, namespace="default")
