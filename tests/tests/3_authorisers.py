import pytest
from kubernetes.client.models import V1Container, V1Pod

from kaniko_remote.authorisers import KanikoAuthoriser, get_matching_authorisers
from kaniko_remote.config import Config


@pytest.fixture()
def pod_only_auth_env_explicit_key_values(test_config_pod_only_auth: Config):
    url = "my.reg/env-explicit-key-values"
    authorisers = get_matching_authorisers([url], config=test_config_pod_only_auth)
    return [a for a in authorisers if a.url == url][0]


@pytest.fixture()
def pod_only_auth_everything_k8s_resources(test_config_pod_only_auth: Config):
    url = "my.reg/everything-k8s-resources"
    authorisers = get_matching_authorisers([url], config=test_config_pod_only_auth)
    return [a for a in authorisers if a.url == url][0]


@pytest.mark.usefixtures("remove_configs")
class AuthoriserTests:
    def test_pod_only_auths_match_correctly(self, test_config_pod_only_auth: Config):
        always_mount_urls = ["my.reg/env-explicit-key-values"]
        always_mount_auths = get_matching_authorisers([], config=test_config_pod_only_auth)

        assert [a.url for a in always_mount_auths] == always_mount_urls

        some_urls = ["my.reg/env-from-k8s-resources"]
        some_auths = get_matching_authorisers(some_urls, config=test_config_pod_only_auth)

        assert [a.url for a in some_auths] == always_mount_urls + some_urls

        some_other_urls = ["my.reg/volumes-from-k8s-resources", "my.reg/everything-k8s-resources"]
        some_other_auths = get_matching_authorisers(some_other_urls, config=test_config_pod_only_auth)

        assert [a.url for a in some_other_auths] == always_mount_urls + some_other_urls

    def test_pod_only_auth_ignores_docker_config(self, pod_only_auth_env_explicit_key_values: KanikoAuthoriser):
        empty = {}
        assert pod_only_auth_env_explicit_key_values.append_auth_to_docker_config(docker_config=empty) == empty

        not_empty = {"auths": {"my.reg": "some-special-token"}}
        assert pod_only_auth_env_explicit_key_values.append_auth_to_docker_config(docker_config=not_empty) == not_empty

    def test_pod_only_auth_env_explicit_key_values_parsed(
        self, pod_only_auth_env_explicit_key_values: KanikoAuthoriser, base_pod: V1Pod
    ):
        base_pod = pod_only_auth_env_explicit_key_values.append_auth_to_pod(pod_spec=base_pod)
        build_container: V1Container = base_pod.spec.containers[0]

        assert len(build_container.env) == 3
        assert build_container.env[0].name == "SOME_KEY_1"
        assert build_container.env[0].value == "big-secret"
        assert build_container.env[1].name == "SOME_KEY_2"
        assert build_container.env[1].value == "not-so-big-secret"
        assert build_container.env[2].name == "SOME_OTHER_VALUE"
        assert build_container.env[2].value == "true"

    def test_pod_only_auth_everything_k8s_resources_parsed(
        self, pod_only_auth_everything_k8s_resources: KanikoAuthoriser, base_pod: V1Pod
    ):
        base_pod = pod_only_auth_everything_k8s_resources.append_auth_to_pod(pod_spec=base_pod)
        build_container: V1Container = base_pod.spec.containers[0]

        assert len(build_container.env_from) == 3
        assert build_container.env_from[0].config_map_ref.name == "my-k8s-config-map-with-env-vars"
        assert build_container.env_from[1].secret_ref.name == "my-k8s-secret-with-env-vars"
        assert build_container.env_from[2].secret_ref.name == "my-k8s-secret-with-env-vars-two"

        assert len(base_pod.spec.volumes) == 3
        assert len(build_container.volume_mounts) == 3
        assert base_pod.spec.volumes[1].config_map.name == "my-k8s-config-map-with-file"
        assert build_container.volume_mounts[1].mount_path == "/etc/config-map-file"
        assert base_pod.spec.volumes[2].secret.secret_name == "my-k8s-secret-with-file"
        assert build_container.volume_mounts[2].mount_path == "/etc/secret-file"

    def test_pod_only_auth_multiple_authorisers_parsed(self, test_config_pod_only_auth: Config, base_pod: V1Pod):
        urls = ["my.reg/env-from-k8s-resources", "my.reg/volumes-from-k8s-resources"]
        for auth in get_matching_authorisers(urls, config=test_config_pod_only_auth):
            base_pod = auth.append_auth_to_pod(base_pod)
        build_container: V1Container = base_pod.spec.containers[0]

        assert len(build_container.env) == 3
        assert build_container.env[0].name == "SOME_KEY_1"
        assert build_container.env[0].value == "big-secret"
        assert build_container.env[1].name == "SOME_KEY_2"
        assert build_container.env[1].value == "not-so-big-secret"
        assert build_container.env[2].name == "SOME_OTHER_VALUE"
        assert build_container.env[2].value == "true"

        assert len(build_container.env_from) == 3
        assert build_container.env_from[0].config_map_ref.name == "my-k8s-config-map-with-env-vars"
        assert build_container.env_from[1].secret_ref.name == "my-k8s-secret-with-env-vars"
        assert build_container.env_from[2].secret_ref.name == "my-k8s-secret-with-env-vars-two"

        assert len(base_pod.spec.volumes) == 3
        assert len(build_container.volume_mounts) == 3
        assert base_pod.spec.volumes[1].config_map.name == "my-k8s-config-map-with-file"
        assert build_container.volume_mounts[1].mount_path == "/etc/config-map-file"
        assert base_pod.spec.volumes[2].secret.secret_name == "my-k8s-secret-with-file"
        assert build_container.volume_mounts[2].mount_path == "/etc/secret-file"
