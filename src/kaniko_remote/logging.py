import logging
from asyncio.log import logger
from functools import partial
from logging import Logger as DefaultLogger
from logging import getLogger as getDefaultLogger
from typing import Union

TRACE = 5
_log_format = "[KANIKO-REMOTE] %(message)s"

logging.addLevelName(5, "TRACE")


# This class isn't actually instantied, just used for type hinting
class Logger(DefaultLogger):
    def trace(self, msg, *args, **kwargs):
        pass


def getLogger(name: str) -> Logger:
    logger = getDefaultLogger(name=name)
    logger.trace = partial(logger.log, TRACE)
    return logger


logger = getLogger(__name__)


def init_logging(level: Union[str, int]):
    if isinstance(level, str):
        level_str = level.upper()
        level = logging.getLevelName(level_str)
        if "Level" in level_str:
            raise ValueError(f"Unknown logging level '{level_str}'")
        logging.basicConfig(level=level, format=_log_format)
        logger.debug(f"Logging initialised to level '{level_str}'")
    else:
        logging.basicConfig(level=level, format=_log_format)
        logger.debug(f"Logging initialised to level '{level}'")
