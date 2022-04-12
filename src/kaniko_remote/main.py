from email.policy import default
from pathlib import Path
from typing import Optional

import asyncclick as click

from kaniko_remote.builder import Builder
from kaniko_remote.k8s.k8s import K8sWrapper
from kaniko_remote.logging import init_logging


@click.group()
@click.option(
    "-v",
    "--verbosity",
    type=click.Choice(
        ["trace", "debug", "info", "warn", "error", "fatal", "panic"],
        case_sensitive=False,
    ),
    default="info",
    # help='Log level (trace, debug, info, warn, error, fatal, panic) (default "info")',
)
@click.pass_context
def cli(ctx: click.Context, verbosity: str):
    """
    Build and push images without Docker, using using kaniko on a configured k8s cluster

    This tool can be explicitly invoked as 'kaniko-remote'.
    If optionally installed, this tool can additionally be invoked as 'docker'.
    """
    init_logging(level=verbosity)


@cli.command()
@click.option(
    "-f",
    "--file",
    help='Name of the Dockerfile (Default is "PATH/Dockerfile")',
    type=click.Path(),
    default="Dockerfile",
)
@click.option(
    "-t",
    "--tag",
    required=True,
    help='Name and tag in the "name:tag" format',
    type=str,
)
@click.argument(
    "path",
    type=click.Path(exists=True),
)
@click.option(
    "-ns",
    "--namespace",
    default="default",
    help='K8s namespace to create builder pod in (Default is "Default")',
    type=str,
)
@click.option(
    "-s",
    "--env-from-secret",
    help="Name of k8s secret to use for environment variables of builder",
    type=str,
)
@click.option(
    "--acr-token",
    type=str,
    default=None,
    help="""
        Access token for ACR (Azure Container Registry) temporary authentication.
        Generate one with:
        az acr login --name <acr-name> --expose-token --output tsv --query accessToken
    """,
)
async def build(
    file: Path,
    tag: str,
    path: Path,
    namespace: str,
    env_from_secret: Optional[str],
    acr_token: Optional[str],
):
    """
    Build an image from a Dockerfile on a k8s cluster using kaniko

    This tool can be explicitly invoked as 'kaniko-remote'.
    If optionally installed, this tool can additionally be invoked as 'docker'.
    """
    with K8sWrapper(namespace=namespace) as k8s:
        with Builder(
            k8s_wrapper=k8s,
            instance_id="ad6ea8b",
            context=path,
            destination=tag,
            dockerfile=file,
            use_debug_image=True,
            env_from_secret=env_from_secret,
            # service_account_name="eliiza-azurecr-push",
            acr_token=acr_token,
        ) as builder:

            await builder.setup()
            await builder.build(click.echo)

    # Options:
    #     --add-host list           Add a custom host-to-IP mapping (host:ip)
    #     --build-arg list          Set build-time variables
    #     --cache-from strings      Images to consider as cache sources
    #     --disable-content-trust   Skip image verification (default true)
    # -f, --file string             Name of the Dockerfile (Default is 'PATH/Dockerfile')
    #     --iidfile string          Write the image ID to the file
    #     --isolation string        Container isolation technology
    #     --label list              Set metadata for an image
    #     --network string          Set the networking mode for the RUN instructions during build (default "default")
    #     --no-cache                Do not use cache when building the image
    # -o, --output stringArray      Output destination (format: type=local,dest=path)
    #     --platform string         Set platform if server is multi-platform capable
    #     --progress string         Set type of progress output (auto, plain, tty). Use plain to show container
    #                                 output (default "auto")
    #     --pull                    Always attempt to pull a newer version of the image
    # -q, --quiet                   Suppress the build output and print image ID on success
    #     --secret stringArray      Secret file to expose to the build (only if BuildKit enabled):
    #                                 id=mysecret,src=/local/secret
    #     --ssh stringArray         SSH agent socket or keys to expose to the build (only if BuildKit enabled)
    #                                 (format: default|<id>[=<socket>|<key>[,<key>]])
    # -t, --tag list                Name and optionally a tag in the 'name:tag' format
    #     --target string           Set the target build stage to build.
