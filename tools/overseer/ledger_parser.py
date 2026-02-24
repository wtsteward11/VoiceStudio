from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from tools.overseer.models import Category, Gate, LedgerEntry, LedgerState, Severity

LEDGER_DEFAULT_PATH = Path("Recovery Plan/QUALITY_LEDGER.md")
RESERVED_IDS = {"VS-0025", "VS-0032"}


@dataclass
class LedgerSummary:
    total_entries: int
    done_entries: int
    blocked_entries: int
    in_progress_entries: int
    open_entries: int

    @property
    def completion_percent(self) -> float:
        if self.total_entries == 0:
            return 100.0
        return (self.done_entries / self.total_entries) * 100.0


@dataclass
class ValidationResult:
    valid: bool
    errors: list[str]
    warnings: list[str]


class LedgerParser:
    """Parse the Quality Ledger open index into structured entries."""

    def __init__(self, ledger_path: Path | None = None):
        self.ledger_path = ledger_path or LEDGER_DEFAULT_PATH
        self._entries: list[LedgerEntry] = []
        self._last_parsed_at: datetime | None = None

    def parse(self, force: bool = False) -> list[LedgerEntry]:
        if self._entries and not force:
            return list(self._entries)
        if not self.ledger_path.exists():
            self._entries = []
            return []
        text = self.ledger_path.read_text(encoding="utf-8")
        self._entries = self._parse_open_index(text)
        self._last_parsed_at = datetime.utcnow()
        return list(self._entries)

    def get_summary(self) -> LedgerSummary:
        entries = self.parse()
        done = sum(1 for e in entries if e.state == LedgerState.DONE or e.state == LedgerState.WONT_FIX)
        blocked = sum(1 for e in entries if e.state == LedgerState.BLOCKED)
        in_progress = sum(1 for e in entries if e.state == LedgerState.IN_PROGRESS or e.state == LedgerState.FIXED_PENDING_PROOF or e.state == LedgerState.TRIAGE)
        open_count = sum(1 for e in entries if e.state == LedgerState.OPEN)
        return LedgerSummary(
            total_entries=len(entries),
            done_entries=done,
            blocked_entries=blocked,
            in_progress_entries=in_progress,
            open_entries=open_count,
        )

    def validate(self) -> ValidationResult:
        errors: list[str] = []
        warnings: list[str] = []
        if not self.ledger_path.exists():
            errors.append(f"Ledger file not found: {self.ledger_path}")
            return ValidationResult(valid=False, errors=errors, warnings=warnings)

        text = self.ledger_path.read_text(encoding="utf-8")
        entries = self._parse_open_index(text, errors=errors, warnings=warnings)
        ids = {e.id for e in entries}
        present_in_text = {rid for rid in RESERVED_IDS if rid in text}
        for reserved in sorted(RESERVED_IDS):
            if reserved not in ids and reserved not in present_in_text:
                warnings.append(f"Reserved ID not present in open index: {reserved}")
        return ValidationResult(valid=len(errors) == 0, errors=errors, warnings=warnings)

    def _parse_open_index(
        self,
        text: str,
        errors: list[str] | None = None,
        warnings: list[str] | None = None,
    ) -> list[LedgerEntry]:
        errors = errors if errors is not None else []
        warnings = warnings if warnings is not None else []
        lines = text.splitlines()
        entries: list[LedgerEntry] = []
        in_index = False
        for line in lines:
            if re.match(r"^\s*##\s+Open index", line, re.IGNORECASE):
                in_index = True
                continue
            if in_index:
                if line.strip().startswith("## "):
                    break
                if not line.strip().startswith("|"):
                    continue
                if "---" in line:
                    continue
                parts = [p.strip() for p in line.split("|")[1:-1]]
                if len(parts) < 7:
                    continue
                entry_id, state_raw, sev_raw, gate_raw, owner, category_raw, title = parts[:7]
                if not entry_id or entry_id.lower() == "id":
                    continue
                if state_raw.upper() == "N/A" or sev_raw.upper() == "N/A" or gate_raw.upper() == "N/A":
                    warnings.append(f"Reserved ID {entry_id} has N/A fields in open index")
                    continue
                gate = Gate.from_string(gate_raw)
                if gate is None:
                    errors.append(f"{entry_id}: invalid gate '{gate_raw}'")
                    continue
                try:
                    state = LedgerState[state_raw.strip().upper()]
                except KeyError:
                    errors.append(f"{entry_id}: invalid state '{state_raw}'")
                    continue
                severity = Severity.from_string(sev_raw)
                categories: list[Category] = []
                for raw in category_raw.split(","):
                    raw = raw.strip()
                    if not raw:
                        continue
                    cat = Category.from_string(raw)
                    if cat is None:
                        warnings.append(f"{entry_id}: unknown category '{raw}'")
                        continue
                    categories.append(cat)
                if not categories:
                    warnings.append(f"{entry_id}: no valid categories")
                entry = LedgerEntry(
                    id=entry_id,
                    state=state,
                    severity=severity,
                    gate=gate,
                    owner_role=owner,
                    categories=categories,
                    title=title,
                    raw_text=line,
                )
                entries.append(entry)
        return entries
