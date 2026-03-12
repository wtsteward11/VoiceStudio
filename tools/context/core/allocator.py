from __future__ import annotations

import json
from typing import Any

from tools.context.core.models import (
    BudgetConstraints,
    ContextBundle,
    ContextLevel,
    GitContext,
    MemoryItem,
    ProgressContext,
    RuleContext,
    SourceResult,
    StateContext,
    TaskContext,
)
from tools.context.core.protocols import AllocatorProtocol

# Map sources to context levels for progressive disclosure
SOURCE_LEVEL_MAP = {
    # Level 1 (HIGH): Always loaded
    "state": ContextLevel.HIGH,
    "task": ContextLevel.HIGH,
    "file_context": ContextLevel.HIGH,  # Language reminders critical for cross-stack work
    "progress": ContextLevel.HIGH,  # Progress tracking is critical context

    # Level 2 (MID): Loaded if budget allows
    "brief": ContextLevel.MID,
    "ledger": ContextLevel.MID,
    "issues": ContextLevel.MID,

    # Level 3 (LOW): Loaded if budget allows
    "rules": ContextLevel.LOW,
    "memory": ContextLevel.LOW,
    "git": ContextLevel.LOW,
    "telemetry": ContextLevel.LOW,
    "gitkraken": ContextLevel.LOW,
}


def _truncate_text(text: str | None, limit: int) -> str | None:
    if text is None or limit <= 0:
        return text
    if len(text) <= limit:
        return text
    return text[: max(0, limit - 3)] + "..."


def _string_size(data) -> int:
    try:
        return len(json.dumps(data, ensure_ascii=False))
    except Exception:
        return len(str(data))


class ContextAllocator(AllocatorProtocol):
    """Priority-aware allocator with budget enforcement and progressive disclosure."""

    def allocate(
        self,
        sources: list[SourceResult],
        budget: BudgetConstraints,
        max_level: ContextLevel = ContextLevel.LOW,
    ) -> ContextBundle:
        """
        Allocate context bundle with progressive disclosure.

        Args:
            sources: Source results to allocate from
            budget: Budget constraints
            max_level: Maximum context level to load (HIGH, MID, or LOW)

        Returns:
            Context bundle with sources filtered by level
        """
        # Filter sources by max_level (progressive disclosure)
        filtered = [
            s for s in sources
            if self._should_include_source(s.source_name, max_level)
        ]

        # Sort by source priority order if provided, else keep input order.
        priority_order = budget.priority_order or []
        order_map = {name: idx for idx, name in enumerate(priority_order)}
        ordered = sorted(
            filtered,
            key=lambda s: order_map.get(s.source_name, len(order_map)),
        )

        bundle = ContextBundle()
        bundle.meta["max_level"] = max_level.value
        for result in ordered:
            if not result.success:
                continue
            data = result.data
            if not isinstance(data, dict):
                continue
            if "task" in data and isinstance(data["task"], TaskContext) and not bundle.task.id:
                bundle.task = data["task"]
            if "state" in data and isinstance(data["state"], StateContext):
                bundle.state = data["state"]
            if "brief" in data and data["brief"] and bundle.brief is None:
                bundle.brief = data["brief"]
            if "rules" in data and isinstance(data["rules"], list) and not bundle.rules:
                bundle.rules = data["rules"]
            if "memory" in data and isinstance(data["memory"], list):
                bundle.memory = (bundle.memory or []) + data["memory"]
            if "git" in data and isinstance(data["git"], GitContext) and bundle.git is None:
                bundle.git = data["git"]
            if "ledger" in data and isinstance(data.get("ledger"), list) and bundle.ledger is None:
                bundle.ledger = data["ledger"]
                if "ledger_context" in data and isinstance(data["ledger_context"], str):
                    bundle.meta["ledger_context"] = data["ledger_context"]
            if "telemetry" in data and bundle.telemetry is None:
                bundle.telemetry = {
                    "telemetry": data.get("telemetry", []),
                    "slo_summary": data.get("slo_summary", ""),
                }
            if "proof_index" in data and isinstance(data.get("proof_index"), list) and bundle.proof_index is None:
                bundle.proof_index = data["proof_index"]
            if "progress" in data and bundle.progress is None:
                prog_data = data["progress"]
                if isinstance(prog_data, dict):
                    bundle.progress = ProgressContext(
                        current_gate=prog_data.get("current_gate", ""),
                        current_phase=prog_data.get("current_phase", ""),
                        progress_percent=prog_data.get("progress_percent", 0.0),
                        blockers_count=prog_data.get("blockers_count", 0),
                        in_progress_count=prog_data.get("in_progress_count", 0),
                        next_actions=prog_data.get("next_actions", []),
                        gate_details=prog_data.get("gate_details", []),
                    )
            # File context (language detection and reminders)
            if "file_context" in data and "language_reminder" not in bundle.meta:
                file_ctx = data.get("file_context")
                lang_reminder = data.get("language_reminder", "")
                if file_ctx:
                    bundle.meta["file_context"] = {
                        "primary_language": getattr(file_ctx, "primary_language", "unknown"),
                        "stack": getattr(file_ctx, "stack", "unknown"),
                        "files_detected": getattr(file_ctx, "files_detected", [])[:5],
                    }
                if lang_reminder:
                    bundle.meta["language_reminder"] = lang_reminder

        self._apply_budget(bundle, budget)
        return bundle.with_meta(budget_chars=budget.total_chars)

    def _should_include_source(self, source_name: str, max_level: ContextLevel) -> bool:
        """Determine if source should be included based on level."""
        source_level = SOURCE_LEVEL_MAP.get(source_name, ContextLevel.LOW)

        if max_level == ContextLevel.HIGH:
            return source_level == ContextLevel.HIGH
        elif max_level == ContextLevel.MID:
            return source_level in (ContextLevel.HIGH, ContextLevel.MID)
        else:  # LOW
            return True  # Include all levels

    def _apply_budget(self, bundle: ContextBundle, budget: BudgetConstraints) -> None:
        # Task
        task_limit = budget.limit_for("task")
        bundle.task.title = _truncate_text(bundle.task.title, task_limit)
        bundle.task.blockers = _truncate_text(bundle.task.blockers, task_limit)

        # State
        state_limit = budget.limit_for("state")
        bundle.state.context = _truncate_text(bundle.state.context, state_limit)

        # Brief
        if bundle.brief:
            brief_limit = budget.limit_for("brief")
            bundle.brief.objective = _truncate_text(bundle.brief.objective, brief_limit)
            bundle.brief.acceptance = _truncate_text(bundle.brief.acceptance, brief_limit)
            bundle.brief.proofs = _truncate_text(bundle.brief.proofs, brief_limit)

        # Rules
        if bundle.rules:
            rules_limit = budget.limit_for("rules")
            remaining = rules_limit
            truncated: list[RuleContext] = []
            for rule in bundle.rules:
                desc = rule.description or ""
                if not desc:
                    truncated.append(rule)
                    continue
                if len(desc) <= remaining:
                    truncated.append(rule)
                    remaining -= len(desc)
                else:
                    rule.description = _truncate_text(desc, remaining)
                    truncated.append(rule)
                    break
            bundle.rules = truncated

        # Memory (GAP-012: token-based sliding window — keep most recent items)
        if bundle.memory:
            mem_limit = budget.limit_for("memory")
            # Sliding window: process from end (most recent) so we keep recent, drop older
            reversed_items = list(reversed(bundle.memory))
            acc: list[MemoryItem] = []
            remaining_chars = mem_limit
            for item in reversed_items:
                if remaining_chars <= 0:
                    break
                content_len = len(item.content)
                if content_len <= remaining_chars:
                    acc.append(item)
                    remaining_chars -= content_len
                else:
                    # Truncate this item to fit; it's the oldest we're keeping
                    acc.append(
                        MemoryItem(
                            content=_truncate_text(item.content, remaining_chars) or "",
                            source=item.source,
                        )
                    )
                    break
            # Restore chronological order (oldest of kept first)
            bundle.memory = list(reversed(acc))

        # Git
        if bundle.git:
            git_limit = budget.limit_for("git")
            bundle.git.status = _truncate_text(bundle.git.status, git_limit)
            bundle.git.shortlog = _truncate_text(bundle.git.shortlog, git_limit)

        # Ledger
        if bundle.ledger:
            ledger_limit = budget.limit_for("ledger")
            acc_ledger: list[dict[str, Any]] = []
            remaining = ledger_limit
            for entry in bundle.ledger:
                try:
                    sz = _string_size(entry)
                    if sz <= remaining:
                        acc_ledger.append(entry)
                        remaining -= sz
                    else:
                        break
                except Exception:
                    acc_ledger.append(entry)
                    remaining -= 200
                    if remaining <= 0:
                        break
            bundle.ledger = acc_ledger
            if "ledger_context" in bundle.meta and isinstance(bundle.meta["ledger_context"], str):
                bundle.meta["ledger_context"] = _truncate_text(bundle.meta["ledger_context"], ledger_limit)

        # Telemetry
        if bundle.telemetry and isinstance(bundle.telemetry, dict):
            telemetry_limit = budget.limit_for("telemetry")
            summary = bundle.telemetry.get("slo_summary")
            if isinstance(summary, str) and len(summary) > telemetry_limit:
                bundle.telemetry["slo_summary"] = _truncate_text(summary, telemetry_limit)
