"""Truth-session batch dates match STATE Latest verify artifact (Tasks 58, 66).

Parses ``artifacts/verify/YYYYMMDD_HHMMSS/`` from ACTIVE WINDOW. For each
registered batch label, every ACTIVE WINDOW line mentioning that label must
include the verify-derived calendar date. PROOF §27 sections anchored by
``## Tasks …`` must carry the same **As of** date in their first ``| **As of** |``
row. Task 46 PROOF rows stay historical (not tied to current verify bar).

Adding a new batch: append ``(label, "## Heading")`` to ``_VERIFY_DATE_BATCHES``.
"""

from __future__ import annotations

import re
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[3]
_STATE = _REPO_ROOT / ".cursor" / "STATE.md"
_PROOF27 = (
    _REPO_ROOT
    / "docs"
    / "reports"
    / "verification"
    / "PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md"
)

# (ACTIVE WINDOW substring, PROOF §27 markdown heading anchor)
_VERIFY_DATE_BATCHES: tuple[tuple[str, str], ...] = (
    ("Tasks 50–56", "## Tasks 50–56"),
    ("Tasks 57–63", "## Tasks 57–63"),
)


def _active_window(text: str) -> str:
    start = text.index("## ACTIVE WINDOW")
    end = text.index("## HISTORY LEDGER", start)
    return text[start:end]


def _state_latest_verify_artifact(state_text: str) -> str:
    window = _active_window(state_text)
    m = re.search(
        r"\*\*Latest verify artifact:\*\* \[`(artifacts/verify/[^`]+\.md)`",
        window,
    )
    assert m is not None, "STATE ACTIVE WINDOW missing Latest verify artifact"
    return m.group(1)


def _calendar_date_from_verify_path(verify_rel: str) -> str:
    m = re.search(r"artifacts/verify/(\d{8})_\d+/", verify_rel)
    assert m is not None, f"bad verify path: {verify_rel!r}"
    ymd = m.group(1)
    return f"{ymd[:4]}-{ymd[4:6]}-{ymd[6:8]}"


def _proof_first_as_of_date_after_anchor(proof_text: str, heading: str) -> str:
    idx = proof_text.index(heading)
    end = idx + 4000
    chunk = proof_text[idx:end]
    m = re.search(r"\|\s*\*\*As of\*\*\s*\|\s*(\d{4}-\d{2}-\d{2})", chunk)
    assert m is not None, f"PROOF §27 missing **As of** row after {heading!r}"
    return m.group(1)


def test_truth_session_batch_dates_align_with_verify_bar() -> None:
    state_text = _STATE.read_text(encoding="utf-8")
    verify_path = _state_latest_verify_artifact(state_text)
    expected = _calendar_date_from_verify_path(verify_path)
    window = _active_window(state_text)
    proof_text = _PROOF27.read_text(encoding="utf-8")

    for batch_label, proof_heading in _VERIFY_DATE_BATCHES:
        for line in window.splitlines():
            if batch_label not in line:
                continue
            assert expected in line, (
                f"STATE ACTIVE WINDOW must include {expected} on lines with "
                f"{batch_label!r}:\n{line!r}\n(verify: {verify_path})"
            )

        got = _proof_first_as_of_date_after_anchor(proof_text, proof_heading)
        assert got == expected, (
            f"PROOF {proof_heading} first As-of {got!r} != verify-derived "
            f"{expected!r} ({verify_path})"
        )
