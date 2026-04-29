"""
Unit tests for scripts/ci/check_voice_synthesis_proof_boundary.py

Tests use tmp_path fixture for isolated file systems.
Git-based tests initialise a minimal repo in tmp_path to avoid coupling
to the working tree state.
"""
from __future__ import annotations

import io
import json
import subprocess
import sys
from pathlib import Path
from unittest.mock import patch

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

# ─── Fixtures (with metadata blocks and non-claims sections) ──────────────────

REAL_ENGINE_MINIMAL = """\
# Real Engine Generated Audio Proof — Test

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

**Classification: REAL_ENGINE**

## 1. Engine Mode Classification

**VERDICT: REAL_ENGINE**

| routed_engine | xtts_v2 |

## 5. Audio Artifact Validation

| Size | 186,956 bytes (182.6 KiB) |
| RIFF header | 52 49 46 46 = RIFF / WAVE |
| Body | binary audio — not a JSON error body; does not start with `{` |

## 6. Library Evidence

HTTP 201 library asset; audio_id abc123

## 7. Timeline Evidence

timeline revision 1→2; clip_id def456; POST /api/timeline/tracks

## 10. Explicit Non-Claims

- not runtime FULL PASS
- not STUB_ENGINE
"""

STUB_ENGINE_MINIMAL = """\
# Stub Engine Orchestration Proof — Test

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

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

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: MOCK_ENGINE
proof_type: generated_audio
engine_mode_source: mock_fixture
runtime_claim: false
operator_claim: false
-->

**Classification: MOCK_ENGINE**

This documents mock-engine orchestration.

## Non-Claims

- not real synthesis
"""

UNKNOWN_MINIMAL = """\
# Engine Mode Blocker Report — Test

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: UNKNOWN
proof_type: voice_synthesis
engine_mode_source: blocked_unknown
runtime_claim: false
operator_claim: false
-->

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


# ─── Test 1: Valid REAL_ENGINE report passes ──────────────────────────────────

def test_valid_real_engine_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_PROOF_TEST.md", REAL_ENGINE_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 2: Valid STUB_ENGINE report passes ──────────────────────────────────

def test_valid_stub_engine_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_PROOF_TEST.md", STUB_ENGINE_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 3: Valid MOCK_ENGINE report passes ──────────────────────────────────

def test_valid_mock_engine_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "GENERATED_AUDIO_MOCK_PROOF_TEST.md", MOCK_ENGINE_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 4: Valid UNKNOWN report passes ──────────────────────────────────────

def test_valid_unknown_passes(tmp_path: Path) -> None:
    p = _write_proof(tmp_path, "GENERATED_AUDIO_UNKNOWN_BLOCKER_TEST.md", UNKNOWN_MINIMAL)
    violations = validate_report(p)
    assert violations == [], f"Expected no violations, got: {violations}"


# ─── Test 5: Missing classification fails ─────────────────────────────────────

def test_missing_classification_fails(tmp_path: Path) -> None:
    content = """\
# Generated Audio Proof — Test

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

This report has no classification token in the body.

## Evidence

Some content here.

## Non-Claims

- no claims
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_NO_CLASS_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "MISSING_CLASSIFICATION" in rules


# ─── Test 6: Multiple distinct classifications fail ───────────────────────────

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

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

**Classification: STUB_ENGINE**

REAL_ENGINE confirmed in synthesis run.

## Non-Claims

- not runtime FULL PASS
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_BADCLAIM_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "NON_REAL_REPORT_CLAIMS_REAL_SYNTHESIS" in rules


# ─── Test 8: MOCK_ENGINE claiming real synthesis fails ────────────────────────

def test_mock_with_real_synthesis_claim_fails(tmp_path: Path) -> None:
    content = """\
# Mock Proof — Test

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: MOCK_ENGINE
proof_type: generated_audio
engine_mode_source: mock_fixture
runtime_claim: false
operator_claim: false
-->

**Classification: MOCK_ENGINE**

This is a real synthesis proof of voice quality.

## Non-Claims

- test only
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_MOCK_BADCLAIM_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "NON_REAL_REPORT_CLAIMS_REAL_SYNTHESIS" in rules


# ─── Test 9: REAL_ENGINE missing artifact evidence fails ──────────────────────

def test_real_engine_missing_artifact_evidence_fails(tmp_path: Path) -> None:
    content = """\
# Real Engine Proof — Test

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

routed_engine: xtts_v2

Library asset saved. asset id: abc123

Timeline revision 1->2 clip def

## Non-Claims

- not runtime FULL PASS
"""
    # Deliberately missing RIFF/WAV/bytes evidence
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_NOARTIFACT_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "REAL_ENGINE_MISSING_ARTIFACT_SIZE" in rules or "REAL_ENGINE_MISSING_ARTIFACT_FORMAT" in rules
    details = " ".join(v.detail for v in violations)
    assert "artifact" in details.lower() or "RIFF" in details or "bytes" in details


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


# ─── Test 11: Unrelated reports are ignored ───────────────────────────────────

def test_unrelated_reports_ignored() -> None:
    # Non-matching names should not be checked
    assert not _is_relevant(Path("TIMELINE_DURABILITY_HARDENING_2026-04-28.md"))
    assert not _is_relevant(Path("GAP008_SLICE45_MAINWINDOW.md"))
    assert not _is_relevant(Path("ADR_001_PLATFORM.md"))
    # Guard/boundary meta-reports excluded even if name starts with VOICE_SYNTHESIS
    assert not _is_relevant(Path("VOICE_SYNTHESIS_PROOF_BOUNDARY_GUARD_2026-04-29.md"))
    # Harness / tooling meta-reports (PROOF_HARNESS) excluded from proof-boundary gate
    assert not _is_relevant(Path("VOICE_SYNTHESIS_REAL_ENGINE_PROOF_HARNESS_2026-04-29.md"))
    assert not _is_relevant(Path("VOICE_SYNTHESIS_PROOF_DURABILITY_AND_SCHEMA_2026-04-29.md"))
    assert not _is_relevant(Path("VOICE_SYNTHESIS_PROOF_SCHEMA_NOTES_2026-04-29.md"))
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
    # Fix must be actionable (mention the valid tokens or "metadata")
    combined = v.fix + " " + v.detail
    assert any(
        tok in combined
        for tok in ("REAL_ENGINE", "STUB_ENGINE", "MOCK_ENGINE", "UNKNOWN", "metadata")
    )


# ─── Test 13: STUB_ENGINE claiming real synthesis via phrase in non-claims passes ─

def test_stub_real_claim_in_nonclaims_section_passes(tmp_path: Path) -> None:
    """
    A STUB_ENGINE report that mentions 'real synthesis proof' only inside
    an explicit non-claims section should pass (non-claims are excluded
    from forbidden-phrase scanning).
    """
    content = """\
# Stub Proof — Test

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

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


# ═══════════════════════════════════════════════════════════════════════════════
# NEW TESTS (20) — Hardening coverage
# ═══════════════════════════════════════════════════════════════════════════════

# ─── New Test 1: Metadata block required for relevant reports ─────────────────

def test_metadata_block_required(tmp_path: Path) -> None:
    """Report without metadata block fails with MISSING_METADATA_BLOCK."""
    content = """\
# Generated Audio Proof — No Metadata

**Classification: STUB_ENGINE**

VOICESTUDIO_TEST_MODE=1

## Non-Claims

- not real synthesis
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_NO_META_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "MISSING_METADATA_BLOCK" in rules


# ─── New Test 2: Metadata classification must match textual classification ─────

def test_metadata_classification_mismatch_fails(tmp_path: Path) -> None:
    """Metadata says STUB_ENGINE but report body says REAL_ENGINE → METADATA_CLASSIFICATION_MISMATCH."""
    content = """\
# Generated Audio Proof — Mismatched

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

routed_engine: xtts_v2

186,956 bytes (182.6 KiB)

RIFF WAVE header

Library asset id: abc123

Timeline revision 1 clip abc

## Non-Claims

- not runtime FULL PASS
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_MISMATCH_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "METADATA_CLASSIFICATION_MISMATCH" in rules


# ─── New Test 3: Metadata missing required field fails ────────────────────────

def test_metadata_missing_field_fails(tmp_path: Path) -> None:
    """Metadata block missing 'proof_type' field → METADATA_MISSING_FIELD."""
    content = """\
# Generated Audio Proof — Missing Field

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

**Classification: STUB_ENGINE**

VOICESTUDIO_TEST_MODE=1

## Non-Claims

- not real synthesis
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_MISSING_FIELD_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "METADATA_MISSING_FIELD" in rules
    details = " ".join(v.detail for v in violations)
    assert "proof_type" in details


# ─── New Test 4: Metadata invalid boolean fails ───────────────────────────────

def test_metadata_invalid_boolean_fails(tmp_path: Path) -> None:
    """runtime_claim = 'yes' is not a valid boolean → METADATA_INVALID_BOOLEAN."""
    content = """\
# Generated Audio Proof — Bad Boolean

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: yes
operator_claim: false
-->

**Classification: STUB_ENGINE**

VOICESTUDIO_TEST_MODE=1

## Non-Claims

- not real synthesis
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_BAD_BOOL_TEST.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "METADATA_INVALID_BOOLEAN" in rules
    details = " ".join(v.detail for v in violations)
    assert "runtime_claim" in details


# ─── New Test 5: --changed-from includes committed branch delta ───────────────

def test_changed_from_includes_committed_delta(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """--changed-from HEAD~ picks up a report committed on the branch."""
    _init_git_repo(tmp_path)
    # Write and commit a valid proof report
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    report = proof_dir / "GENERATED_AUDIO_COMMIT_DELTA_TEST.md"
    report.write_text(STUB_ENGINE_MINIMAL, encoding="utf-8")
    subprocess.run(["git", "add", "."], cwd=tmp_path, capture_output=True)
    subprocess.run(["git", "commit", "-m", "add proof"], cwd=tmp_path, capture_output=True)

    import scripts.ci.check_voice_synthesis_proof_boundary as mod
    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    # main uses --changed-from HEAD~ (initial commit)
    from scripts.ci.check_voice_synthesis_proof_boundary import _get_committed_changed_files
    paths = _get_committed_changed_files("HEAD~1")
    assert paths is not None
    names = [p.name for p in paths]
    assert "GENERATED_AUDIO_COMMIT_DELTA_TEST.md" in names


# ─── New Test 6: --changed-from includes staged relevant proof report ──────────

def test_changed_from_includes_staged_files(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Staged proof report is included in changed-file set."""
    _init_git_repo(tmp_path)
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    report = proof_dir / "GENERATED_AUDIO_STAGED_TEST.md"
    report.write_text(STUB_ENGINE_MINIMAL, encoding="utf-8")
    subprocess.run(["git", "add", str(report)], cwd=tmp_path, capture_output=True)

    import scripts.ci.check_voice_synthesis_proof_boundary as mod
    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    from scripts.ci.check_voice_synthesis_proof_boundary import _get_staged_changed_files
    paths = _get_staged_changed_files()
    assert paths is not None
    names = [p.name for p in paths]
    assert "GENERATED_AUDIO_STAGED_TEST.md" in names


# ─── New Test 7: --changed-from includes unstaged relevant proof report ────────

def test_changed_from_includes_unstaged_files(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Unstaged (modified, not yet staged) proof report is included."""
    _init_git_repo(tmp_path)
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    report = proof_dir / "GENERATED_AUDIO_UNSTAGED_TEST.md"
    report.write_text("initial", encoding="utf-8")
    subprocess.run(["git", "add", "."], cwd=tmp_path, capture_output=True)
    subprocess.run(["git", "commit", "-m", "init report"], cwd=tmp_path, capture_output=True)
    # Modify without staging
    report.write_text(STUB_ENGINE_MINIMAL, encoding="utf-8")

    import scripts.ci.check_voice_synthesis_proof_boundary as mod
    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    from scripts.ci.check_voice_synthesis_proof_boundary import _get_unstaged_changed_files
    paths = _get_unstaged_changed_files()
    assert paths is not None
    names = [p.name for p in paths]
    assert "GENERATED_AUDIO_UNSTAGED_TEST.md" in names


# ─── New Test 8: --changed-from includes untracked relevant proof report ───────

def test_changed_from_includes_untracked_files(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Untracked proof report under docs/reports/verification is included."""
    _init_git_repo(tmp_path)
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    # Write but don't add to git
    report = proof_dir / "GENERATED_AUDIO_UNTRACKED_TEST.md"
    report.write_text(STUB_ENGINE_MINIMAL, encoding="utf-8")

    import scripts.ci.check_voice_synthesis_proof_boundary as mod
    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    from scripts.ci.check_voice_synthesis_proof_boundary import _get_untracked_relevant_files
    paths = _get_untracked_relevant_files()
    assert paths is not None
    names = [p.name for p in paths]
    assert "GENERATED_AUDIO_UNTRACKED_TEST.md" in names


# ─── New Test 9: --changed-from ignores unrelated untracked markdown ───────────

def test_changed_from_ignores_unrelated_untracked(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Untracked markdown with non-matching name is not included."""
    _init_git_repo(tmp_path)
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    unrelated = proof_dir / "TIMELINE_DURABILITY_2026-04-29.md"
    unrelated.write_text("# Timeline\n\nno classification\n", encoding="utf-8")

    import scripts.ci.check_voice_synthesis_proof_boundary as mod
    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    from scripts.ci.check_voice_synthesis_proof_boundary import _get_untracked_relevant_files
    paths = _get_untracked_relevant_files()
    # Either None (git query failure) or not containing the unrelated file
    if paths is not None:
        names = [p.name for p in paths]
        assert "TIMELINE_DURABILITY_2026-04-29.md" not in names


# ─── New Test 10: STUB_ENGINE without non-claims section fails ─────────────────

def test_stub_engine_without_non_claims_fails(tmp_path: Path) -> None:
    """STUB_ENGINE report without any Non-Claims section → MISSING_NON_CLAIMS_SECTION."""
    content = """\
# Stub Proof — No Non-Claims

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

**Classification: STUB_ENGINE**

VOICESTUDIO_TEST_MODE=1
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_NONCLAIMS_MISSING.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "MISSING_NON_CLAIMS_SECTION" in rules


# ─── New Test 11: MOCK_ENGINE without non-claims section fails ─────────────────

def test_mock_engine_without_non_claims_fails(tmp_path: Path) -> None:
    """MOCK_ENGINE report without any Non-Claims section → MISSING_NON_CLAIMS_SECTION."""
    content = """\
# Mock Proof — No Non-Claims

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: MOCK_ENGINE
proof_type: generated_audio
engine_mode_source: mock_fixture
runtime_claim: false
operator_claim: false
-->

**Classification: MOCK_ENGINE**

Mock engine was used.
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_MOCK_NONCLAIMS_MISSING.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "MISSING_NON_CLAIMS_SECTION" in rules


# ─── New Test 12: UNKNOWN without blocker evidence fails ──────────────────────

def test_unknown_without_blocker_evidence_fails(tmp_path: Path) -> None:
    """UNKNOWN report with no blocker language → UNKNOWN_MISSING_BLOCKER_EVIDENCE."""
    content = """\
# Unknown Engine Proof — Explanation Missing

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: UNKNOWN
proof_type: voice_synthesis
engine_mode_source: manual_unknown
runtime_claim: false
operator_claim: false
-->

**Classification: UNKNOWN**

The engine status is uncertain.

## Non-Claims

- not real synthesis
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_UNKNOWN_NO_BLOCKER.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "UNKNOWN_MISSING_BLOCKER_EVIDENCE" in rules


# ─── New Test 13: UNKNOWN with blocker evidence passes ────────────────────────

def test_unknown_with_blocker_evidence_passes(tmp_path: Path) -> None:
    """UNKNOWN report with explicit blocker language passes the UNKNOWN check."""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_UNKNOWN_WITH_BLOCKER.md", UNKNOWN_MINIMAL)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "UNKNOWN_MISSING_BLOCKER_EVIDENCE" not in rules


# ─── New Test 14: REAL_ENGINE without non-claims section fails ─────────────────

def test_real_engine_without_non_claims_fails(tmp_path: Path) -> None:
    """REAL_ENGINE report without Non-Claims section → MISSING_NON_CLAIMS_SECTION."""
    content = """\
# Real Engine Proof — No Non-Claims

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

186,956 bytes (182.6 KiB)
RIFF WAVE header confirmed.

Library asset id: abc123

Timeline revision 1 clip def
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_NO_NONCLAIMS.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "MISSING_NON_CLAIMS_SECTION" in rules


# ─── New Test 15: REAL_ENGINE negative-only "no library evidence" fails ────────

def test_real_engine_negative_library_evidence_fails(tmp_path: Path) -> None:
    """REAL_ENGINE report with 'no library evidence' (negative-only) → REAL_ENGINE_NEGATIVE_LIBRARY_EVIDENCE."""
    content = """\
# Real Engine Proof — Negative Library

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

routed_engine: xtts_v2

186,956 bytes (182.6 KiB)

RIFF WAVE header confirmed.

no library evidence at this time.

Timeline revision 1 clip def

## Non-Claims

- durability not tested
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_NEG_LIBRARY.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "REAL_ENGINE_NEGATIVE_LIBRARY_EVIDENCE" in rules


# ─── New Test 16: REAL_ENGINE negative-only "timeline not tested" fails ────────

def test_real_engine_negative_timeline_evidence_fails(tmp_path: Path) -> None:
    """REAL_ENGINE report with 'no timeline evidence' (negative-only) → REAL_ENGINE_NEGATIVE_TIMELINE_EVIDENCE."""
    content = """\
# Real Engine Proof — Negative Timeline

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

routed_engine: xtts_v2

186,956 bytes (182.6 KiB)

RIFF WAVE header confirmed.

Library asset id: abc123

no timeline evidence available.

## Non-Claims

- durability not tested
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_GENERATED_AUDIO_NEG_TIMELINE.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "REAL_ENGINE_NEGATIVE_TIMELINE_EVIDENCE" in rules


# ─── New Test 17: STUB_ENGINE claiming "runtime FULL PASS" outside non-claims fails ─

def test_stub_claiming_runtime_full_pass_fails(tmp_path: Path) -> None:
    """STUB_ENGINE report containing 'runtime FULL PASS' outside non-claims fails."""
    content = """\
# Stub Proof — Overclaims FULL PASS

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

**Classification: STUB_ENGINE**

runtime FULL PASS observed during synthesis run.

## Non-Claims

- this is a stub
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_FULLPASS.md", content)
    violations = validate_report(p)
    rules = [v.rule for v in violations]
    assert "NON_REAL_REPORT_CLAIMS_REAL_SYNTHESIS" in rules


# ─── New Test 18: STUB_ENGINE "runtime FULL PASS is not claimed" in non-claims passes ─

def test_stub_runtime_full_pass_negation_in_nonclaims_passes(tmp_path: Path) -> None:
    """STUB_ENGINE report with 'runtime FULL PASS' only in the Non-Claims section passes."""
    content = """\
# Stub Proof — Correct Non-Claims

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->

**Classification: STUB_ENGINE**

VOICESTUDIO_TEST_MODE=1. Orchestration proven, not real synthesis.

## Non-Claims

- runtime FULL PASS is not claimed
- not REAL_ENGINE
- not operator proof
"""
    p = _write_proof(tmp_path, "GENERATED_AUDIO_STUB_FULLPASS_NONCLAIMS_OK.md", content)
    violations = validate_report(p)
    assert violations == [], f"Phrase in non-claims should be excluded: {violations}"


# ─── New Test 19: JSON output includes status, mode, checked, violations ───────

def test_json_output_structure(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """--json output is valid JSON with status, mode, checked, violations keys."""
    import scripts.ci.check_voice_synthesis_proof_boundary as mod

    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    report = proof_dir / "GENERATED_AUDIO_JSON_TEST.md"
    report.write_text(STUB_ENGINE_MINIMAL, encoding="utf-8")

    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    # Capture stdout without capsys (avoid pytest-asyncio incompatibility)
    buf = io.StringIO()
    with patch("builtins.print", side_effect=lambda *a, **kw: buf.write(" ".join(str(x) for x in a) + "\n")):
        ret = main(["--all", "--json"])

    output = buf.getvalue()
    # Find the JSON object in stdout
    json_start = output.find("{")
    assert json_start >= 0, f"No JSON found in output: {output!r}"
    data = json.loads(output[json_start:output.rfind("}") + 1])

    assert "status" in data
    assert "mode" in data
    assert "checked" in data
    assert "violations" in data
    assert isinstance(data["checked"], list)
    assert isinstance(data["violations"], list)


# ─── New Test 20: Guard meta-report is excluded from all mode ─────────────────

def test_guard_meta_report_excluded_in_all_mode(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """--all mode does not pick up guard/meta-report files."""
    import scripts.ci.check_voice_synthesis_proof_boundary as mod

    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    # Guard/meta report
    guard = proof_dir / "VOICE_SYNTHESIS_PROOF_BOUNDARY_GUARD_2026-04-29.md"
    guard.write_text("# Guard Report\n\n## Non-Claims\n- test\n", encoding="utf-8")
    harness_meta = proof_dir / "VOICE_SYNTHESIS_REAL_ENGINE_PROOF_HARNESS_2026-04-29.md"
    harness_meta.write_text("# Harness Meta\n\n## Non-Claims\n- test\n", encoding="utf-8")
    durability_meta = proof_dir / "VOICE_SYNTHESIS_PROOF_DURABILITY_AND_SCHEMA_2026-04-29.md"
    durability_meta.write_text("# Durability Meta\n\n## Non-Claims\n- test\n", encoding="utf-8")

    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    files = mod._get_all_relevant_files()
    names = [p.name for p in files]
    assert "VOICE_SYNTHESIS_PROOF_BOUNDARY_GUARD_2026-04-29.md" not in names
    assert "VOICE_SYNTHESIS_REAL_ENGINE_PROOF_HARNESS_2026-04-29.md" not in names
    assert "VOICE_SYNTHESIS_PROOF_DURABILITY_AND_SCHEMA_2026-04-29.md" not in names


# ─── Bonus Test 21: --self-test-examples exits 0 ─────────────────────────────

def test_self_test_examples_passes() -> None:
    """--self-test-examples CLI mode exits 0 — built-in sanity check."""
    ret = main(["--self-test-examples"])
    assert ret == 0, "Built-in self-test examples must all pass"


# ═══════════════════════════════════════════════════════════════════════════════
# Residual gap tests (validator hardening)
# ═══════════════════════════════════════════════════════════════════════════════

_REAL_BODY_CORE = """\
**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

186,956 bytes (182.6 KiB)

RIFF WAVE header confirmed.

HTTP 201 library asset; audio_id z9

timeline revision 1→2; clip_id z9; POST /api/timeline/tracks

binary audio; does not start with `{`
"""


def test_duplicate_metadata_blocks_fail(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Dup Meta

""" + _REAL_BODY_CORE + """

## Non-Claims

- not operator proof
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_DUP_META_BLOCK.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "DUPLICATE_METADATA_BLOCK" in rules


def test_metadata_duplicate_field_key_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Dup Field

""" + _REAL_BODY_CORE + """

## Non-Claims

- not operator proof
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_DUP_META_FIELD.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "METADATA_DUPLICATE_FIELD" in rules


def test_metadata_invalid_classification_value_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: NOT_A_REAL_TOKEN
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Bad meta class

**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

186,956 bytes (182.6 KiB)

RIFF WAVE header.

HTTP 201 library asset; audio_id z

timeline revision 1; clip_id z; /api/timeline/state

binary audio; not a JSON error body

## Non-Claims

- not operator proof
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_BAD_META_CLASS.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "METADATA_INVALID_CLASSIFICATION" in rules


def test_metadata_invalid_proof_type_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: not_a_valid_proof_type
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Bad proof_type

""" + _REAL_BODY_CORE + """

## Non-Claims

- not operator proof
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_BAD_PROOF_TYPE.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "METADATA_INVALID_PROOF_TYPE" in rules


def test_metadata_invalid_engine_mode_source_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: imaginary_source
runtime_claim: false
operator_claim: false
-->
# Bad engine_mode_source

""" + _REAL_BODY_CORE + """

## Non-Claims

- not operator proof
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_BAD_ENGINE_SRC.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "METADATA_INVALID_ENGINE_MODE_SOURCE" in rules


def test_operator_claim_true_missing_evidence_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: true
-->
# Op claim no evidence

""" + _REAL_BODY_CORE + """

## Non-Claims

- operator playback is not attested in the main body above
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_OP_CLAIM_NO_EVID.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "OPERATOR_CLAIM_MISSING_EVIDENCE" in rules


def test_runtime_claim_true_missing_evidence_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: true
operator_claim: false
-->
# Rt claim no evidence

""" + _REAL_BODY_CORE + """

## Non-Claims

- runtime FULL PASS is not claimed in the main body above
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_RT_CLAIM_NO_EVID.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "RUNTIME_CLAIM_MISSING_EVIDENCE" in rules


def test_operator_claim_true_with_evidence_passes(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: true
-->
# Op claim with evidence

""" + _REAL_BODY_CORE + """

Operator manual playback confirmed.

## Non-Claims

- not end-to-end certification
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_OP_CLAIM_OK.md", content)
    assert validate_report(p) == []


def test_runtime_claim_true_with_evidence_passes(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: true
operator_claim: false
-->
# Rt claim with evidence

""" + _REAL_BODY_CORE + """

End-to-end runtime FULL PASS recorded.

## Non-Claims

- not operator attestation
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_RT_CLAIM_OK.md", content)
    assert validate_report(p) == []


def test_real_engine_missing_non_error_audio_evidence_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

186,956 bytes (182.6 KiB)

RIFF WAVE header confirmed.

HTTP 201 library asset; audio_id z

timeline revision 1; clip_id z; /api/timeline/tracks

## Non-Claims

- not operator proof
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_NO_NONERROR_BODY.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "REAL_ENGINE_MISSING_NON_ERROR_AUDIO_EVIDENCE" in rules


def test_negative_library_phrase_only_in_non_claims_passes(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Neg lib in NC only

""" + _REAL_BODY_CORE + """

## Explicit Non-Claims

- no library evidence for durability (hypothetical)
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_NEG_LIB_IN_NC.md", content)
    assert validate_report(p) == []


def test_negative_timeline_phrase_only_in_non_claims_passes(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Neg timeline in NC only

""" + _REAL_BODY_CORE + """

## Explicit Non-Claims

- no timeline evidence for export (out of scope)
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_NEG_TL_IN_NC.md", content)
    assert validate_report(p) == []


def test_positive_library_evidence_only_in_non_claims_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

186,956 bytes (182.6 KiB)

RIFF WAVE header confirmed.

timeline revision 1; clip_id z; /api/timeline/tracks

binary audio; does not start with `{`

## Explicit Non-Claims

- HTTP 201 library asset would appear here only
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_LIB_ONLY_NC.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "REAL_ENGINE_MISSING_LIBRARY_EVIDENCE" in rules


def test_positive_timeline_evidence_only_in_non_claims_fails(tmp_path: Path) -> None:
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

186,956 bytes (182.6 KiB)

RIFF WAVE header confirmed.

HTTP 201 library asset; audio_id z

binary audio; not a JSON error body

## Explicit Non-Claims

- clip_id fake only in non-claims; POST /api/timeline/tracks hypothetical
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_TL_ONLY_NC.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "REAL_ENGINE_MISSING_TIMELINE_EVIDENCE" in rules


def test_negative_inside_non_claims_does_not_trigger_negative_library_rule(
    tmp_path: Path,
) -> None:
    """'no library evidence' inside Non-Claims must not pair with missing positive."""
    content = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# NC neg lib

""" + _REAL_BODY_CORE + """

## Explicit Non-Claims

- no library evidence for unrelated subsystem X
"""
    p = _write_proof(tmp_path, "REAL_ENGINE_NC_NEG_LIB.md", content)
    rules = [v.rule for v in validate_report(p)]
    assert "REAL_ENGINE_NEGATIVE_LIBRARY_EVIDENCE" not in rules


def test_json_output_includes_residual_rules(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    import scripts.ci.check_voice_synthesis_proof_boundary as mod

    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True, exist_ok=True)
    report = proof_dir / "GENERATED_AUDIO_JSON_RESIDUAL.md"
    report.write_text(
        """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: bad_proof_type_x
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
**Classification: REAL_ENGINE**

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

186,956 bytes (182.6 KiB)

RIFF WAVE

HTTP 201 library asset

clip_id x; /api/timeline/

binary audio; not a JSON error body

## Non-Claims

- test
""",
        encoding="utf-8",
    )

    monkeypatch.setattr(mod, "ROOT", tmp_path)
    monkeypatch.setattr(mod, "RELEVANT_DIR", proof_dir)

    buf = io.StringIO()
    with patch("builtins.print", side_effect=lambda *a, **kw: buf.write(" ".join(str(x) for x in a) + "\n")):
        ret = main(["--all", "--json"])

    assert ret == 1
    output = buf.getvalue()
    json_start = output.find("{")
    data = json.loads(output[json_start : output.rfind("}") + 1])
    rules = {v["rule"] for v in data["violations"]}
    assert "METADATA_INVALID_PROOF_TYPE" in rules
