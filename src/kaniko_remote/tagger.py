import re
from collections import OrderedDict
from typing import Iterator, Optional

from kaniko_remote.logging import getLogger

logger = getLogger(__name__)


class Tagger:
    def __init__(
        self,
        default: Optional[str] = None,
        static: Optional[str] = None,
        prefix: Optional[str] = None,
        regexes: OrderedDict = OrderedDict(),
    ) -> None:
        self._default = default
        self._static = static
        self._prefix = prefix
        self._regex_by_template = OrderedDict({t: re.compile(r) for r, t in regexes})
        self._has_regex = len(self._regex_by_template) > 0

        if self._static and (self._prefix or self._default or self._has_regex):
            raise ValueError(f"No other tag options may be configured when 'static' is set")

    def adjust_tags(self, tags: Iterator[str]) -> Iterator[str]:
        if self._static:
            logger.warning(f"Overwriting with static tag: {self._static}")
            return [self._static]

        tags = list(tags)

        if len(tags) == 0:
            if not self._default:
                raise ValueError("No tag specified and no default tag configured")
            logger.warning(f"Using default tag: {self._default}")
            return [self._default]

        # Short circuit so avoid unneccessary logs
        if not self._has_regex and not self._prefix:
            return tags

        edited_tags = []
        for tag in tags:
            regex_matches = OrderedDict({t: r.match(tag) for t, r in self._regex_by_template})
            regex_matches = OrderedDict({t: m for t, m in regex_matches if m is not None})
            if len(regex_matches) > 0:
                templ, match = next(regex_matches.items())
                edited_tags.append(match.expand(templ))
            elif self._prefix:
                edited_tags.append(f"{self._prefix}/{tag}")

        logger.warning(f"Adjusted tags to: {edited_tags}")
        return edited_tags
