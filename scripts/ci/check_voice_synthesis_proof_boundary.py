#!/usr/bin/env python3
"""
CI check: enforce engine-mode classification in voice synthesis proof reports.

Every new or changed proof report whose filename matches:
  VOICE_SYNTHESIS*.md, GENERATED_AUDIO*.md, REAL_ENGINE_GENERATED_AUDIO*.md
under docs/reports/verification/ must:
  1. Contain a VOICESTUDIO_PROOF_BOUNDARY_V1 metadata block
  2. Declare exactly one of: REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN
  3. Contain an explicit Non-Claims / Boundaries section
  4. Pass classification-specific evidence rules

Usage:
  python scripts/ci/check_voice_synthesis_proof_boundary.py
      (changed-from mode vs origin/main — default, for CI)
  python scripts/ci/check_voice_synthesis_proof_boundary.py --changed-from origin/main
  python scripts/ci/check_voice_synthesis_proof_boundary.py --all
  python scripts/ci/check_voice_synthesis_proof_boundary.py --json
  python scripts/ci/check_voice_synthesis_proof_boundary.py --self-test-examples
  python scripts/ci/check_voice_synthesis_proof_boundary.py --help

Exit 0 = all checked reports pass.
Exit 1 = violations found (messages printed to stderr).
Exit 0 with advisory when git is unavailable in changed-from mode.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import NamedTuple

ROOT = Path(__file__).resolve().parent.parent.parent
RELEVANT_DIR = ROOT / "docs" / "reports" / "verification"

RELEVANT_NAME_PATTERNS: list[re.Pattern[str]] = [
    re.compile(r"VOICE_SYNTHESIS.*\.md$", re.IGNORECASE),
    re.compile(r"GENERATED_AUDIO.*\.md$", re.IGNORECASE),
    re.compile(r"REAL_ENGINE_GENERATED_AUDIO.*\.md$", re.IGNORECASE),
]

# Names that match the patterns above but are meta/guard reports — not synthesis proofs.
_EXCLUSION_PATTERNS: list[re.Pattern[str]] = [
    re.compile(r"PROOF_BOUNDARY", re.IGNORECASE),
    re.compile(r"_BOUNDARY_GUARD", re.IGNORECASE),
    re.compile(r"_GUARD_", re.IGNORECASE),
]

VALID_CLASSIFICATIONS = frozenset(
    ["REAL_ENGINE", "STUB_ENGINE", "MOCK_ENGINE", "UNKNOWN"]
)

# ── Metadata block ────────────────────────────────────────────────────────────
_METADATA_BLOCK_RE = re.compile(
    r"<!--\s*VOICESTUDIO_PROOF_BOUNDARY_V1\s*\n(.*?)-->",
    re.DOTALL,
)
_METADATA_FIELD_RE = re.compile(r"^\s*(\w+)\s*:\s*(.+?)\s*$", re.MULTILINE)

_REQUIRED_METADATA_FIELDS = frozenset([
    "classification", "proof_type", "engine_mode_source",
    "runtime_claim", "operator_claim",
])
_VALID_PROOF_TYPES = frozenset([
    "voice_synthesis", "generated_audio", "proof_boundary", "other",
])
_VALID_ENGINE_MODE_SOURCES = frozenset([
    "runtime_probe", "test_mode_env", "mock_fixture",
    "blocked_unknown", "manual_unknown", "not_applicable",
])

# ── Classification detection ──────────────────────────────────────────────────
_CLASS_LABEL = re.compile(
    r"(?:classification|verdict|engine.?mode|ENGINE.?MODE)\s*[:\|]?\s*\**\s*(REAL_ENGINE|STUB_ENGINE|MOCK_ENGINE|UNKNOWN)\b",
    re.IGNORECASE,
)
_BARE_CLASS_TOKEN = re.compile(
    r"\b(REAL_ENGINE|STUB_ENGINE|MOCK_ENGINE|UNKNOWN)\b"
)

# ── Non-claims section detection ──────────────────────────────────────────────
# Matches headings whose *subject* is a non-claims/boundaries section.
# The pattern anchors at the START of the heading text to avoid matching titles
# that merely mention the word (e.g. "# Stub Proof — No Non-Claims").
_NON_CLAIMS_HEADING = re.compile(
    r"^#{1,6}\s+(?:\d+\.?\s+)?(?:explicit\s+non.?claims?|non.?claims?|mock.?stub\s+non.?claims?|"
    r"boundaries|proof\s+boundary|what\s+this\s+does\s+not\s+prove)",
    re.IGNORECASE | re.MULTILINE,
)

# ── UNKNOWN blocker evidence ──────────────────────────────────────────────────
_UNKNOWN_BLOCKER_TERMS = re.compile(
    r"\b(?:blocker|blocked|could\s+not\s+determine|unable\s+to\s+determine|"
    r"engine\s+mode\s+unknown|unavailable|missing\s+evidence|"
    r"verification\s+could\s+not\s+complete|automatic\s+verification\s+failed)\b",
    re.IGNORECASE,
)

# ── REAL_ENGINE positive library evidence ─────────────────────────────────────
_LIBRARY_POSITIVE = re.compile(
    r"(?:asset[ _]id|library\s+asset|HTTP\s+201|audio_id|Created|"
    r"upload_id|asset\s+id)",
    re.IGNORECASE,
)
_LIBRARY_NEGATIVE = re.compile(
    r"(?:no\s+library\s+evidence|library\s+not\s+tested|"
    r"library\s+unavailable|library\s+not\s+verified)",
    re.IGNORECASE,
)
# ── REAL_ENGINE positive timeline evidence ────────────────────────────────────
_TIMELINE_POSITIVE = re.compile(
    r"\b(?:revision|track|clip|placement|start_time|end_time|"
    r"clip_id|track_id|timeline\s+revision)\b",
    re.IGNORECASE,
)
_TIMELINE_NEGATIVE = re.compile(
    r"(?:no\s+timeline\s+evidence|timeline\s+not\s+tested|"
    r"timeline\s+unavailable|timeline\s+not\s+verified)",
    re.IGNORECASE,
)

# ── Forbidden real-synthesis claims (STUB/MOCK/UNKNOWN) ───────────────────────
_FORBIDDEN_REAL_CLAIM_PHRASES: list[re.Pattern[str]] = [
    re.compile(r"REAL_ENGINE\s+(?:classification\s+)?confirmed", re.IGNORECASE),
    re.compile(r"real\s+synthesis\s+proof", re.IGNORECASE),
    re.compile(r"real\s+engine\s+generated\s+audio\s+proof", re.IGNORECASE),
    re.compile(r"actual\s+model\s+output\s+confirmed", re.IGNORECASE),
    re.compile(r"real\s+model\s+output", re.IGNORECASE),
    re.compile(r"non.?stub\s+synthesis\s+confirmed", re.IGNORECASE),
    re.compile(r"runtime\s+proof\s+complete", re.IGNORECASE),
    re.compile(r"runtime\s+FULL\s+PASS", re.IGNORECASE),
    re.compile(r"operator\s+proof\s+complete", re.IGNORECASE),
    re.compile(r"heard\s+attestation", re.IGNORECASE),
    re.compile(r"manual\s+playback\s+confirmed", re.IGNORECASE),
]


class Violation(NamedTuple):
    file: str
    rule: str
    detail: str
    fix: str


def _rel(path: Path) -> str:
    try:
        return str(path.relative_to(ROOT)).replace("\\", "/")
    except ValueError:
        return str(path)


def _is_relevant(path: Path) -> bool:
    name = path.name
    if any(exc.search(name) for exc in _EXCLUSION_PATTERNS):
        return False
    return any(pat.search(name) for pat in RELEVANT_NAME_PATTERNS)


def _strip_non_claims_sections(text: str) -> str:
    """Return text with non-claims sections blanked out (replaced by spaces)."""
    result = list(text)
    for m in _NON_CLAIMS_HEADING.finditer(text):
        start = m.start()
        heading_level = len(re.match(r"^(#+)", m.group()).group(1))
        end_pattern = re.compile(
            r"^#{1," + str(heading_level) + r"}\s+",
            re.MULTILINE,
        )
        next_heading = end_pattern.search(text, m.end())
        end = next_heading.start() if next_heading else len(text)
        for i in range(start, end):
            result[i] = " "
    return "".join(result)


def _find_classifications(text: str) -> list[str]:
    """Return list of unique classification tokens found in the text."""
    found: list[str] = []
    for m in _CLASS_LABEL.finditer(text):
        token = m.group(1).upper()
        if token not in found:
            found.append(token)
    summary_block_re = re.compile(
        r"(?:classification summary|verdict)[^\n]*\n(?:[^\n]*\n){0,15}",
        re.IGNORECASE,
    )
    for block_m in summary_block_re.finditer(text):
        block = block_m.group()
        for bare_m in _BARE_CLASS_TOKEN.finditer(block):
            token = bare_m.group(1).upper()
            if token not in found:
                found.append(token)
    return found


def _parse_metadata_block(text: str) -> dict[str, str] | None:
    """Return parsed metadata fields from VOICESTUDIO_PROOF_BOUNDARY_V1 block, or None."""
    m = _METADATA_BLOCK_RE.search(text)
    if not m:
        return None
    body = m.group(1)
    fields: dict[str, str] = {}
    for fm in _METADATA_FIELD_RE.finditer(body):
        fields[fm.group(1).lower()] = fm.group(2).strip()
    return fields


def validate_report(path: Path) -> list[Violation]:
    """Validate a single proof report. Return list of Violation (empty = pass)."""
    violations: list[Violation] = []
    rel = _rel(path)

    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError as e:
        return [Violation(rel, "FILE_READ", str(e), "Ensure file is readable")]

    # ── 1. Metadata block ────────────────────────────────────────────────────
    metadata = _parse_metadata_block(text)
    if metadata is None:
        violations.append(Violation(
            file=rel,
            rule="MISSING_METADATA_BLOCK",
            detail="No VOICESTUDIO_PROOF_BOUNDARY_V1 metadata block found",
            fix=(
                "Add a metadata block near the top of the report:\n"
                "<!-- VOICESTUDIO_PROOF_BOUNDARY_V1\n"
                "classification: REAL_ENGINE\n"
                "proof_type: voice_synthesis\n"
                "engine_mode_source: runtime_probe\n"
                "runtime_claim: false\n"
                "operator_claim: false\n"
                "-->"
            ),
        ))
        # Continue — we can still check classification and other rules
        metadata = {}
    else:
        # Check required fields
        for field in _REQUIRED_METADATA_FIELDS:
            if field not in metadata:
                violations.append(Violation(
                    file=rel,
                    rule="METADATA_MISSING_FIELD",
                    detail=f"Metadata block missing required field: '{field}'",
                    fix=f"Add '{field}: <value>' to the VOICESTUDIO_PROOF_BOUNDARY_V1 block",
                ))
        # Check boolean fields
        for bool_field in ("runtime_claim", "operator_claim"):
            val = metadata.get(bool_field, "")
            if val and val.lower() not in ("true", "false"):
                violations.append(Violation(
                    file=rel,
                    rule="METADATA_INVALID_BOOLEAN",
                    detail=f"Metadata field '{bool_field}' must be 'true' or 'false', got '{val}'",
                    fix=f"Set '{bool_field}: true' or '{bool_field}: false' in the metadata block",
                ))

    # ── 2. Classification detection ──────────────────────────────────────────
    # Strip the metadata block before searching for classifications so that the
    # metadata `classification: X` field doesn't pollute textual detection.
    body_for_class = _METADATA_BLOCK_RE.sub("", text)
    classifications = _find_classifications(body_for_class)

    if not classifications:
        violations.append(Violation(
            file=rel,
            rule="MISSING_CLASSIFICATION",
            detail="No engine-mode classification token found",
            fix=(
                'Add "**Classification: REAL_ENGINE**" (or STUB_ENGINE / MOCK_ENGINE / UNKNOWN) '
                "near the top of the report, or a VERDICT: / engine_mode: line"
            ),
        ))
        return violations  # can't continue without classification

    if len(classifications) > 1:
        violations.append(Violation(
            file=rel,
            rule="AMBIGUOUS_CLASSIFICATION",
            detail=f"Multiple distinct classifications found: {classifications}",
            fix=(
                "Ensure exactly one classification token is present; "
                "move REAL_ENGINE / STUB_ENGINE / MOCK_ENGINE / UNKNOWN to a single labelled line"
            ),
        ))
        return violations

    classification = classifications[0]

    # Check metadata classification matches textual classification
    meta_class = metadata.get("classification", "").upper()
    if meta_class and meta_class != classification:
        violations.append(Violation(
            file=rel,
            rule="METADATA_CLASSIFICATION_MISMATCH",
            detail=(
                f"Metadata classification '{meta_class}' does not match "
                f"textual classification '{classification}'"
            ),
            fix=(
                f"Update the metadata block classification to '{classification}' "
                f"to match the report's textual classification"
            ),
        ))

    # ── 3. Non-claims section ────────────────────────────────────────────────
    if not _NON_CLAIMS_HEADING.search(text):
        violations.append(Violation(
            file=rel,
            rule="MISSING_NON_CLAIMS_SECTION",
            detail="No explicit Non-Claims / Boundaries section found",
            fix=(
                "Add a section with one of these headings: "
                "## Explicit Non-Claims | ## Non-Claims | ## Boundaries | "
                "## Proof Boundary | ## What This Does Not Prove"
            ),
        ))

    # ── 4. UNKNOWN: require blocker evidence ─────────────────────────────────
    if classification == "UNKNOWN":
        # Search the full text for blocker evidence; the blocker terms are
        # specific enough (blocker, could not determine, etc.) that they won't
        # produce false positives from classification or metadata lines.
        if not _UNKNOWN_BLOCKER_TERMS.search(text):
            violations.append(Violation(
                file=rel,
                rule="UNKNOWN_MISSING_BLOCKER_EVIDENCE",
                detail="UNKNOWN report has no blocker evidence explaining why engine mode could not be determined",
                fix=(
                    "Add an UNKNOWN blocker section with one of: "
                    "blocker, blocked, could not determine, unable to determine, "
                    "verification could not complete, missing evidence, unavailable"
                ),
            ))

    # ── 5. REAL_ENGINE: require evidence terms ───────────────────────────────
    if classification == "REAL_ENGINE":
        # routed_engine
        if "routed_engine" not in text:
            violations.append(Violation(
                file=rel,
                rule="REAL_ENGINE_MISSING_ROUTED_ENGINE",
                detail="Missing 'routed_engine' field — no engine routing evidence",
                fix="Add a section showing routed_engine: <engine_name> (e.g. xtts_v2, piper)",
            ))
        else:
            # Check routed_engine is not stub/mock/test
            stub_only = re.search(
                r'routed.?engine["\s:=`]*"?(?:stub|mock|test)"?',
                text,
                re.IGNORECASE,
            )
            real_engine_val = re.search(
                r'routed.?engine["\s:=`]*"?(?!stub|mock|test)(\w+)"?',
                text,
                re.IGNORECASE,
            )
            if stub_only and not real_engine_val:
                violations.append(Violation(
                    file=rel,
                    rule="REAL_ENGINE_STUB_ROUTED",
                    detail="routed_engine shows stub/mock/test value — not valid for REAL_ENGINE classification",
                    fix=(
                        "Change Classification to STUB_ENGINE or MOCK_ENGINE, "
                        "or confirm routed_engine = xtts_v2 / piper / etc."
                    ),
                ))

        # Artifact size
        if not any(term in text for term in ("bytes", "KiB", "MiB", " B)")):
            violations.append(Violation(
                file=rel,
                rule="REAL_ENGINE_MISSING_ARTIFACT_SIZE",
                detail="Missing audio artifact size evidence — none of: bytes, KiB, MiB",
                fix="Add artifact size (e.g. '186,956 bytes (182.6 KiB)') in an Audio Artifact section",
            ))

        # Artifact format
        if not any(term in text for term in ("RIFF", "WAV", "WAVE", "header")):
            violations.append(Violation(
                file=rel,
                rule="REAL_ENGINE_MISSING_ARTIFACT_FORMAT",
                detail="Missing audio artifact format evidence — none of: RIFF, WAV, WAVE, header",
                fix="Add RIFF/WAV header validation evidence in an Audio Artifact section",
            ))

        # Library evidence — require positive, reject negative-only
        has_positive_library = bool(_LIBRARY_POSITIVE.search(text))
        has_negative_library = bool(_LIBRARY_NEGATIVE.search(text))
        if has_negative_library and not has_positive_library:
            violations.append(Violation(
                file=rel,
                rule="REAL_ENGINE_NEGATIVE_LIBRARY_EVIDENCE",
                detail="Report contains negative-only library evidence phrase ('no library evidence', etc.)",
                fix=(
                    "Add positive library evidence (asset id, library asset, audio_id, HTTP 201) "
                    "or move negative statement to an Explicit Non-Claims section"
                ),
            ))
        elif not has_positive_library and not has_negative_library:
            # Fallback — check for broad library mentions
            if not any(term in text for term in ("library", "Library", "asset", "Asset")):
                violations.append(Violation(
                    file=rel,
                    rule="REAL_ENGINE_MISSING_LIBRARY_EVIDENCE",
                    detail="No library evidence found — REAL_ENGINE reports must document library save",
                    fix=(
                        "Add a Library Evidence section with asset id, library asset, "
                        "or audio_id from the upload response"
                    ),
                ))

        # Timeline evidence — require positive, reject negative-only
        has_positive_timeline = bool(_TIMELINE_POSITIVE.search(text))
        has_negative_timeline = bool(_TIMELINE_NEGATIVE.search(text))
        if has_negative_timeline and not has_positive_timeline:
            violations.append(Violation(
                file=rel,
                rule="REAL_ENGINE_NEGATIVE_TIMELINE_EVIDENCE",
                detail="Report contains negative-only timeline evidence phrase ('no timeline evidence', etc.)",
                fix=(
                    "Add positive timeline evidence (revision, track, clip, placement) "
                    "or move negative statement to an Explicit Non-Claims section"
                ),
            ))
        elif not has_positive_timeline and not has_negative_timeline:
            if not any(term in text for term in ("timeline", "Timeline", "clip", "Clip")):
                violations.append(Violation(
                    file=rel,
                    rule="REAL_ENGINE_MISSING_TIMELINE_EVIDENCE",
                    detail="No timeline evidence found — REAL_ENGINE reports must document timeline placement",
                    fix=(
                        "Add a Timeline Evidence section with revision, clip, or track details; "
                        "or add an explicit non-claim if timeline is out of scope for this proof"
                    ),
                ))

    # ── 6. STUB / MOCK / UNKNOWN: forbid real-synthesis claims ───────────────
    elif classification in ("STUB_ENGINE", "MOCK_ENGINE", "UNKNOWN"):
        searchable = _strip_non_claims_sections(text)
        for pat in _FORBIDDEN_REAL_CLAIM_PHRASES:
            m = pat.search(searchable)
            if m:
                line_no = text.count("\n", 0, m.start()) + 1
                violations.append(Violation(
                    file=rel,
                    rule="NON_REAL_REPORT_CLAIMS_REAL_SYNTHESIS",
                    detail=(
                        f'Report classified as {classification} but contains '
                        f'real-synthesis claim "{m.group()}" near line {line_no}'
                    ),
                    fix=(
                        "Move this phrase to the explicit non-claims section, "
                        "or correct the classification to REAL_ENGINE if synthesis was real"
                    ),
                ))

    return violations


# ── Git file discovery ────────────────────────────────────────────────────────

def _run_git_names(args: list[str]) -> list[Path] | None:
    """Run a git command and return list of Paths from its stdout lines, or None on failure."""
    try:
        result = subprocess.run(
            ["git"] + args,
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode != 0:
            return None
        paths = []
        for line in result.stdout.splitlines():
            line = line.strip()
            if line:
                full = ROOT / line.replace("\\", "/")
                paths.append(full)
        return paths
    except (subprocess.TimeoutExpired, FileNotFoundError):
        return None


def _get_committed_changed_files(ref: str) -> list[Path] | None:
    """Files changed in committed branch delta since ref."""
    return _run_git_names(["diff", "--name-only", "--diff-filter=ACM", f"{ref}..HEAD"])


def _get_staged_changed_files() -> list[Path] | None:
    """Files staged in the index (not yet committed)."""
    return _run_git_names(["diff", "--name-only", "--cached", "--diff-filter=ACM"])


def _get_unstaged_changed_files() -> list[Path] | None:
    """Files with unstaged working-tree changes."""
    return _run_git_names(["diff", "--name-only", "--diff-filter=ACM"])


def _get_untracked_relevant_files() -> list[Path] | None:
    """Untracked files under docs/reports/verification/ that match relevant patterns."""
    try:
        result = subprocess.run(
            ["git", "ls-files", "--others", "--exclude-standard",
             str(RELEVANT_DIR.relative_to(ROOT))],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode != 0:
            return None
        paths = []
        for line in result.stdout.splitlines():
            line = line.strip()
            if line:
                full = ROOT / line.replace("\\", "/")
                if _is_relevant(full) and full.suffix.lower() == ".md":
                    paths.append(full)
        return paths
    except (subprocess.TimeoutExpired, FileNotFoundError):
        return None


def _get_changed_files(ref: str) -> list[Path] | None:
    """
    Return union of committed+staged+unstaged+untracked relevant proof reports.
    Returns None only if git itself is unavailable.
    Advisory messages are printed for partial failures.
    """
    # Test git availability first
    try:
        probe = subprocess.run(
            ["git", "rev-parse", "--git-dir"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=5,
        )
        if probe.returncode != 0:
            return None
    except (subprocess.TimeoutExpired, FileNotFoundError):
        return None

    seen: set[Path] = set()
    all_paths: list[Path] = []

    def _add(source_name: str, paths: list[Path] | None) -> None:
        if paths is None:
            print(
                f"[ADVISORY] voice_synthesis_proof_boundary: {source_name} query unavailable",
                file=sys.stderr,
            )
            return
        for p in paths:
            if p not in seen:
                seen.add(p)
                all_paths.append(p)

    committed = _get_committed_changed_files(ref)
    if committed is None:
        # ref may not exist yet (fresh branch) — not fatal
        print(
            f"[ADVISORY] voice_synthesis_proof_boundary: committed-delta query failed "
            f"(ref '{ref}' may not exist) — continuing with staged/unstaged/untracked",
            file=sys.stderr,
        )
    else:
        _add("committed-delta", committed)

    _add("staged", _get_staged_changed_files())
    _add("unstaged", _get_unstaged_changed_files())

    # Untracked: already filtered to relevant in the helper
    untracked = _get_untracked_relevant_files()
    if untracked is not None:
        for p in untracked:
            if p not in seen:
                seen.add(p)
                all_paths.append(p)
    else:
        print(
            "[ADVISORY] voice_synthesis_proof_boundary: untracked-files query unavailable",
            file=sys.stderr,
        )

    return sorted(all_paths, key=lambda p: str(p))


def _get_all_relevant_files() -> list[Path]:
    """Return all relevant proof report files under RELEVANT_DIR."""
    if not RELEVANT_DIR.exists():
        return []
    return sorted(
        [p for p in RELEVANT_DIR.rglob("*.md") if _is_relevant(p)],
        key=lambda p: str(p),
    )


# ── Self-test mode ────────────────────────────────────────────────────────────

_SELF_TEST_VALID_REAL_ENGINE = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Self-Test Real Engine Proof

**Classification: REAL_ENGINE**

## Engine Mode

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

## Audio Artifact

| Size | 186,956 bytes (182.6 KiB) |
| RIFF header | 52 49 46 46 = RIFF / WAVE |

## Library Evidence

Library asset id: abc123

## Timeline Evidence

timeline revision 1→2 clip def456

## Explicit Non-Claims

- not runtime FULL PASS
- not operator proof
"""

_SELF_TEST_VALID_STUB_ENGINE = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->
# Self-Test Stub Engine Proof

**Classification: STUB_ENGINE**

VOICESTUDIO_TEST_MODE=1

## Non-Claims

- not real synthesis
- not REAL_ENGINE
"""

_SELF_TEST_VALID_MOCK_ENGINE = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: MOCK_ENGINE
proof_type: generated_audio
engine_mode_source: mock_fixture
runtime_claim: false
operator_claim: false
-->
# Self-Test Mock Engine Proof

**Classification: MOCK_ENGINE**

## Non-Claims

- orchestration proof only
"""

_SELF_TEST_VALID_UNKNOWN = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: UNKNOWN
proof_type: voice_synthesis
engine_mode_source: blocked_unknown
runtime_claim: false
operator_claim: false
-->
# Self-Test Unknown Blocker

**Classification: UNKNOWN**

Blocked: could not determine engine mode — backend was unreachable.

## Non-Claims

- not real synthesis
"""

_SELF_TEST_MISSING_METADATA = """\
# Self-Test Missing Metadata

**Classification: STUB_ENGINE**

## Non-Claims

- not real synthesis
"""

_SELF_TEST_STUB_CLAIMING_REAL = """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->
# Self-Test Stub Claiming Real

**Classification: STUB_ENGINE**

runtime FULL PASS confirmed.

## Non-Claims

- stub only
"""

_SELF_TEST_CASES: list[tuple[str, str, list[str], bool]] = [
    # (name, content, expected_rule_subset, should_pass)
    ("valid_real_engine", _SELF_TEST_VALID_REAL_ENGINE, [], True),
    ("valid_stub_engine", _SELF_TEST_VALID_STUB_ENGINE, [], True),
    ("valid_mock_engine", _SELF_TEST_VALID_MOCK_ENGINE, [], True),
    ("valid_unknown", _SELF_TEST_VALID_UNKNOWN, [], True),
    ("missing_metadata", _SELF_TEST_MISSING_METADATA, ["MISSING_METADATA_BLOCK"], False),
    ("stub_claiming_real", _SELF_TEST_STUB_CLAIMING_REAL, ["NON_REAL_REPORT_CLAIMS_REAL_SYNTHESIS"], False),
]


def run_self_test() -> int:
    """Run built-in self-test examples. Exit 0 if all expected outcomes match."""
    import os

    failures: list[str] = []
    with tempfile.TemporaryDirectory() as tmpdir:
        proof_dir = Path(tmpdir) / "docs" / "reports" / "verification"
        proof_dir.mkdir(parents=True)
        for name, content, expected_rules, should_pass in _SELF_TEST_CASES:
            fname = f"GENERATED_AUDIO_{name.upper()}_SELF_TEST.md"
            path = proof_dir / fname
            path.write_text(content, encoding="utf-8")
            violations = validate_report(path)
            actual_pass = len(violations) == 0
            if actual_pass != should_pass:
                failures.append(
                    f"SELF_TEST FAIL [{name}]: expected {'PASS' if should_pass else 'FAIL'}, "
                    f"got {'PASS' if actual_pass else 'FAIL'}. "
                    f"Violations: {[v.rule for v in violations]}"
                )
            elif not should_pass and expected_rules:
                actual_rules = {v.rule for v in violations}
                for rule in expected_rules:
                    if rule not in actual_rules:
                        failures.append(
                            f"SELF_TEST FAIL [{name}]: expected rule '{rule}' "
                            f"not found. Got: {sorted(actual_rules)}"
                        )

    if failures:
        for f in failures:
            print(f, file=sys.stderr)
        print(
            f"\nSelf-test: {len(failures)} failure(s) — validator logic error",
            file=sys.stderr,
        )
        return 1

    print(
        f"[voice_synthesis_proof_boundary] Self-test: "
        f"{len(_SELF_TEST_CASES)} example(s) PASS"
    )
    return 0


# ── Main ─────────────────────────────────────────────────────────────────────

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Enforce engine-mode classification in voice synthesis proof reports."
    )
    mode_group = parser.add_mutually_exclusive_group()
    mode_group.add_argument(
        "--changed-from",
        metavar="REF",
        default="origin/main",
        help="Validate only files changed/staged/untracked since REF (default: origin/main)",
    )
    mode_group.add_argument(
        "--all",
        action="store_true",
        help="Validate ALL relevant files under docs/reports/verification/",
    )
    mode_group.add_argument(
        "--self-test-examples",
        action="store_true",
        help="Run built-in self-test examples and exit",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results as JSON to stdout",
    )
    args = parser.parse_args(argv)

    if args.self_test_examples:
        return run_self_test()

    # ── Gather files to check ─────────────────────────────────────────────────
    if args.all:
        files_to_check = _get_all_relevant_files()
        mode_desc = "all"
    else:
        changed = _get_changed_files(args.changed_from)
        if changed is None:
            msg = (
                f"[ADVISORY] voice_synthesis_proof_boundary: git unavailable or "
                f"ref '{args.changed_from}' not found — skipping changed-file check"
            )
            print(msg, file=sys.stderr)
            if args.json:
                print(json.dumps({"status": "advisory", "message": msg, "violations": []}))
            return 0
        files_to_check = [
            p for p in changed
            if _is_relevant(p)
            and p.suffix.lower() == ".md"
            and "docs/reports/verification" in str(p).replace("\\", "/")
        ]
        mode_desc = f"changed from {args.changed_from}"

    # ── Validate ──────────────────────────────────────────────────────────────
    all_violations: list[Violation] = []
    checked: list[str] = []

    for path in files_to_check:
        if not path.exists():
            continue
        violations = validate_report(path)
        checked.append(_rel(path))
        all_violations.extend(violations)

    # ── Output ────────────────────────────────────────────────────────────────
    if args.json:
        output = {
            "status": "pass" if not all_violations else "fail",
            "mode": mode_desc,
            "checked": checked,
            "violations": [
                {
                    "file": v.file,
                    "rule": v.rule,
                    "detail": v.detail,
                    "fix": v.fix,
                }
                for v in all_violations
            ],
        }
        print(json.dumps(output, indent=2))
    else:
        if checked:
            print(
                f"[voice_synthesis_proof_boundary] Checked {len(checked)} report(s) ({mode_desc}):"
            )
            for f in checked:
                print(f"  {f}")
        else:
            print(
                f"[voice_synthesis_proof_boundary] No relevant proof reports found ({mode_desc}) — PASS"
            )

    if all_violations:
        print(
            "\nPROOF BOUNDARY VIOLATIONS:",
            file=sys.stderr,
        )
        for v in all_violations:
            print(f"\nFAIL {v.file}: {v.detail}", file=sys.stderr)
            print(f"  Rule:  {v.rule}", file=sys.stderr)
            print(f"  Fix:   {v.fix}", file=sys.stderr)
        print(
            "\nEach voice synthesis / generated-audio proof report must:\n"
            "  1. Include a VOICESTUDIO_PROOF_BOUNDARY_V1 metadata block\n"
            "  2. Declare exactly one of: REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN\n"
            "  3. Include an Explicit Non-Claims section\n"
            "See docs/developer/VOICE_SYNTHESIS_PROOF_REPORTING_STANDARD.md for guidance.",
            file=sys.stderr,
        )
        return 1

    if checked:
        print(f"[voice_synthesis_proof_boundary] All {len(checked)} report(s) PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
