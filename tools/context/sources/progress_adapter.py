"""
Progress Source Adapter - Provides project progress tracking context.

Integrates with GateTracker, task completion tracking, and milestone status
to provide real-time progress information in context bundles.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

from tools.context.core.models import AllocationContext, SourceResult
from tools.context.sources.base import BaseSourceAdapter

logger = logging.getLogger(__name__)


@dataclass
class ProgressSummary:
    """Summary of project progress."""

    current_gate: str = ""
    current_phase: str = ""
    total_gates: int = 8
    completed_gates: int = 0
    blockers_count: int = 0
    in_progress_count: int = 0
    next_actions: list[str] = field(default_factory=list)
    gate_details: list[dict[str, Any]] = field(default_factory=list)
    milestones: list[dict[str, Any]] = field(default_factory=list)
    updated_at: str = ""

    def to_dict(self) -> dict[str, Any]:
        return {
            "current_gate": self.current_gate,
            "current_phase": self.current_phase,
            "total_gates": self.total_gates,
            "completed_gates": self.completed_gates,
            "progress_percent": round(self.completed_gates / max(1, self.total_gates) * 100, 1),
            "blockers_count": self.blockers_count,
            "in_progress_count": self.in_progress_count,
            "next_actions": self.next_actions,
            "gate_details": self.gate_details,
            "milestones": self.milestones,
            "updated_at": self.updated_at,
        }


class ProgressSourceAdapter(BaseSourceAdapter):
    """
    Fetch project progress information for context.

    Integrates with:
    - GateTracker: Gate completion status
    - LedgerParser: Issue/blocker counts
    - STATE.md: Current phase and milestones
    """

    def __init__(
        self,
        include_gate_details: bool = True,
        include_milestones: bool = True,
        max_actions: int = 5,
    ):
        super().__init__(source_name="progress", priority=60, offline=True)
        self._include_gate_details = include_gate_details
        self._include_milestones = include_milestones
        self._max_actions = max_actions

    def health_check(self) -> bool:
        """Check if progress sources are available."""
        try:
            # Check if ledger file exists
            from tools.overseer.ledger_parser import LEDGER_DEFAULT_PATH

            state_path = Path(".cursor/STATE.md")
            return LEDGER_DEFAULT_PATH.exists() or state_path.exists()
        except Exception:
            return False

    def fetch(self, context: AllocationContext) -> SourceResult:
        def _load() -> dict[str, Any]:
            summary = self._build_progress_summary()
            return {"progress": summary.to_dict()}

        return self._measure(_load, context)

    def _build_progress_summary(self) -> ProgressSummary:
        """Build complete progress summary."""
        summary = ProgressSummary(updated_at=datetime.now().isoformat())

        # Get gate status from GateTracker
        try:
            from tools.overseer.gate_tracker import GateTracker
            from tools.overseer.ledger_parser import LedgerParser

            parser = LedgerParser()
            tracker = GateTracker(parser)
            statuses = tracker.compute_statuses(force=True)

            # Determine current gate and progress
            summary.current_gate = tracker.get_current_gate().value
            summary.total_gates = len(statuses)
            summary.completed_gates = sum(1 for s in statuses if s.is_green)
            summary.blockers_count = sum(s.blocked_entries for s in statuses)
            summary.in_progress_count = sum(s.in_progress_entries for s in statuses)
            summary.next_actions = tracker.get_next_actions()[:self._max_actions]

            if self._include_gate_details:
                summary.gate_details = [
                    {
                        "gate": s.gate.value,
                        "total": s.total_entries,
                        "done": s.done_entries,
                        "blocked": s.blocked_entries,
                        "in_progress": s.in_progress_entries,
                        "is_green": s.is_green,
                    }
                    for s in statuses
                ]

        except Exception as e:
            logger.debug("GateTracker unavailable: %s", e)

        # Get phase from STATE.md
        try:
            state_path = Path(".cursor/STATE.md")
            if state_path.exists():
                text = state_path.read_text(encoding="utf-8")
                import re

                phase_match = re.search(r"\*\*Phase\*\*:\s*(.+)", text)
                if phase_match:
                    summary.current_phase = phase_match.group(1).strip()

                # Extract milestones if present
                if self._include_milestones:
                    summary.milestones = self._parse_milestones(text)

        except Exception as e:
            logger.debug("Failed to parse STATE.md: %s", e)

        return summary

    def _parse_milestones(self, text: str) -> list[dict[str, Any]]:
        """Parse milestones/proof index from STATE.md."""
        milestones = []

        # Look for Last Milestone section
        import re

        # Simple pattern for milestone entries
        milestone_pattern = re.compile(
            r"\*\*Last Milestone\*\*:\s*(.+)",
            re.IGNORECASE,
        )
        match = milestone_pattern.search(text)
        if match:
            milestones.append({
                "type": "last",
                "description": match.group(1).strip(),
            })

        # Look for completed items in Proof Index
        in_proof_index = False
        for line in text.splitlines():
            if "## Proof Index" in line:
                in_proof_index = True
                continue
            if in_proof_index and line.startswith("## "):
                break
            if in_proof_index and line.strip().startswith("|") and "---" not in line:
                parts = [p.strip() for p in line.split("|")[1:-1]]
                if len(parts) >= 3 and parts[0] != "Date":
                    milestones.append({
                        "type": "proof",
                        "date": parts[0],
                        "task": parts[1],
                        "artifact": parts[2] if len(parts) > 2 else "",
                    })

        return milestones[:10]  # Limit to 10 most recent

    def estimate_size(self, context: AllocationContext) -> int:
        return 2000


# Factory function for registry
def create_progress_adapter() -> ProgressSourceAdapter:
    """Create progress adapter with default configuration."""
    return ProgressSourceAdapter()
