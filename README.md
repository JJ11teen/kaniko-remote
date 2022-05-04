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
  namespace: kaniko-remote
builder:
  instanceId: lucas
  cpu: 1
  memory: 1G
  kanikoImage: ""
  setupImage: ""
  additionalLabels:
    yes: hello
  additionalAnnotations:
    why: not
  kanikoArgs:
  - --use-new-run
auth:
  - url: eliiza.azurecr.io
    mount: always
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