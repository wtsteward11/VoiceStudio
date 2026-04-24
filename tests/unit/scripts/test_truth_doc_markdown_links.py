"""Resolve internal markdown links from truth-surface + authority docs (67).

Allowlist: verification truth docs, parity matrix, session STATE, and the
canonical registry. Skips ``http``/``https``/``mailto``, fragment-only targets.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

_REPO_ROOT = Path(__file__).resolve().parents[3]

# Repo-relative markdown sources (narrow allowlist).
_TRUTH_MARKDOWN_REL_PATHS = (
    ".cursor/STATE.md",
    "docs/governance/CANONICAL_REGISTRY.md",
    "docs/reports/verification/generated/README.md",
    "docs/reports/verification/PROOF_SLICE30_ENGINE_TRUTH_JSON.md",
    "docs/reports/verification/slice27/README.md",
    "docs/reports/verification/PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md",
    "docs/reports/verification/ENGINE_PARITY_MATRIX.md",
)

_LINK_RE = re.compile(r"\]\(([^)]+)\)")


def _optional_verify_report_missing(repo_root: Path, resolved: Path) -> bool:
    """Historic ``artifacts/verify/*/verification_report.md`` may be absent locally."""
    verify_root = (repo_root / "artifacts" / "verify").resolve()
    try:
        resolved.relative_to(verify_root)
    except ValueError:
        return False
    return resolved.name == "verification_report.md" and not resolved.is_file()


def _optional_stt_pack_summary_missing(repo_root: Path, resolved: Path) -> bool:
    """``stt_hardening_regress_summary.json`` is written at end of ``stt_hardening_regress.ps1``."""
    gen = (repo_root / "docs" / "reports" / "verification" / "generated").resolve()
    try:
        resolved.relative_to(gen)
    except ValueError:
        return False
    return resolved.name == "stt_hardening_regress_summary.json" and not resolved.is_file()


def _iter_markdown_link_targets(md_text: str) -> list[str]:
    out: list[str] = []
    for raw in _LINK_RE.findall(md_text):
        t = raw.strip()
        if not t or t.startswith("#"):
            continue
        path_part = t.split("#", 1)[0].strip()
        if not path_part:
            continue
        lowered = path_part.lower()
        if lowered.startswith(("http://", "https://", "mailto:")):
            continue
        out.append(path_part)
    return out


def test_truth_doc_allowlist_includes_state_and_canonical_registry() -> None:
    """Task 76 — authority surfaces must remain in the link contract (regression guard)."""
    assert ".cursor/STATE.md" in _TRUTH_MARKDOWN_REL_PATHS
    assert "docs/governance/CANONICAL_REGISTRY.md" in _TRUTH_MARKDOWN_REL_PATHS


@pytest.mark.parametrize("rel_path", _TRUTH_MARKDOWN_REL_PATHS)
def test_truth_doc_internal_links_resolve(rel_path: str) -> None:
    source = _REPO_ROOT / rel_path
    assert source.is_file(), f"missing source: {rel_path}"
    text = source.read_text(encoding="utf-8")
    repo_resolved = _REPO_ROOT.resolve()
    for target in _iter_markdown_link_targets(text):
        resolved = (source.parent / target).resolve()
        if _optional_verify_report_missing(repo_resolved, resolved):
            continue
        if _optional_stt_pack_summary_missing(repo_resolved, resolved):
            continue
        assert resolved.is_file(), (
            f"broken link in {rel_path}: ({target!r}) -> {resolved}"
        )
        assert resolved.is_relative_to(repo_resolved), (
            f"link escapes repo in {rel_path}: {target!r} -> {resolved}"
        )
