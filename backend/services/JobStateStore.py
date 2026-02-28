"""
Disk-backed job state store for VoiceStudio backend.

Used to persist lightweight job metadata (status/progress) across backend restarts.
"""

from __future__ import annotations

import json
import logging
import os
import threading
from pathlib import Path
from typing import Any

from backend.config.path_config import get_path

logger = logging.getLogger(__name__)


class JobStateStore:
    """
    Simple job-state persistence using one JSON file per job.

    Layout:
      <jobs_root>/<namespace>/<job_id>.json
    """

    def __init__(self, namespace: str, jobs_dir: str | None = None):
        if not namespace or any(c in namespace for c in "\\/:"):
            raise ValueError(f"Invalid namespace: {namespace!r}")

        self.namespace = namespace
        self.jobs_root = self._resolve_jobs_root(jobs_dir)
        self.namespace_dir = self.jobs_root / namespace
        self._lock = threading.RLock()
        self.namespace_dir.mkdir(parents=True, exist_ok=True)

    @staticmethod
    def _resolve_jobs_root(jobs_dir: str | None) -> Path:
        if jobs_dir:
            return Path(jobs_dir)
        return get_path("jobs")

    def _job_path(self, job_id: str) -> Path:
        safe_id = job_id.replace(os.sep, "_")
        return self.namespace_dir / f"{safe_id}.json"

    def upsert(self, job_id: str, payload: dict[str, Any]) -> None:
        path = self._job_path(job_id)
        tmp = path.with_suffix(path.suffix + ".tmp")
        with self._lock:
            try:
                data = json.dumps(payload, indent=2, ensure_ascii=False)
                tmp.write_text(data, encoding="utf-8")
                os.replace(tmp, path)
            except Exception as e:
                # ALLOWED: Log and continue - job state persistence is best-effort
                logger.warning(f"Failed to persist job state {job_id}: {e}")
                try:
                    if tmp.exists():
                        tmp.unlink()
                except OSError as cleanup_err:
                    logger.debug(f"Failed to cleanup temp file {tmp}: {cleanup_err}")

    def get(self, job_id: str) -> dict[str, Any] | None:
        """Get job state by ID. Returns None if not found or corrupted."""
        path = self._job_path(job_id)
        with self._lock:
            if not path.exists():
                return None
            try:
                return json.loads(path.read_text(encoding="utf-8"))
            except (json.JSONDecodeError, OSError) as e:
                # Job file is corrupted or unreadable - treat as missing
                logger.debug(f"Failed to read job state {job_id}: {e}")
                return None

    def delete(self, job_id: str) -> None:
        path = self._job_path(job_id)
        with self._lock:
            try:
                if path.exists():
                    path.unlink()
            except OSError as delete_err:
                logger.debug(f"Failed to delete job state file {path}: {delete_err}")

    def load_all(self, limit: int = 5000) -> dict[str, dict[str, Any]]:
        with self._lock:
            results: dict[str, dict[str, Any]] = {}
            if not self.namespace_dir.exists():
                return results

            for p in list(self.namespace_dir.glob("*.json"))[:limit]:
                try:
                    payload = json.loads(p.read_text(encoding="utf-8"))
                    job_id = p.stem
                    results[job_id] = payload
                except (json.JSONDecodeError, OSError):
                    # Skip corrupted or unreadable job files - continue loading others
                    continue

            return results


_stores: dict[str, JobStateStore] = {}
_stores_lock = threading.RLock()


def get_job_state_store(namespace: str, jobs_dir: str | None = None) -> JobStateStore:
    key = f"{namespace}::{jobs_dir or ''}"
    with _stores_lock:
        store = _stores.get(key)
        if store is None:
            store = JobStateStore(namespace=namespace, jobs_dir=jobs_dir)
            _stores[key] = store
        return store
