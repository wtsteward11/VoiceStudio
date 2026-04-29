#!/usr/bin/env python3
"""
CI check: enforce engine-mode classification in voice synthesis proof reports.

Every new or changed proof report whose filename matches:
  VOICE_SYNTHESIS*.md, GENERATED_AUDIO*.md, REAL_ENGINE_GENERATED_AUDIO*.md
under docs/reports/verification/ must declare exactly one of:
  REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN

REAL_ENGINE reports must include artifact-validation evidence terms.
STUB_ENGINE / MOCK_ENGINE / UNKNOWN reports must not claim real synthesis.

Usage:
  python scripts/ci/check_voice_synthesis_proof_boundary.py
      (changed-from mode vs origin/main — default, for CI)
  python scripts/ci/check_voice_synthesis_proof_boundary.py --changed-from origin/main
  python scripts/ci/check_voice_synthesis_proof_boundary.py --all
  python scripts/ci/check_voice_synthesis_proof_boundary.py --json
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

# Regex to find classification tokens in context like:
#   Classification: REAL_ENGINE
#   **Classification: REAL_ENGINE**
#   VERDICT: REAL_ENGINE
#   engine_mode: REAL_ENGINE
#   - **VERDICT:** REAL_ENGINE
#   | Classification | REAL_ENGINE |
#   Classification Summary ... REAL_ENGINE
_CLASS_LABEL = re.compile(
    r"(?:classification|verdict|engine.?mode|ENGINE.?MODE)\s*[:\|]?\s*\**\s*(REAL_ENGINE|STUB_ENGINE|MOCK_ENGINE|UNKNOWN)\b",
    re.IGNORECASE,
)
# Also allow a bare token on a line (e.g. "VERDICT: REAL_ENGINE" already covered,
# but also lines like "**VERDICT: REAL_ENGINE**" or table cells "| REAL_ENGINE |")
_BARE_CLASS_TOKEN = re.compile(
    r"\b(REAL_ENGINE|STUB_ENGINE|MOCK_ENGINE|UNKNOWN)\b"
)

# Required evidence terms for REAL_ENGINE reports (grouped as alternatives)
_REAL_ENGINE_EVIDENCE: list[tuple[str, list[str]]] = [
    # Routed engine field exists and names a non-stub engine
    ("routed_engine evidence (non-stub routed_engine value)",
     ["routed_engine"]),
    # Artifact size evidence
    ("audio artifact size evidence (bytes/KiB/MiB)",
     ["bytes", "KiB", "MiB", " B)"]),
    # Artifact format validation
    ("audio artifact format validation (RIFF/WAV/WAVE/header)",
     ["RIFF", "WAV", "WAVE", "header"]),
    # Library evidence
    ("library evidence",
     ["library", "Library", "asset", "Asset"]),
    # Timeline evidence
    ("timeline evidence (or explicit non-claim)",
     ["timeline", "Timeline", "revision", "clip", "Clip"]),
]

# Phrases that indicate a false real-synthesis claim in non-REAL_ENGINE reports.
# These are only checked OUTSIDE of explicit non-claims sections.
_FORBIDDEN_REAL_CLAIM_PHRASES: list[re.Pattern[str]] = [
    re.compile(r"REAL_ENGINE\s+confirmed", re.IGNORECASE),
    re.compile(r"real\s+synthesis\s+proof", re.IGNORECASE),
    re.compile(r"real\s+engine\s+generated\s+audio\s+proof", re.IGNORECASE),
    re.compile(r"actual\s+model\s+output\s+confirmed", re.IGNORECASE),
]

# Heading patterns that start a non-claims section; the section ends at next ##-level heading.
_NON_CLAIMS_HEADING = re.compile(
    r"^#{1,6}\s+.*(?:non.?claim|not\s+a\s+claim|explicit\s+non)",
    re.IGNORECASE | re.MULTILINE,
)
_HEADING = re.compile(r"^#{1,6}\s+", re.MULTILINE)


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
        # Find next same-or-higher-level heading or end of text
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
    # Also scan for context-free bare tokens in classification-summary blocks
    # (e.g. the "Classification Summary" code block at end of real-engine report)
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


def validate_report(path: Path) -> list[Violation]:
    """Validate a single proof report. Return list of Violation (empty = pass)."""
    violations: list[Violation] = []
    rel = _rel(path)

    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError as e:
        return [Violation(rel, "FILE_READ", str(e), "Ensure file is readable")]

    # ── 1. Classification detection ──────────────────────────────────────────
    classifications = _find_classifications(text)

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

    # ── 2. REAL_ENGINE: require evidence terms ───────────────────────────────
    if classification == "REAL_ENGINE":
        for desc, terms in _REAL_ENGINE_EVIDENCE:
            if not any(term in text for term in terms):
                violations.append(Violation(
                    file=rel,
                    rule="REAL_ENGINE_MISSING_EVIDENCE",
                    detail=f"Missing {desc} — none of {terms!r} found in report body",
                    fix=f"Add a section documenting {desc} (include one of: {', '.join(terms)})",
                ))
        # Check that routed_engine value is not exclusively stub/mock/test
        if "routed_engine" in text:
            stub_only = re.search(
                r'routed.?engine["\s:=]*"?(?:stub|mock|test)"?',
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
                    detail='routed_engine shows stub/mock/test value — not valid for REAL_ENGINE classification',
                    fix=(
                        "Change Classification to STUB_ENGINE or MOCK_ENGINE, "
                        "or confirm routed_engine = xtts_v2 / piper / etc."
                    ),
                ))

    # ── 3. STUB / MOCK / UNKNOWN: forbid real-synthesis claims ───────────────
    elif classification in ("STUB_ENGINE", "MOCK_ENGINE", "UNKNOWN"):
        searchable = _strip_non_claims_sections(text)
        for pat in _FORBIDDEN_REAL_CLAIM_PHRASES:
            m = pat.search(searchable)
            if m:
                # Find approximate line number
                line_no = text.count("\n", 0, m.start()) + 1
                violations.append(Violation(
                    file=rel,
                    rule="STUB_CLAIMS_REAL_SYNTHESIS",
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


def _get_changed_files(ref: str) -> list[Path] | None:
    """Return list of added/modified files since ref, or None if git unavailable."""
    try:
        result = subprocess.run(
            ["git", "diff", "--name-only", "--diff-filter=ACM", f"{ref}..HEAD"],
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


def _get_all_relevant_files() -> list[Path]:
    """Return all relevant proof report files under RELEVANT_DIR."""
    if not RELEVANT_DIR.exists():
        return []
    return [p for p in RELEVANT_DIR.rglob("*.md") if _is_relevant(p)]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Enforce engine-mode classification in voice synthesis proof reports."
    )
    group = parser.add_mutually_exclusive_group()
    group.add_argument(
        "--changed-from",
        metavar="REF",
        default="origin/main",
        help="Validate only files changed since REF (default: origin/main)",
    )
    group.add_argument(
        "--all",
        action="store_true",
        help="Validate ALL relevant files under docs/reports/verification/",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results as JSON to stdout",
    )
    args = parser.parse_args(argv)

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
            and str(p).replace("\\", "/").find("docs/reports/verification") != -1
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
            "\nEach voice synthesis / generated-audio proof report must declare exactly one of:\n"
            "  REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN\n"
            "See docs/reports/verification/REAL_ENGINE_GENERATED_AUDIO_PROOF_2026-04-29.md for a compliant example.",
            file=sys.stderr,
        )
        return 1

    if checked:
        print(f"[voice_synthesis_proof_boundary] All {len(checked)} report(s) PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
