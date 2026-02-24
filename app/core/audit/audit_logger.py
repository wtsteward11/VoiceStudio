"""
AuditLogger Service for VoiceStudio.

Central audit logging service that integrates with existing StructuredLogger
and provides comprehensive audit trail functionality.

Features:
- File change logging with role/task attribution
- Build event logging with warning/error codes
- Runtime exception logging with correlation
- XAML failure logging
- Crash artifact cross-referencing
- Automatic context enrichment
"""

from __future__ import annotations

import json
import logging
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import TYPE_CHECKING, Any, Protocol

logger = logging.getLogger(__name__)

if TYPE_CHECKING:
    from .issue_bridge import AuditIssueBridge


class ContextEnricher(Protocol):
    def enrich(self, entry: AuditEntry) -> AuditEntry: ...


from .schema import (
    AuditActor,
    AuditEntry,
    AuditEventType,
    create_build_event_entry,
    create_exception_entry,
    create_file_change_entry,
    create_xaml_failure_entry,
)


class AuditLogger:
    """
    Central audit logging service integrated with existing monitoring.

    Logs audit entries to:
    - Daily JSONL files (.audit/log-YYYY-MM-DD.jsonl)
    - Daily Markdown summaries (.audit/log-YYYY-MM-DD.md)
    - Index file for quick lookups (.audit/index.json)

    Thread-safe for concurrent logging from multiple sources.

    Integrates with:
    - ContextEnricher for automatic context resolution
    - AuditIssueBridge for automatic issue creation from errors
    """

    def __init__(
        self,
        audit_dir: Path | None = None,
        enable_markdown: bool = True,
        enable_console: bool = False,
        enable_issue_bridge: bool = True,
    ):
        """
        Initialize AuditLogger.

        Args:
            audit_dir: Directory for audit logs (default: .audit/)
            enable_markdown: Generate Markdown summaries
            enable_console: Echo audit entries to console
            enable_issue_bridge: Enable automatic issue creation for errors
        """
        self._audit_dir = audit_dir or Path(".audit")
        self._enable_markdown = enable_markdown
        self._enable_console = enable_console
        self._enable_issue_bridge = enable_issue_bridge
        self._lock = threading.Lock()
        self._context_enricher: ContextEnricher | None = None
        self._issue_bridge: AuditIssueBridge | None = None

        # Ensure audit directory exists
        self._audit_dir.mkdir(parents=True, exist_ok=True)
        (self._audit_dir / "files").mkdir(exist_ok=True)
        (self._audit_dir / "tasks").mkdir(exist_ok=True)
        (self._audit_dir / "diffs").mkdir(exist_ok=True)

        # Initialize issue bridge if enabled
        if self._enable_issue_bridge:
            self._init_issue_bridge()

    def _init_issue_bridge(self):
        """Initialize the issue bridge for automatic issue creation."""
        try:
            from .issue_bridge import get_audit_issue_bridge

            self._issue_bridge = get_audit_issue_bridge()
        except ImportError:
            self._issue_bridge = None

    def set_context_enricher(self, enricher: ContextEnricher):
        """Set the context enricher for automatic context resolution."""
        self._context_enricher = enricher

    def set_issue_bridge(self, bridge: AuditIssueBridge):
        """Set the issue bridge for automatic issue creation."""
        self._issue_bridge = bridge

    def _get_today_log_path(self) -> Path:
        """Get path to today's log file."""
        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        return self._audit_dir / f"log-{today}.jsonl"

    def _get_today_markdown_path(self) -> Path:
        """Get path to today's Markdown summary."""
        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        return self._audit_dir / f"log-{today}.md"

    def _enrich_entry(self, entry: AuditEntry) -> AuditEntry:
        """Enrich entry with context if enricher is available."""
        if self._context_enricher:
            return self._context_enricher.enrich(entry)
        return entry

    def _write_entry(self, entry: AuditEntry):
        """Write entry to log files (thread-safe)."""
        with self._lock:
            # Write to daily JSONL log
            log_path = self._get_today_log_path()
            with open(log_path, "a", encoding="utf-8") as f:
                f.write(entry.to_json() + "\n")

            # Write to Markdown summary
            if self._enable_markdown:
                md_path = self._get_today_markdown_path()
                self._append_markdown(md_path, entry)

            # Write to per-file log if applicable
            if entry.file_path:
                self._append_to_file_log(entry)

            # Write to per-task log if applicable
            if entry.task_id:
                self._append_to_task_log(entry)

            # Update index
            self._update_index(entry)

            # Console output
            if self._enable_console:
                print(f"[AUDIT] {entry.event_type}: {entry.summary}")

        # Notify issue bridge (outside lock to avoid blocking)
        if self._issue_bridge:
            try:
                self._issue_bridge.on_audit_entry(entry)
            except Exception as e:
                # Don't let issue bridge failures break audit logging
                logger.debug(f"Issue bridge notification failed: {e}")

    def _append_markdown(self, md_path: Path, entry: AuditEntry):
        """Append entry to Markdown summary."""
        if not md_path.exists():
            # Create header
            today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
            header = f"""# Audit Log - {today}

| Timestamp | Event | Role | Task | File | Summary |
|-----------|-------|------|------|------|---------|
"""
            with open(md_path, "w", encoding="utf-8") as f:
                f.write(header)

        with open(md_path, "a", encoding="utf-8") as f:
            f.write(entry.to_markdown() + "\n")

    def _append_to_file_log(self, entry: AuditEntry):
        """Append entry to per-file log."""
        if not entry.file_path:
            return

        # Sanitize file path for log filename
        safe_name = entry.file_path.replace("/", "_").replace("\\", "_")
        if len(safe_name) > 100:
            safe_name = safe_name[-100:]

        file_log_path = self._audit_dir / "files" / f"{safe_name}.log"
        with open(file_log_path, "a", encoding="utf-8") as f:
            f.write(entry.to_json() + "\n")

    def _append_to_task_log(self, entry: AuditEntry):
        """Append entry to per-task log."""
        if not entry.task_id:
            return

        task_log_path = self._audit_dir / "tasks" / f"{entry.task_id}.json"

        # Load existing entries or create new list
        entries = []
        if task_log_path.exists():
            try:
                with open(task_log_path, encoding="utf-8") as f:
                    entries = json.load(f)
            except json.JSONDecodeError as e:
                # GAP-PY-001: Corrupted task log, start fresh
                logger.debug(f"Failed to parse task log {task_log_path}: {e}")
                entries = []

        entries.append(entry.to_dict())

        with open(task_log_path, "w", encoding="utf-8") as f:
            json.dump(entries, f, indent=2, default=str)

    def _update_index(self, entry: AuditEntry):
        """Update the quick lookup index."""
        index_path = self._audit_dir / "index.json"

        # Load existing index or create new
        index: dict[str, Any] = {"last_updated": "", "files": {}, "tasks": {}, "recent_entries": []}
        if index_path.exists():
            try:
                with open(index_path, encoding="utf-8") as f:
                    index = json.load(f)
            except json.JSONDecodeError as e:
                # GAP-PY-001: Corrupted index file, use fresh index
                logger.debug(f"Failed to parse audit index {index_path}: {e}")

        index["last_updated"] = datetime.now(timezone.utc).isoformat()

        # Update file index
        if entry.file_path:
            if entry.file_path not in index["files"]:
                index["files"][entry.file_path] = {"entry_count": 0}
            index["files"][entry.file_path]["last_modified"] = entry.timestamp
            index["files"][entry.file_path]["last_task"] = entry.task_id
            index["files"][entry.file_path]["entry_count"] += 1

        # Update task index
        if entry.task_id:
            if entry.task_id not in index["tasks"]:
                index["tasks"][entry.task_id] = {
                    "files_changed": 0,
                    "errors_fixed": 0,
                }
            index["tasks"][entry.task_id]["last_activity"] = entry.timestamp
            if entry.file_path:
                index["tasks"][entry.task_id]["files_changed"] += 1
            if entry.event_type in [
                AuditEventType.BUILD_ERROR.value,
                AuditEventType.RUNTIME_EXCEPTION.value,
            ]:
                index["tasks"][entry.task_id]["errors_fixed"] += 1

        # Keep last 50 recent entries
        recent: list[Any] = index.get("recent_entries", [])
        recent.insert(0, {"id": entry.entry_id, "type": entry.event_type, "time": entry.timestamp})
        index["recent_entries"] = recent[:50]

        with open(index_path, "w", encoding="utf-8") as f:
            json.dump(index, f, indent=2, default=str)

    # ========== Public Logging Methods ==========

    def log_file_change(
        self,
        file_path: str,
        operation: str,
        role: str,
        task_id: str,
        summary: str,
        lines_added: int = 0,
        lines_removed: int = 0,
        actor: str = AuditActor.AI_AGENT.value,
    ) -> str:
        """
        Log a file modification with full context.

        Args:
            file_path: Relative path of the changed file
            operation: Operation type (create, modify, delete, rename)
            role: Role performing the change (e.g., "Role 2 - Core Engineer")
            task_id: Task ID from Quality Ledger (e.g., "VS-0018")
            summary: Human-readable description of the change
            lines_added: Number of lines added
            lines_removed: Number of lines removed
            actor: Actor type (human, ai-agent, system)

        Returns:
            Entry ID for cross-referencing
        """
        entry = create_file_change_entry(
            file_path=file_path,
            operation=operation,
            role=role,
            task_id=task_id,
            summary=summary,
            lines_added=lines_added,
            lines_removed=lines_removed,
            actor=actor,
        )
        entry = self._enrich_entry(entry)
        self._write_entry(entry)
        return entry.entry_id

    def log_build_event(
        self,
        warnings: list[str],
        errors: list[str],
        commit_hash: str | None = None,
        task_id: str | None = None,
    ) -> list[str]:
        """
        Log build results with warning/error codes.

        Args:
            warnings: List of warning codes (e.g., ["CS8618", "RCS1163"])
            errors: List of error codes
            commit_hash: Git commit hash
            task_id: Task ID from Quality Ledger

        Returns:
            List of entry IDs
        """
        entry_ids = []

        # Log each warning
        for warning in warnings:
            entry = create_build_event_entry(
                event_type=AuditEventType.BUILD_WARNING.value,
                error_code=warning,
                commit_hash=commit_hash,
                task_id=task_id,
            )
            entry = self._enrich_entry(entry)
            self._write_entry(entry)
            entry_ids.append(entry.entry_id)

        # Log each error
        for error in errors:
            entry = create_build_event_entry(
                event_type=AuditEventType.BUILD_ERROR.value,
                error_code=error,
                commit_hash=commit_hash,
                task_id=task_id,
            )
            entry = self._enrich_entry(entry)
            self._write_entry(entry)
            entry_ids.append(entry.entry_id)

        # Log success if no errors
        if not errors:
            entry = AuditEntry(
                event_type=AuditEventType.BUILD_SUCCESS.value,
                commit_hash=commit_hash,
                task_id=task_id,
                summary=f"Build succeeded with {len(warnings)} warnings",
                actor=AuditActor.SYSTEM.value,
            )
            entry = self._enrich_entry(entry)
            self._write_entry(entry)
            entry_ids.append(entry.entry_id)

        return entry_ids

    def log_runtime_exception(
        self,
        exception: Exception,
        context: dict[str, Any],
    ) -> str:
        """
        Log runtime exception with correlation.

        Args:
            exception: The exception that occurred
            context: Context dictionary with keys like:
                - request_id: Request correlation ID
                - path: Request path
                - task_id: Task ID
                - subsystem: Subsystem name

        Returns:
            Entry ID
        """
        entry = create_exception_entry(
            exception=exception,
            subsystem=context.get("subsystem", "unknown"),
            correlation_id=context.get("request_id") or context.get("correlation_id"),
            task_id=context.get("task_id"),
            extra_context=context,
        )
        entry = self._enrich_entry(entry)
        self._write_entry(entry)
        return entry.entry_id

    def log_xaml_failure(
        self,
        file_path: str,
        error_type: str,
        message: str,
        commit_hash: str | None = None,
    ) -> str:
        """
        Log XAML compiler or binding failure.

        Args:
            file_path: Path to the XAML file
            error_type: Type of error (compile, binding)
            message: Error message
            commit_hash: Git commit hash

        Returns:
            Entry ID
        """
        entry = create_xaml_failure_entry(
            file_path=file_path,
            error_type=error_type,
            message=message,
            commit_hash=commit_hash,
        )
        entry = self._enrich_entry(entry)
        self._write_entry(entry)
        return entry.entry_id

    def link_crash_artifact(self, entry_id: str, crash_path: str):
        """
        Cross-reference crash artifact with log entry.

        Args:
            entry_id: ID of the entry to link
            crash_path: Path to the crash artifact
        """
        # Find and update the entry
        log_path = self._get_today_log_path()
        if not log_path.exists():
            return

        with self._lock:
            entries = []
            with open(log_path, encoding="utf-8") as f:
                for line in f:
                    try:
                        entry = AuditEntry.from_json(line.strip())
                        if entry.entry_id == entry_id:
                            entry.linked_artifacts.append(crash_path)
                        entries.append(entry)
                    except json.JSONDecodeError as e:
                        # GAP-PY-001: Skip malformed audit log line
                        logger.debug(f"Failed to parse audit entry: {e}")
                        continue

            # Rewrite log with updated entry
            with open(log_path, "w", encoding="utf-8") as f:
                for entry in entries:
                    f.write(entry.to_json() + "\n")

    def log_gate_proof(
        self,
        gate: str,
        status: str,
        artifacts: list[str],
        task_id: str | None = None,
    ) -> str:
        """
        Log gate validation result.

        Args:
            gate: Gate identifier (A-H)
            status: PASS or FAIL
            artifacts: List of proof artifact paths
            task_id: Task ID from Quality Ledger

        Returns:
            Entry ID
        """
        entry = AuditEntry(
            event_type=AuditEventType.GATE_PROOF.value,
            gate=gate,
            task_id=task_id,
            linked_artifacts=artifacts,
            severity="info" if status == "PASS" else "error",
            summary=f"Gate {gate}: {status}",
            actor=AuditActor.SYSTEM.value,
            extra={"status": status},
        )
        entry = self._enrich_entry(entry)
        self._write_entry(entry)
        return entry.entry_id

    def log_compatibility_drift(
        self,
        file_path: str,
        expected: str,
        actual: str,
        component: str,
    ) -> str:
        """
        Log compatibility drift detection.

        Args:
            file_path: Path to the file with drift
            expected: Expected value from spec
            actual: Actual value found
            component: Component name (e.g., "torch", "cuda")

        Returns:
            Entry ID
        """
        entry = AuditEntry(
            event_type=AuditEventType.COMPATIBILITY_DRIFT.value,
            file_path=file_path,
            severity="warning",
            summary=f"Drift: {component} expected {expected}, found {actual}",
            actor=AuditActor.SYSTEM.value,
            extra={
                "component": component,
                "expected": expected,
                "actual": actual,
            },
        )
        entry = self._enrich_entry(entry)
        self._write_entry(entry)
        return entry.entry_id

    def get_recent_entries(self, limit: int = 100) -> list[AuditEntry]:
        """
        Get recent audit entries.

        Args:
            limit: Maximum number of entries to return

        Returns:
            List of recent AuditEntry objects
        """
        log_path = self._get_today_log_path()
        entries = []

        if log_path.exists():
            with open(log_path, encoding="utf-8") as f:
                lines = f.readlines()
                for line in reversed(lines[-limit:]):
                    try:
                        entries.append(AuditEntry.from_json(line.strip()))
                    except json.JSONDecodeError as e:
                        # GAP-PY-001: Skip malformed audit log line
                        logger.debug(f"Failed to parse recent audit entry: {e}")
                        continue

        return entries


# Global audit logger instance
_audit_logger: AuditLogger | None = None


def get_audit_logger() -> AuditLogger:
    """
    Get or create global audit logger.

    Returns:
        AuditLogger instance
    """
    global _audit_logger
    if _audit_logger is None:
        _audit_logger = AuditLogger()
    return _audit_logger


def setup_audit_logger(
    audit_dir: Path | None = None,
    enable_markdown: bool = True,
    enable_console: bool = False,
) -> AuditLogger:
    """
    Setup global audit logger with custom configuration.

    Args:
        audit_dir: Directory for audit logs
        enable_markdown: Generate Markdown summaries
        enable_console: Echo audit entries to console

    Returns:
        Configured AuditLogger instance
    """
    global _audit_logger
    _audit_logger = AuditLogger(
        audit_dir=audit_dir,
        enable_markdown=enable_markdown,
        enable_console=enable_console,
    )
    return _audit_logger
