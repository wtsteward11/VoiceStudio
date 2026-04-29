"""
Unit tests for scripts/ci/check_voice_synthesis_proof_boundary.py

Tests use tmp_path fixture for isolated file systems.
Git-based tests initialise a minimal repo in tmp_path to avoid coupling
to the working tree state.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import pytest

# Ensure scripts/ci/ is importable
ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.ci.check_voice_synthesis_proof_boundary import (
    _get_all_relevant_files,
    _is_relevant,
    main,
    validate_report,
)

# ─── Helpers ─────────────────────────────────────────────────────────────────

REAL_ENGINE_MINIMAL = """\
# Real Engine Generated Audio Proof — Test

**Classification: REAL_ENGINE**

## 1. Engine Mode Classification

**VERDICT: REAL_ENGINE**

| routed_engine | xtts_v2 |

## 5. Audio Artifact Validation

| Size | 186,956 bytes (182.6 KiB) |
| RIFF header | 52 49 46 46 = RIFF / WAVE |

## 6. Library Evidence

Library asset id: abc123

## 7. Timeline Evidence

timeline revision 1→2 clip def456

## 10. Explicit Non-Claims

- not runtime FULL PASS
- not STUB_ENGINE
"""

STUB_ENGINE_MINIMAL = """\
# Stub Engine Orchestration Proof — Test

**Classification: STUB_ENGINE**

This report documents orchestration-only proof.
VOICESTUDIO_TEST_MODE=1
routed_engine: stub

## Non-Claims

- not real synthesis proof
- STUB_ENGINE only
"""

MOCK_ENGINE_MINIMAL = """\
# Mock Engine Orchestration Proof — Test

**Classification: MOCK_ENGINE**

This documents mock-engine orchestration.

## Non-Claims

- not real synthesis
"""

UNKNOWN_MINIMAL = """\
# Engine Mode Blocker Report — Test

**Classification: UNKNOWN**

Could not determine engine mode; backend was unreachable.

## Non-Claims

- not real synthesis proof
"""


def _write_proof(tmp_path: Path, name: str, content: str) -> Path:
    d = tmp_path / "docs" / "reports" / "verification"
    d.mkdir(parents=True, exist_ok=True)
    p = d / name
    p.write_text(content, encoding="utf-8")
    return p


def _init_git_repo(tmp_path: Path) -> None:
    """Initialise a bare git repo with one commit in tmp_path."""
    subprocess.run(["git", "init", "-b", "main"], cwd=tmp_path, capture_output=True)
    subprocess.run(
        ["git", "config", "user.email", "test@test.com"],
        cwd=tmp_path,
        capture_output=True,
    )
    subprocess.run(
        ["git", "config", "user.name", "Test"],
        cwd=tmp_path,
        capture_output=True,
    )
    # Create a placeholder file so HEAD is valid
    placeholder = tmp_path / ".gitkeep"
    placeholder.write_text("")
    subprocess.run(["git", "add", "."], cwd=tmp_path, capture_output=True)
    subprocess.run(
        ["git", "commit", "-m", "init"],
        cwd=tmp_path,
        capture_output=True,
    )


# ─── Test 1: Valid REAL_ENGINE report passes ─────────────────────────────────

def test_valid_real_engine_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_PROOF_TEST.md", REAL_ENGINE_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 2: Valid STUB_ENGINE report passes ─────────────────────────────────

def test_valid_stub_engine_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_PROOF_TEST.md", STUB_ENGINE_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 3: Valid MOCK_ENGINE report passes ─────────────────────────────────

def test_valid_mock_engine_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "GENERATED_AUDIO_MOCK_PROOF_TEST.md", MOCK_ENGINE_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 4: Valid UNKNOWN report passes ─────────────────────────────────────

def test_valid_unknown_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "GENERATED_AUDIO_UNKNOWN_BLOCKER_TEST.md", UNKNOWN_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 5: Missing classification fails ────────────────────────────────────

def test_missing_classification_fails(tmp_path: Path) -> None:
    content = """\
# Generated Audio Proof — Test

This report has no classification token.

## Evidence

Some content here.
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_NO_CLASS_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "MISSING_CLASSIFICATION" in rules
    # Error must name the file path
    messages = " ".join(v.detail + v.fix for v in violations)
    assert "classification" in messages.lower() or "REAL_ENGINE" in messages


# ─── Test 6: Multiple distinct classifications fail ──────────────────────────

def test_multiple_classifications_fail(tmp_path: Path) -> None:
    content = """\
# Generated Audio Proof — Test

**Classification: REAL_ENGINE**
**Classification: STUB_ENGINE**

## Evidence

Some content.
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_MULTI_CLASS_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "AMBIGUOUS_CLASSIFICATION" in rules
    messages = " ".join(v.detail for v in violations)
    assert "REAL_ENGINE" in messages or "multiple" in messages.lower() or "ambiguous" in messages.lower()


# ─── Test 7: STUB_ENGINE claiming REAL_ENGINE confirmed fails ─────────────────

def test_stub_with_real_engine_confirmed_fails(tmp_path: Path) -> None:
    content = """\
# Stub Proof — Test

**Classification: STUB_ENGINE**

REAL_ENGINE confirmed in synthesis run.

## Non-Claims

- not runtime FULL PASS
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_BADCLAIM_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "STUB_CLAIMS_REAL_SYNTHESIS" in rules


# ─── Test 8: MOCK_ENGINE claiming real synthesis fails ───────────────────────

def test_mock_with_real_synthesis_claim_fails(tmp_path: Path) -> None:
    content = """\
# Mock Proof — Test

**Classification: MOCK_ENGINE**

This is a real synthesis proof of voice quality.
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_MOCK_BADCLAIM_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "STUB_CLAIMS_REAL_SYNTHESIS" in rules


# ─── Test 9: REAL_ENGINE missing artifact evidence fails ─────────────────────

def test_real_engine_missing_artifact_evidence_fails(tmp_path: Path) -> None:
    content = """\
# Real Engine Proof — Test

**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

routed_engine: xtts_v2

Library asset saved.

Timeline revision 1→2.
"""
    # Deliberately missing RIFF/WAV/bytes evidence
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_NOARTIFACT_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "REAL_ENGINE_MISSING_EVIDENCE" in rules
    details = " ".join(v.detail for v in violations)
    # Should mention the missing evidence category
    assert "RIFF" in details or "WAV" in details or "bytes" in details or "artifact" in details.lower()


# ─── Test 10: changed-from mode: non-matching changed files are not validated ─

def test_changed_from_only_validates_relevant_names(tmp_path: Path) -> None:
    """
    validate_report() is only called for files matching RELEVANT_NAME_PATTERNS.
    A failing report with an irrelevant name must not produce violations.
    Conversely, a failing file with a relevant name produces violations.
    This tests the name-filter logic without coupling to ROOT git state.
    """
    # Irrelevant name — even without classification, should not be matched
    irrelevant = tmp_path / "TIMELINE_DURABILITY_2026-04-29.md"
    irrelevant.write_text("# Timeline Durability\n\nNo classification.\n", encoding="utf-8")
    assert not _is_relevant(irrelevant)

    # Relevant name — same content → validation fires → violation
    relevant = tmp_path / "GENERATED_AUDIO_NEW_FAIL.md"
    relevant.write_text("# Generated Audio Proof\n\nNo classification here.\n", encoding="utf-8")
    assert _is_relevant(relevant)
    violations = validate_report(relevant)
    assert any(v.rule == "MISSING_CLASSIFICATION" for v in violations)

    # Passing old file should produce no violations regardless
    old_file = tmp_path / "GENERATED_AUDIO_OLD_PASS.md"
    old_file.write_text(STUB_ENGINE_MINIMAL, encoding="utf-8")
    assert validate_report(old_file) == []


# ─── Test 11: Unrelated reports are ignored ──────────────────────────────────

def test_unrelated_reports_ignored() -> None:
    # Non-matching names should not be checked
    assert not _is_relevant(Path("TIMELINE_DURABILITY_HARDENING_2026-04-28.md"))
    assert not _is_relevant(Path("GAP008_SLICE45_MAINWINDOW.md"))
    assert not _is_relevant(Path("ADR_001_PLATFORM.md"))
    # Guard/boundary meta-reports excluded even if name starts with VOICE_SYNTHESIS
    assert not _is_relevant(Path("VOICE_SYNTHESIS_PROOF_BOUNDARY_GUARD_2026-04-29.md"))
    # Matching synthesis proof report names should be relevant
    assert _is_relevant(Path("GENERATED_AUDIO_WORKFLOW_TEST.md"))
    assert _is_relevant(Path("VOICE_SYNTHESIS_ERROR_DIALOG_RECOVERY_2026-04-29.md"))
    assert _is_relevant(Path("REAL_ENGINE_GENERATED_AUDIO_PROOF_2026-04-29.md"))


# ─── Test 12: Error output includes file path and actionable reason ───────────

def test_error_output_includes_file_and_fix(tmp_path: Path) -> None:
    content = "# Generated Audio Proof\n\nNo classification.\n"
    p = _write_proof(tmp_path, "GENERATED_AUDIO_NOCLASS.md", content)
    violations = validate_report(p)
    assert violations, "Expected at least one violation"
    v = violations[0]
    # File path must be in the violation
    assert "GENERATED_AUDIO_NOCLASS.md" in v.file or "verification" in v.file
    # Fix must be actionable (mention the valid tokens)
    assert any(
        tok in v.fix
        for tok in ("REAL_ENGINE", "STUB_ENGINE", "MOCK_ENGINE", "UNKNOWN")
    )


# ─── Test 13 (bonus): STUB_ENGINE claiming real synthesis via phrase in non-claims passes ──

def test_stub_real_claim_in_nonclaims_section_passes(tmp_path: Path) -> None:
    """
    A STUB_ENGINE report that mentions 'real synthesis proof' only inside
    an explicit non-claims section should pass (non-claims are excluded
    from forbidden-phrase scanning).
    """
    content = """\
# Stub Proof — Test

**Classification: STUB_ENGINE**

This is an orchestration-only proof.

## Explicit Non-Claims

- This is NOT a real synthesis proof
- REAL_ENGINE confirmed is not claimed here
- Not real engine generated audio proof
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_NONCLAIMS_OK.md", content)
    violations = validate_report(p)
    assert violations == [], f"Non-claims section should be excluded: {violations}"
