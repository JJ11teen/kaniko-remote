from pathlib import Path
from time import time

import asyncclick as click

from kaniko_remote.builder import Builder
from kaniko_remote.config import Config
from kaniko_remote.k8s.k8s import K8sWrapper
from kaniko_remote.logging import getLogger, init_logging

logger = getLogger(__name__)


@click.group()
@click.option(
    "-v",
    "--verbosity",
    type=click.Choice(
        ["trace", "debug", "info", "warn", "error", "fatal", "panic"],
        case_sensitive=False,
    ),
    default="warn",
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


@cli.command(context_settings=dict(ignore_unknown_options=True, allow_extra_args=True))
async def push(**kwargs):
    logger.warning("This command is a NOOP as kaniko-remote pushes the image at the end of the build step.")


@cli.command(context_settings=dict(allow_interspersed_args=True))
@click.option(
    "-t",
    "--tag",
    "destinations",
    required=True,
    multiple=True,
    help='Name and tag in the "name:tag" format',
    type=str,
)
@click.option(
    "-f",
    "--file",
    "dockerfile",
    help='Path to the dockerfile within context (default "./Dockerfile")',
    type=str,
)
@click.option("--build-arg", "build_args", help="Set build-time ARG variables", type=str, multiple=True)
@click.option("--label", "labels", help="Set metadata for an image", type=str, multiple=True)
@click.option("--target", help="Set the target build stage to build.", type=str)
@click.option("--platform", "customPlatform", help="Set platform if server is multi-platform capable", type=str)
@click.option("-q", "--quiet", help="Suppress the build output and print image ID on success", type=bool)
@click.option("--iidfile", help="Write the image ID to the file", type=str)
@click.argument("path", type=click.Path(exists=True, dir_okay=True, file_okay=False))
async def build(path: Path, quiet: bool, iidfile: str, **kaniko_args):
    """
    Build an image from a Dockerfile on a k8s cluster using kaniko

    This tool can be explicitly invoked as 'kaniko-remote'.
    If optionally installed, this tool can additionally be invoked as 'docker'.
    """
    kaniko_args["context"] = path
    config = Config()

    logger.warning(f"Remotely building image on remote k8s cluster (Using config: {config.config_location})")

    pre_tag = config.get_tag_prepend()
    kaniko_args["destinations"] = tuple([f"{pre_tag}/{d}" for d in kaniko_args["destinations"]])

    with K8sWrapper(**config.get_kubernetes_options()) as k8s:
        with Builder(
            k8s_wrapper=k8s,
            config=config,
            **kaniko_args,
        ) as builder:
            logger.warning(f"Initialised builder {k8s.namespace}/{builder.pod_name}")

            start_time = time()
            await builder.setup()
            setup_time = time() - start_time
            logger.warning(f"Setup builder {builder.pod_name} in {setup_time:.2f} seconds, streaming logs:")

            start_time = time()
            image_sha = await builder.build(logger.warning)  # click.echo
            setup_time = time() - start_time
    logger.warning(f"Built image with digest {image_sha} in {setup_time:.2f} seconds")
    logger.warning(f"Note that this newly built image has not been pulled to this machine")
