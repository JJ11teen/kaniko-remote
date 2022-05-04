from typing import Iterator

from kaniko_remote.logging import getLogger

logger = getLogger(__name__)


class Tagger:
    def __init__(
        self,
        default: str,
        static: str,
        prefix: str,
    ) -> None:
        self._default = default
        self._static = static
        self._prefix = prefix

        if self._static and self._prefix:
            raise ValueError(f"Only one of 'static' or 'prefix' can be configured")

    def adjust_tags(self, tags: Iterator[str]) -> Iterator[str]:
        if self._static:
            logger.warning(f"Overwriting with static tag: {self._static}")
            return [self._static]

        tags = list(tags)

        if len(tags) == 0:
            logger.warning(f"Using configured default tag: {self._default}")
            return [self._default]

        if self._prefix:
            pre = [f"{self._prefix}/{t}" for t in tags]
            logger.warning(f"Overwriting with Prefixed tags: {pre}")
            return pre

        return tags
