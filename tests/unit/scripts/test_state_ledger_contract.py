"""STATE ACTIVE WINDOW operator lane; HISTORY LEDGER milestone + proof index batch order."""

from __future__ import annotations

from pathlib import Path

# STATE.md uses Unicode en dash (U+2013) in task batch labels (e.g. **Tasks 71–77**).
_EN = "\u2013"

_REPO_ROOT = Path(__file__).resolve().parents[3]
_STATE = _REPO_ROOT / ".cursor" / "STATE.md"


def _active_window(text: str) -> str:
    start = text.index("## ACTIVE WINDOW")
    end = text.index("## HISTORY LEDGER", start)
    return text[start:end]


def _history_ledger(text: str) -> str:
    start = text.index("## HISTORY LEDGER")
    return text[start:]


def test_state_next_three_steps_operator_lane() -> None:
    """Slice 27 live seam vocabulary (Task 142+): plain language + link to slice27 README.

    Legacy governance task IDs (68/69/74/75/81/82) live in PROOF §27 **Task ID map** only.
    """
    text = _STATE.read_text(encoding="utf-8")
    window = _active_window(text)
    step = next(
        (ln for ln in window.splitlines() if ln.strip().startswith("- **Next 3 Steps:**")),
        "",
    )
    assert "dedicated" in step.lower(), "Next 3 Steps must mention dedicated backend / port"
    assert "checks.whisper_cpp" in step, "Next 3 Steps must mention checks.whisper_cpp"
    assert "slice27/README.md" in step, "Next 3 Steps must link slice27/README.md"
    assert "pytest -m real_whisper_cpp" in step, (
        "Next 3 Steps must mention pytest -m real_whisper_cpp"
    )
    assert "§8" in step, "Next 3 Steps must mention slice27 §8 for post-PASS mechanical flip"
    assert "mechanical flip" in step, "Next 3 Steps must describe §8 as mechanical flip"
    assert "only" in step and "PASS" in step, (
        "Next 3 Steps must tie §8 flip to runtime PASS only (no pre-PASS flip)"
    )


def test_state_latest_milestone_batch_order() -> None:
    """LATEST MILESTONE must list newest governance batches first (123–132, 114–122, … 71–77)."""
    text = _STATE.read_text(encoding="utf-8")
    ledger = _history_ledger(text)
    ms = ledger.index("### LATEST MILESTONE")
    proof = ledger.index("### LATEST PROOF INDEX")
    block = ledger[ms:proof]
    row_123 = block.find(f"**Tasks 123{_EN}132")
    row_114 = block.find(f"**Tasks 114{_EN}122")
    row_106 = block.find(f"**Tasks 106{_EN}113")
    row_98 = block.find(f"**Tasks 98{_EN}105")
    row_90 = block.find(f"**Tasks 84{_EN}90")
    row_78 = block.find(f"**Tasks 78{_EN}83")
    row_71 = block.find(f"**Tasks 71{_EN}77")
    assert row_123 != -1, "LATEST MILESTONE missing Tasks 123–132 entry"
    assert row_114 != -1, "LATEST MILESTONE missing Tasks 114–122 entry"
    assert row_106 != -1, "LATEST MILESTONE missing Tasks 106–113 entry"
    assert row_98 != -1, "LATEST MILESTONE missing Tasks 98–105 entry"
    assert row_90 != -1, "LATEST MILESTONE missing Tasks 84–90 entry"
    assert row_78 != -1, "LATEST MILESTONE missing Tasks 78–83 entry"
    assert row_71 != -1, "LATEST MILESTONE missing Tasks 71–77 entry"
    assert row_123 < row_114 < row_106 < row_98 < row_90 < row_78 < row_71, (
        "LATEST MILESTONE must list 123–132, 114–122, 106–113, 98–105, 84–90, 78–83, 71–77 (newest first)"
    )


def test_state_latest_proof_index_row_order() -> None:
    text = _STATE.read_text(encoding="utf-8")
    ledger = _history_ledger(text)
    idx = ledger.index("### LATEST PROOF INDEX")
    table = ledger[idx : idx + 120_000]
    # Batch labels use en dash (123–132, 114–122, 84–90, …). Some rows close bold after the batch id.
    row_123 = table.find(f"**Tasks 123{_EN}132")
    row_114 = table.find(f"**Tasks 114{_EN}122")
    row_90 = table.find(f"**Tasks 84{_EN}90")
    row_78 = table.find(f"**Tasks 78{_EN}83")
    row_71 = table.find(f"**Tasks 71{_EN}77")
    row_57 = table.find(f"**Tasks 57{_EN}63**")
    row_50 = table.find(f"**Tasks 50{_EN}56**")
    row_44 = table.find(f"**Tasks 44{_EN}49")
    assert row_123 != -1, "LATEST PROOF INDEX missing Tasks 123–132 row"
    assert row_114 != -1, "LATEST PROOF INDEX missing Tasks 114–122 row"
    assert row_90 != -1, "LATEST PROOF INDEX missing Tasks 84–90 row"
    assert row_78 != -1, "LATEST PROOF INDEX missing Tasks 78–83 row"
    assert row_71 != -1, "LATEST PROOF INDEX missing Tasks 71–77 row"
    assert row_57 != -1, "LATEST PROOF INDEX missing Tasks 57–63 row"
    assert row_50 != -1, "LATEST PROOF INDEX missing Tasks 50–56 row"
    assert row_44 != -1, "LATEST PROOF INDEX missing Tasks 44–49 row"
    assert row_123 < row_114 < row_90 < row_78 < row_71 < row_57 < row_50 < row_44, (
        "Proof index must list 123–132, then 114–122, then 84–90, then 78–83, then 71–77, "
        "then 57–63, then 50–56, then 44–49 (newest governance batches first)"
    )
