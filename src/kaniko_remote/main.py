from pathlib import Path

import asyncclick as click

from kaniko_remote.builder import Builder
from kaniko_remote.config.config import Config
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
    "dockerfile",
    help='Name of the Dockerfile (Default is "PATH/Dockerfile")',
    type=click.Path(),
    default="Dockerfile",
)
@click.option(
    "-t",
    "--tag",
    "destination",
    required=True,
    help='Name and tag in the "name:tag" format',
    type=str,
)
@click.argument(
    "path",
    type=click.Path(exists=True),
)
async def build(path: Path, **kwargs):
    """
    Build an image from a Dockerfile on a k8s cluster using kaniko

    This tool can be explicitly invoked as 'kaniko-remote'.
    If optionally installed, this tool can additionally be invoked as 'docker'.
    """
    config = Config()
    with K8sWrapper(
        kubeconfig=config.get_kubeconfig(),
        namespace=config.get_namespace(),
    ) as k8s:
        with Builder(
            k8s_wrapper=k8s,
            config=config,
            context=path,
            **kwargs,
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
