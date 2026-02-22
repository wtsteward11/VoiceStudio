"""
Job Entity.

Task 2.3: Domain entity for job state persistence.
Represents lightweight job metadata (status, progress) persisted across restarts.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

from backend.domain.entities.base import AggregateRoot


class JobStatus(str, Enum):
    """Job status enumeration."""

    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"
    PAUSED = "paused"


@dataclass
class Job(AggregateRoot):
    """
    Job aggregate for persistent job state.

    Lightweight metadata for synthesis, cloning, batch, etc.
    """

    namespace: str = "default"
    job_type: str = "other"
    name: str = ""
    status: str = field(default=JobStatus.PENDING.value)
    progress: float = 0.0
    current_step: str | None = None
    total_steps: int | None = None
    error: str | None = None
    result_path: str | None = None
    result_id: str | None = None
    metadata: dict[str, Any] = field(default_factory=dict)
    started_at: datetime | None = None
    completed_at: datetime | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for persistence."""
        base = super().to_dict()
        base.update(
            {
                "namespace": self.namespace,
                "job_type": self.job_type,
                "name": self.name,
                "status": self.status,
                "progress": self.progress,
                "current_step": self.current_step,
                "total_steps": self.total_steps,
                "error": self.error,
                "result_path": self.result_path,
                "result_id": self.result_id,
                "metadata": self.metadata,
                "started_at": (self.started_at.isoformat() if self.started_at else None),
                "completed_at": (self.completed_at.isoformat() if self.completed_at else None),
            }
        )
        return base

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Job:
        """Create from dictionary."""
        started_at = None
        if data.get("started_at"):
            started_at = datetime.fromisoformat(data["started_at"])

        completed_at = None
        if data.get("completed_at"):
            completed_at = datetime.fromisoformat(data["completed_at"])

        metadata = data.get("metadata")
        if isinstance(metadata, str):
            import json

            try:
                metadata = json.loads(metadata) if metadata else {}
            except json.JSONDecodeError:
                metadata = {}
        elif metadata is None:
            metadata = {}

        return cls(
            id=data["id"],
            created_at=datetime.fromisoformat(data["created_at"]),
            updated_at=datetime.fromisoformat(data["updated_at"]),
            version=data.get("version", 0),
            namespace=data.get("namespace", "default"),
            job_type=data.get("job_type", "other"),
            name=data.get("name", ""),
            status=data.get("status", JobStatus.PENDING.value),
            progress=data.get("progress", 0.0),
            current_step=data.get("current_step"),
            total_steps=data.get("total_steps"),
            error=data.get("error"),
            result_path=data.get("result_path"),
            result_id=data.get("result_id"),
            metadata=metadata,
            started_at=started_at,
            completed_at=completed_at,
        )
