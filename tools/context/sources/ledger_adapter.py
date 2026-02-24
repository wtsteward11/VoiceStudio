from __future__ import annotations

from pathlib import Path
from typing import Any

from tools.context.core.models import AllocationContext, SourceResult
from tools.context.sources.base import BaseSourceAdapter
from tools.overseer.ledger_parser import LedgerParser

DEFAULT_LEDGER_PATH = Path("Recovery Plan/QUALITY_LEDGER.md")


class LedgerSourceAdapter(BaseSourceAdapter):
    """Load Quality Ledger entries for context bundles."""

    def __init__(self, ledger_path: Path = DEFAULT_LEDGER_PATH, include_done: bool = False, offline: bool = True):
        super().__init__(source_name="ledger", priority=70, offline=offline)
        self._ledger_path = ledger_path
        self._include_done = include_done

    def fetch(self, context: AllocationContext) -> SourceResult:
        def _load() -> dict[str, Any]:
            path = self._ledger_path
            if not path.exists():
                return {"ledger": []}
            parser = LedgerParser(path)
            entries = parser.parse()
            out: list[dict[str, Any]] = []
            for entry in entries:
                if not self._include_done and getattr(entry, "state", None) and entry.state.value == "DONE":
                    continue
                out.append({
                    "id": entry.id,
                    "state": entry.state.value if entry.state else None,
                    "severity": entry.severity.value if entry.severity else None,
                    "gate": entry.gate.value if entry.gate else None,
                    "owner_role": entry.owner_role,
                    "categories": [c.value for c in entry.categories],
                    "title": entry.title,
                })
            return {"ledger": out}

        return self._measure(_load, context)

    def estimate_size(self, context: AllocationContext) -> int:
        return 2000
