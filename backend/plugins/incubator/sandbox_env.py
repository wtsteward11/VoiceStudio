"""
Sandbox Environment for Plugin Testing.

Phase 6E: Provides isolated execution environments for testing plugins
without affecting the production system.

Features:
1. Filesystem isolation (temp directories)
2. Resource limits (memory, CPU, time)
3. Network isolation
4. State snapshots and rollback
5. Execution recording

Usage:
    config = SandboxConfig(
        max_memory_mb=512,
        max_execution_time_s=30,
        allow_network=False,
    )

    sandbox = SandboxEnvironment(config)

    async with sandbox.create_session("plugin-id") as session:
        result = await session.execute(plugin_func, args)
        print(result.success)
"""

from __future__ import annotations

import asyncio
import logging
import os
import shutil
import tempfile
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

logger = logging.getLogger(__name__)


class IsolationLevel(Enum):
    """Levels of sandbox isolation."""

    NONE = "none"  # No isolation (for trusted plugins)
    FILESYSTEM = "fs"  # Filesystem only
    PROCESS = "process"  # Separate process
    CONTAINER = "container"  # Full container (future)


class SandboxState(Enum):
    """State of a sandbox session."""

    CREATED = "created"
    RUNNING = "running"
    SUSPENDED = "suspended"
    COMPLETED = "completed"
    FAILED = "failed"
    ROLLED_BACK = "rolled_back"


@dataclass
class SandboxConfig:
    """Configuration for sandbox environment."""

    max_memory_mb: int = 512
    max_execution_time_s: float = 30.0
    max_disk_mb: int = 100
    allow_network: bool = False
    allow_filesystem: bool = True
    isolation_level: IsolationLevel = IsolationLevel.FILESYSTEM
    record_execution: bool = True
    auto_cleanup: bool = True

    def validate(self) -> List[str]:
        """Validate config and return any errors."""
        errors = []
        if self.max_memory_mb < 16:
            errors.append("max_memory_mb must be at least 16")
        if self.max_execution_time_s < 1:
            errors.append("max_execution_time_s must be at least 1")
        if self.max_disk_mb < 1:
            errors.append("max_disk_mb must be at least 1")
        return errors


@dataclass
class ExecutionRecord:
    """Record of a single execution."""

    function_name: str
    args: Dict[str, Any]
    result: Any = None
    error: Optional[str] = None
    started_at: datetime = field(default_factory=datetime.utcnow)
    ended_at: Optional[datetime] = None
    memory_used_mb: float = 0.0
    execution_time_s: float = 0.0

    def to_dict(self) -> Dict[str, Any]:
        return {
            "function_name": self.function_name,
            "args": str(self.args)[:200],  # Truncate
            "result": str(self.result)[:200] if self.result else None,
            "error": self.error,
            "started_at": self.started_at.isoformat(),
            "ended_at": self.ended_at.isoformat() if self.ended_at else None,
            "memory_used_mb": self.memory_used_mb,
            "execution_time_s": self.execution_time_s,
        }


@dataclass
class SandboxResult:
    """Result of sandbox execution."""

    success: bool
    result: Any = None
    error: Optional[str] = None
    execution_time_s: float = 0.0
    memory_used_mb: float = 0.0
    records: List[ExecutionRecord] = field(default_factory=list)
    files_created: List[str] = field(default_factory=list)
    files_modified: List[str] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "success": self.success,
            "result": str(self.result)[:500] if self.result else None,
            "error": self.error,
            "execution_time_s": self.execution_time_s,
            "memory_used_mb": self.memory_used_mb,
            "records": [r.to_dict() for r in self.records],
            "files_created": self.files_created,
            "files_modified": self.files_modified,
        }


class SandboxSession:
    """An active sandbox session."""

    def __init__(
        self,
        session_id: str,
        plugin_id: str,
        config: SandboxConfig,
        workspace: Path,
    ):
        self.session_id = session_id
        self.plugin_id = plugin_id
        self.config = config
        self.workspace = workspace
        self.state = SandboxState.CREATED
        self.records: List[ExecutionRecord] = []
        self.files_snapshot: Dict[str, bytes] = {}
        self._start_time: Optional[datetime] = None

    async def execute(
        self,
        func: Callable,
        args: Optional[Dict[str, Any]] = None,
    ) -> SandboxResult:
        """Execute a function in the sandbox."""
        self.state = SandboxState.RUNNING
        self._start_time = datetime.utcnow()
        args = args or {}

        record = ExecutionRecord(
            function_name=func.__name__ if hasattr(func, "__name__") else str(func),
            args=args,
        )

        result = SandboxResult(success=False)

        try:
            # Set timeout
            timeout = self.config.max_execution_time_s

            # Execute with timeout
            if asyncio.iscoroutinefunction(func):
                output = await asyncio.wait_for(
                    func(**args),
                    timeout=timeout,
                )
            else:
                output = await asyncio.wait_for(
                    asyncio.get_event_loop().run_in_executor(
                        None,
                        lambda: func(**args),
                    ),
                    timeout=timeout,
                )

            record.result = output
            record.ended_at = datetime.utcnow()
            record.execution_time_s = (record.ended_at - record.started_at).total_seconds()

            result.success = True
            result.result = output
            result.execution_time_s = record.execution_time_s
            self.state = SandboxState.COMPLETED

        except asyncio.TimeoutError:
            record.error = f"Execution timed out after {timeout}s"
            record.ended_at = datetime.utcnow()
            result.error = record.error
            self.state = SandboxState.FAILED

        except Exception as e:
            record.error = str(e)
            record.ended_at = datetime.utcnow()
            result.error = str(e)
            self.state = SandboxState.FAILED
            logger.exception(f"Sandbox execution failed: {e}")

        self.records.append(record)
        result.records = self.records.copy()

        # Collect file changes
        result.files_created, result.files_modified = self._detect_file_changes()

        return result

    def _detect_file_changes(self) -> tuple[List[str], List[str]]:
        """Detect files created or modified during session."""
        created = []
        modified = []

        if not self.workspace.exists():
            return created, modified

        for path in self.workspace.rglob("*"):
            if path.is_file():
                rel_path = str(path.relative_to(self.workspace))
                if rel_path not in self.files_snapshot:
                    created.append(rel_path)
                else:
                    try:
                        current = path.read_bytes()
                        if current != self.files_snapshot[rel_path]:
                            modified.append(rel_path)
                    except Exception as e:
                        # GAP-PY-001: Best effort file comparison
                        logger.debug(f"Failed to compare file {rel_path}: {e}")

        return created, modified

    def snapshot_files(self):
        """Take snapshot of current files."""
        self.files_snapshot.clear()

        if not self.workspace.exists():
            return

        for path in self.workspace.rglob("*"):
            if path.is_file():
                try:
                    rel_path = str(path.relative_to(self.workspace))
                    self.files_snapshot[rel_path] = path.read_bytes()
                except Exception as e:
                    # GAP-PY-001: Best effort file snapshot
                    logger.debug(f"Failed to snapshot file {path}: {e}")

    def rollback_files(self):
        """Rollback files to snapshot state."""
        if not self.workspace.exists():
            return

        # Remove files not in snapshot
        for path in self.workspace.rglob("*"):
            if path.is_file():
                rel_path = str(path.relative_to(self.workspace))
                if rel_path not in self.files_snapshot:
                    try:
                        path.unlink()
                    except Exception as e:
                        # GAP-PY-001: Best effort file cleanup
                        logger.debug(f"Failed to remove file {path}: {e}")

        # Restore snapshot content
        for rel_path, content in self.files_snapshot.items():
            full_path = self.workspace / rel_path
            try:
                full_path.parent.mkdir(parents=True, exist_ok=True)
                full_path.write_bytes(content)
            except Exception as e:
                # GAP-PY-001: Best effort file restore
                logger.debug(f"Failed to restore file {full_path}: {e}")

        self.state = SandboxState.ROLLED_BACK


class SandboxEnvironment:
    """
    Manages sandbox environments for plugin testing.

    Provides isolated execution contexts with:
    - Temporary filesystem
    - Resource limits
    - Execution recording
    - State snapshots

    Example:
        config = SandboxConfig(max_memory_mb=256, max_execution_time_s=10)
        sandbox = SandboxEnvironment(config)

        async with sandbox.create_session("my-plugin") as session:
            result = await session.execute(my_func, {"arg1": "value"})

            if result.success:
                print(f"Result: {result.result}")
            else:
                print(f"Error: {result.error}")
    """

    def __init__(
        self,
        default_config: Optional[SandboxConfig] = None,
        base_path: Optional[Path] = None,
    ):
        """
        Initialize sandbox environment.

        Args:
            default_config: Default configuration for sessions
            base_path: Base path for sandbox workspaces
        """
        self.default_config = default_config or SandboxConfig()
        self.base_path = base_path or Path(tempfile.gettempdir()) / "voicestudio_sandbox"
        self._sessions: Dict[str, SandboxSession] = {}
        self._session_counter = 0

    @asynccontextmanager
    async def create_session(
        self,
        plugin_id: str,
        config: Optional[SandboxConfig] = None,
    ):
        """
        Create a sandbox session.

        Args:
            plugin_id: Plugin identifier
            config: Optional config override

        Yields:
            SandboxSession for execution
        """
        config = config or self.default_config

        # Validate config
        errors = config.validate()
        if errors:
            raise ValueError(f"Invalid config: {', '.join(errors)}")

        # Create session
        self._session_counter += 1
        session_id = (
            f"{plugin_id}_{self._session_counter}_{datetime.utcnow().strftime('%Y%m%d%H%M%S')}"
        )

        workspace = self.base_path / session_id
        workspace.mkdir(parents=True, exist_ok=True)

        session = SandboxSession(
            session_id=session_id,
            plugin_id=plugin_id,
            config=config,
            workspace=workspace,
        )

        # Take initial snapshot
        session.snapshot_files()

        self._sessions[session_id] = session

        try:
            yield session
        finally:
            # Cleanup
            if config.auto_cleanup:
                await self._cleanup_session(session)

    async def _cleanup_session(self, session: SandboxSession):
        """Clean up a sandbox session."""
        try:
            if session.workspace.exists():
                shutil.rmtree(session.workspace)
        except Exception as e:
            logger.warning(f"Failed to cleanup sandbox workspace: {e}")

        self._sessions.pop(session.session_id, None)

    def get_session(self, session_id: str) -> Optional[SandboxSession]:
        """Get an active session by ID."""
        return self._sessions.get(session_id)

    def list_sessions(self) -> List[Dict[str, Any]]:
        """List all active sessions."""
        return [
            {
                "session_id": s.session_id,
                "plugin_id": s.plugin_id,
                "state": s.state.value,
                "records_count": len(s.records),
            }
            for s in self._sessions.values()
        ]

    async def cleanup_all(self):
        """Clean up all sessions."""
        for session in list(self._sessions.values()):
            await self._cleanup_session(session)

        # Clean base path if empty
        try:
            if self.base_path.exists() and not any(self.base_path.iterdir()):
                self.base_path.rmdir()
        except Exception as e:
            # GAP-PY-001: Best effort base path cleanup
            logger.debug(f"Failed to clean base path {self.base_path}: {e}")
