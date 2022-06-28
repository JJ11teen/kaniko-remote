# kaniko-remote
Enable familiar `docker build` semantics using kaniko remotely on a preconfigured k8s cluster

## Quick Start
1. Download and unpack the latest binary for your system. Rename to `docker` if you would like to exactly match the docker cli (useful if using scripts that expect the docker cli).

1. Ensure .kube/config is setup with permission to: create, get, watch, delete for: pods, pods/exec, pods/log. You can limit to the namespace kaniko-remote is configured to use.

1. Run docker build commands as expected:
  ```bash
./kaniko-remote build -t registry.fish/my/cool-image:latest .
# Or if you rename to docker and add to your path:
docker build -t registry.fish/my/cool-image:latest .
```

## Config

kaniko-remote will check for a configuration file in the following locations, using the first it finds. The configuration in use at run time is logged for easy confirmation.
1. The path specified by the env var `KANIKO_REMOTE_CONFIG` if it is set.
1. The `.kaniko-remote.yaml` file in the current working directory.
1. The `.kaniko-remote.yaml` in the user's home directory.
1. An empty configuration file.

The kaniko-remote configuration file supports the following options:

```yaml
kubernetes:
  # kubeconfig: 
  # context
  namespace: kaniko-remote # Defaults to "default", unless running incluster in which case defaults to the current namespace
builder:
  name: lucas # Default is to get the username from the environment
  cpu: 1 # Default is 1, accepts any value k8s accepts
  memory: 1G # Default is 1G, accepts any value k8s accepts
  kanikoImage: "" # Default is gcr.io/kaniko-project/executor:latest
  setupImage: "" # Default is busybox:stable
  additionalLabels:
    yes: hello
  additionalAnnotations:
    why: not
  kanikoArgs: # Replaces default if provided, default is:
  - --use-new-run
  podStartTimeout: 300 # In seconds, default 5 mins
  podTransferPacketSize: 14e3 # In bytes, default 14kB
# Tags are optional. By default kaniko-remote will use the tag specified on the command line.
tags:
  # A tag to use to if no tags are specifed on the command line
  default: eliiza.azurecr.io/some-default-image
  # Always use the static tag if set (overwriting the tag specified on the command line).
  # If static is set, other options cannot be set
  static: eliiza.azurecr.io/specific-image:v1
  # A prefix to add to the tag specified on the command line
  prefix: eliiza.azurecr.io/dataiku
  # A mapping of regex patterns to match against and replace with
  regexes:
    pattern: template
auth:
  # List of auth configs to use.
  # By default there are none, which is unlikely to work in a production setting.
  # See below for available options
```

An example config that I use:

```yaml
kubernetes:
  namespace: kaniko-remote
builder:
  name: lucas
tags:
  prefix: eliiza.azurecr.io/lucas-dev
auth:
  - type: acr
    registry: eliiza.azurecr.io
    mount: always
    env:
    - fromSecret: eliiza-azurecr-push-sp
```

## Configure Auth

By default, no authentication is configured. This is unlikely to work in a production setting and a warning is logged if this is the case.

Each auth entry must have a 'type' and either a 'url' specified or 'mount' set to 'always'. If a url is specified kaniko-remote will only configure the builder to use that auth configuration if the url matches one of the destination tags for the image being built.

The type of the auth determines the other available/required configuration options, see below.

### Pod Only

The simplest form of authentication is `type: pod-only`. This auth can setup volume mounts of env vars for the builder pod, but does not configure any additional kaniko authentication.

Environment variables may be specified as follows:
```yaml
  env:
  # mounted from preconfigured kubernetes secrets:
  - fromSecret: my-k8s-secret-with-env-vars
  # mounted from preconfigured kubernetes config maps:
  - fromConfigMap: my-k8s-config-map-with-env-vars
  # a raw key/value pair in the config (not recommended):
  - key: SOME_KEY
    value: big-secret
```

Volumes may be specified as follows:
```yaml
  volumes:
  # mounted from preconfigured kubernetes secrets:
  - fromSecret: my-k8s-secret-with-file
    mountPath: /etc/secret-file
  # mounted from preconfigured kubernetes config maps:
  - fromConfigMap: my-k8s-config-map-with-file
    mountPath: /etc/config-map-file
```

A single pod-only auth entry may have multiple of both env and volumes configured.

### ACR

`type: acr`
A 'registry' option is required to be set to the hostname of your Azure Container Registry instance (usually takes the form `<name>.azurecr.io`). Can additionally have all the options available to pod-only auth, and requires enough pod-auth to satisfy the ACR credential helper as specified here: https://github.com/chrismellard/docker-credential-acr-env

### Docker Hub

`type: docker-hub`
Your docker registry username and password must be configured with 'username' and 'password' respectively.

### GCR

`type: gcr`
Your gcr.io project name must be configured with 'project', or parsable from the url. The project is parseable from the url as follows: `gcr.io/<PROJECT>/`. Can additionally have all the options available to pod-only auth, and requires enough pod-auth to satisfy the GCR credential helper as specified here: https://github.com/GoogleCloudPlatform/docker-credential-gcr. Note that:
> In particular, [the gcr credential helper] respects Application Default Credentials and is capable of generating credentials automatically (without an explicit login operation) when running in App Engine or Compute Engine.

## License

kaniko-remote is licensed under the MIT license
