FROM mcr.microsoft.com/vscode/devcontainers/dotnet:6.0

# Install az
RUN curl -sS https://dl.yarnpkg.com/debian/pubkey.gpg | sudo apt-key add - \
    && curl -sL https://aka.ms/InstallAzureCLIDeb | bash

# Install gcloud
RUN echo "deb [signed-by=/usr/share/keyrings/cloud.google.gpg] http://packages.cloud.google.com/apt cloud-sdk main" | tee -a /etc/apt/sources.list.d/google-cloud-sdk.list && curl https://packages.cloud.google.com/apt/doc/apt-key.gpg | apt-key --keyring /usr/share/keyrings/cloud.google.gpg  add - && apt-get update -y && apt-get install google-cloud-sdk -y

# Install aws
RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip" \
    && unzip awscliv2.zip \
    && ./aws/install

# Install kubectl
RUN curl -sLO "https://dl.k8s.io/release/v1.23.5/bin/linux/amd64/kubectl" \
    && chmod +x ./kubectl \
    && sudo mv ./kubectl /usr/local/bin/kubectl \
    && kubectl version --client

# Install kubelogin
RUN curl -sLO "https://github.com/Azure/kubelogin/releases/download/v0.0.12/kubelogin-linux-amd64.zip" \
    && unzip ./kubelogin-linux-amd64.zip -d kubelogin \
    && chmod +x ./kubelogin/bin/linux_amd64/kubelogin \
    && sudo mv ./kubelogin/bin/linux_amd64/kubelogin /usr/local/bin/kubelogin \
    && rm -r ./kubelogin-linux-amd64.zip ./kubelogin
