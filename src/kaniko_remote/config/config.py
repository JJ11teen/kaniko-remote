from typing import Optional

"""
context_transfer: "exec"
image_push:
    azure:
        auth: "k8s"
"""


class Config:
    def get_pod_service_account_name(self) -> Optional[str]:
        return ""
