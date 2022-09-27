FROM mcr.microsoft.com/dotnet/sdk:7.0.100-rc.1-bullseye-slim

# Install az cli (use pip as other methods are not amd/arm compatible)
# https://github.com/Azure/azure-cli/issues/7368#issuecomment-921578936
RUN DEBIAN_FRONTEND=noninteractive \
    apt-get update \
    && apt-get -y install --no-install-recommends \
        gcc \
        python3-pip \
        python3-dev \
        libsodium-dev \
        # Needed to unzip kubelogin
        unzip \
    && SODIUM_INSTALL=system pip install pynacl \
    && pip install azure-cli

# # Install gcloud
# RUN echo "deb [signed-by=/usr/share/keyrings/cloud.google.gpg] http://packages.cloud.google.com/apt cloud-sdk main" | tee -a /etc/apt/sources.list.d/google-cloud-sdk.list && curl https://packages.cloud.google.com/apt/doc/apt-key.gpg | apt-key --keyring /usr/share/keyrings/cloud.google.gpg  add - && apt-get update -y && apt-get install google-cloud-sdk -y

# # Install aws
# RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip" \
#     && unzip awscliv2.zip \
#     && ./aws/install

# Install kubectl
RUN if [ "$(uname -m)" = "aarch64" ]; then ARCH="arm64"; else ARCH="amd64"; fi \
    && curl -sLO "https://dl.k8s.io/release/v1.23.5/bin/linux/${ARCH}/kubectl" \
    && chmod +x ./kubectl \
    && mv ./kubectl /usr/local/bin/kubectl \
    && kubectl version --client

# Install kubelogin
RUN if [ "$(uname -m)" = "aarch64" ]; then ARCH="arm64"; else ARCH="amd64"; fi \
    && curl -sLO "https://github.com/Azure/kubelogin/releases/download/v0.0.20/kubelogin-linux-${ARCH}.zip" \
    && unzip ./kubelogin-linux-${ARCH}.zip -d kubelogin \
    && chmod +x ./kubelogin/bin/linux_${ARCH}/kubelogin \
    && mv ./kubelogin/bin/linux_${ARCH}/kubelogin /usr/local/bin/kubelogin \
    && rm -r ./kubelogin-linux-${ARCH}.zip ./kubelogin
