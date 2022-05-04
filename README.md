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
  namespace: kaniko-remote # Default is default
builder:
  instanceId: lucas # Default is to get the username from the environment
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
  pod_start_timeout: 300 # In seconds, default 5 mins
  pod_transfer_packet_size: 9e3 # In bytes, default 9kB
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
  instanceId: lucas
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