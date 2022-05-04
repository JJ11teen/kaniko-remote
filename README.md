# kaniko-remote
Enable familiar `docker build` semantics using kaniko remotely on a preconfigured k8s cluster

## Quick Start
Install with pip, optionally with the docker alias:

```bash
pip install kaniko-remote[docker]
```

Run docker build commands as expected:
```bash
docker build -t registry.fish/my/cool-image:latest .
# Or without the docker alias:
kaniko-remote build -t registry.fish/my/cool-image:latest .
```

## Config
```yaml
kubernetes:
  # kubeconfig: 
  # context
  namespace: kaniko-remote # Default is default
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
tag:
  fixed: eliiza.azurecr.io/specific-image:v1
  prepend: eliiza.azurecr.io/dataiku
auth: # Default is to have none of these
  - url: eliiza.azurecr.io
    mount: always # Defaults to only mounting each auth into the builder if the url matches the tag being built
    type: acr
    env:
    - fromSecret: eliiza-azurecr-push-sp
  - url: gs://kaniko-bucket
    type: pod-only
  - url: s3://kaniko-bucket
  - url: https://myaccount.blob.core.windows.net/container
```

```yaml
kubernetes:
  namespace: kaniko-remote
builder:
  name: lucas
tag:
  prepend: eliiza.azurecr.io/lucas-dev
auth:
  - url: eliiza.azurecr.io
    mount: always
    type: acr
    env:
    - fromSecret: eliiza-azurecr-push-sp
```

## License

kaniko-remote is licensed under the MIT license

Dependencies with other licenses are included under `/docs/3rd_party_licenses`