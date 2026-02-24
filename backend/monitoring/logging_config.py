"""
Phase 8: Structured Logging System
Task 8.4: Structured logging for observability.
"""

from __future__ import annotations

import json
import logging
import sys
import traceback
from collections.abc import MutableMapping
from dataclasses import dataclass
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any


class LogLevel(Enum):
    """Log levels."""

    DEBUG = "DEBUG"
    INFO = "INFO"
    WARNING = "WARNING"
    ERROR = "ERROR"
    CRITICAL = "CRITICAL"


@dataclass
class LogConfig:
    """Logging configuration."""

    level: LogLevel = LogLevel.INFO
    console_enabled: bool = True
    file_enabled: bool = True
    file_path: Path | None = None
    json_format: bool = True
    max_file_size_mb: int = 10
    backup_count: int = 5
    include_stacktrace: bool = True


class StructuredFormatter(logging.Formatter):
    """Formatter that outputs structured JSON logs."""

    def __init__(self, include_stacktrace: bool = True):
        super().__init__()
        self.include_stacktrace = include_stacktrace

    def format(self, record: logging.LogRecord) -> str:
        """Format the log record as JSON."""
        log_entry = {
            "timestamp": datetime.utcnow().isoformat() + "Z",
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
            "module": record.module,
            "function": record.funcName,
            "line": record.lineno,
        }

        # Add extra fields
        if hasattr(record, "extra"):
            log_entry["extra"] = record.extra

        # Add exception info
        if record.exc_info and self.include_stacktrace:
            log_entry["exception"] = {
                "type": record.exc_info[0].__name__ if record.exc_info[0] else None,
                "message": str(record.exc_info[1]) if record.exc_info[1] else None,
                "stacktrace": traceback.format_exception(*record.exc_info),
            }

        # Add context fields
        for key in ["request_id", "user_id", "session_id", "operation"]:
            if hasattr(record, key):
                log_entry[key] = getattr(record, key)

        return json.dumps(log_entry)


class ConsoleFormatter(logging.Formatter):
    """Human-readable formatter for console output."""

    COLORS = {
        "DEBUG": "\033[36m",  # Cyan
        "INFO": "\033[32m",  # Green
        "WARNING": "\033[33m",  # Yellow
        "ERROR": "\033[31m",  # Red
        "CRITICAL": "\033[35m",  # Magenta
    }
    RESET = "\033[0m"

    def format(self, record: logging.LogRecord) -> str:
        """Format the log record for console."""
        color = self.COLORS.get(record.levelname, "")
        reset = self.RESET

        timestamp = datetime.now().strftime("%H:%M:%S")

        message = (
            f"{color}[{timestamp}] {record.levelname:8}{reset} {record.name}: {record.getMessage()}"
        )

        if record.exc_info:
            message += "\n" + "".join(traceback.format_exception(*record.exc_info))

        return message


class LoggerAdapter(logging.LoggerAdapter):
    """Logger adapter with context support."""

    def process(
        self, msg: str, kwargs: MutableMapping[str, Any]
    ) -> tuple[str, MutableMapping[str, Any]]:
        """Process the message and kwargs."""
        extra = kwargs.get("extra", {})
        extra.update(self.extra)
        kwargs["extra"] = extra
        return msg, kwargs


class ContextLogger:
    """Logger with context binding support."""

    def __init__(self, name: str, context: dict[str, Any] | None = None):
        self._logger = logging.getLogger(name)
        self._context = context or {}

    def bind(self, **kwargs: Any) -> ContextLogger:
        """Create a new logger with additional context."""
        new_context = {**self._context, **kwargs}
        return ContextLogger(self._logger.name, new_context)

    def _log(self, level: int, msg: str, *args: Any, **kwargs: Any) -> None:
        """Log a message with context."""
        extra = kwargs.pop("extra", {})
        extra.update(self._context)
        kwargs["extra"] = extra
        self._logger.log(level, msg, *args, **kwargs)

    def debug(self, msg: str, *args: Any, **kwargs: Any) -> None:
        """Log debug message."""
        self._log(logging.DEBUG, msg, *args, **kwargs)

    def info(self, msg: str, *args: Any, **kwargs: Any) -> None:
        """Log info message."""
        self._log(logging.INFO, msg, *args, **kwargs)

    def warning(self, msg: str, *args: Any, **kwargs: Any) -> None:
        """Log warning message."""
        self._log(logging.WARNING, msg, *args, **kwargs)

    def error(self, msg: str, *args: Any, **kwargs: Any) -> None:
        """Log error message."""
        self._log(logging.ERROR, msg, *args, **kwargs)

    def critical(self, msg: str, *args: Any, **kwargs: Any) -> None:
        """Log critical message."""
        self._log(logging.CRITICAL, msg, *args, **kwargs)

    def exception(self, msg: str, *args: Any, **kwargs: Any) -> None:
        """Log exception with traceback."""
        kwargs["exc_info"] = True
        self._log(logging.ERROR, msg, *args, **kwargs)


def configure_logging(config: LogConfig | None = None) -> None:
    """Configure the logging system."""
    config = config or LogConfig()

    # Get root logger
    root_logger = logging.getLogger()
    root_logger.setLevel(getattr(logging, config.level.value))

    # Remove existing handlers
    root_logger.handlers.clear()

    # Console handler
    if config.console_enabled:
        console_handler = logging.StreamHandler(sys.stdout)
        console_handler.setFormatter(ConsoleFormatter())
        root_logger.addHandler(console_handler)

    # File handler
    if config.file_enabled and config.file_path:
        from logging.handlers import RotatingFileHandler

        config.file_path.parent.mkdir(parents=True, exist_ok=True)

        formatter: logging.Formatter
        if config.json_format:
            formatter = StructuredFormatter(config.include_stacktrace)
        else:
            formatter = logging.Formatter("%(asctime)s - %(name)s - %(levelname)s - %(message)s")

        file_handler = RotatingFileHandler(
            config.file_path,
            maxBytes=config.max_file_size_mb * 1024 * 1024,
            backupCount=config.backup_count,
        )
        file_handler.setFormatter(formatter)
        root_logger.addHandler(file_handler)


def get_logger(name: str, **context: Any) -> ContextLogger:
    """Get a context logger."""
    return ContextLogger(name, context)


# Default configuration
def setup_default_logging() -> None:
    """Set up default logging configuration."""
    configure_logging(
        LogConfig(
            level=LogLevel.INFO,
            console_enabled=True,
            file_enabled=True,
            file_path=Path.home() / ".voicestudio" / "logs" / "voicestudio.log",
            json_format=True,
        )
    )


def setup_buildlogs_logging() -> None:
    """
    Phase 7 Sprint 2: Add log rotation for .buildlogs/ structured logs.

    Adds a RotatingFileHandler for .buildlogs/voicestudio.json with:
    - 10MB max file size
    - 5 backup files
    - JSON format
    """
    from logging.handlers import RotatingFileHandler

    buildlogs_dir = Path(".buildlogs")
    buildlogs_dir.mkdir(parents=True, exist_ok=True)
    log_path = buildlogs_dir / "voicestudio.json"

    handler = RotatingFileHandler(
        log_path,
        maxBytes=10 * 1024 * 1024,
        backupCount=5,
    )
    handler.setFormatter(StructuredFormatter(include_stacktrace=True))
    logging.getLogger().addHandler(handler)
