# kaniko-remote
Enable familiar `docker build` semantics using remote kaniko on k8s

```yaml
kubernetes:
  # kubeconfig: 
  namespace: kaniko-remote
builder:
  instanceId: lucas
  cpu: 1
  memory: 1G
  # kanikoImage: ""
  # setupImage: ""
  # additionalLabels:
  #   yes: hello
  # additionalAnnotations:
  #   why: not
auth:
  - url: eliiza.azurecr.io
    alwaysMount: False
    type: acr
    serviceAccount: False
    secretAsEnvVars: eliiza-azurecr-push-sp
    secretAsFile: False
    #TODO: mount this as /secrets (from kaniko docs, for gcp)
    token: False
  - url: gs://kaniko-bucket
    type: pod-only
    serviceAccount: False
    secretAsEnvVars: False
    secretAsFile: False
  - url: s3://kaniko-bucket
    serviceAccount: False
    secretAsEnvVars: False
    secretAsFile: False
  - url: https://myaccount.blob.core.windows.net/container
    serviceAccount: False
    secretAsEnvVars: False
    secretAsFile: False
```