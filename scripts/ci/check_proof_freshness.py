#!/usr/bin/env python3
"""CI check: voice synthesis / product-closure proof JSON must be current-HEAD honest.

This validator enforces that committed proof artifacts do not silently pretend to be
"current" while pointing at an older `git.head`, and that dirty working trees are not
used to mint "clean" proofs unless explicitly allowed.

Rules (non-exhaustive):
  - STALE_PROOF_HEAD: `git.head` exists but does not match `git rev-parse HEAD`
  - DIRTY_PROOF_NOT_ALLOWED: `git.dirty_summary` is not clean unless `--allow-dirty-proof`
  - HISTORICAL_PROOF_NOT_CURRENT_HEAD: `historical: true` but `git.head` matches current HEAD
  - MISSING_GIT_HEAD: expected `git.head` / git section missing for proof JSON objects
  - INVALID_PROOF_JSON: unreadable JSON or top-level not an object

Modes:
  --proof-json PATH
  --dir DIR
  --changed-from REF   (union: ref..HEAD, staged, unstaged, untracked under docs/reports/verification/**/*.json)
  --json               (machine-readable result to stdout)
  --allow-dirty-proof  (explicit opt-in for dirty-summary proofs)
  --self-test-examples   (internal fixture checks; writes temp files)

Exit codes:
  0 = pass
  1 = violations / self-test failure
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable

ROOT = Path(__file__).resolve().parent.parent.parent


@dataclass(frozen=True)
class Violation:
    file: str
    rule: str
    field: str
    detail: str
    fix: str
    recorded_head: str | None
    current_head: str | None


def _rel(path: Path) -> str:
    try:
        return str(path.relative_to(ROOT)).replace("\\", "/")
    except ValueError:
        return str(path)


def _run_git(args: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def current_git_head() -> tuple[str | None, list[Violation]]:
    cp = _run_git(["rev-parse", "HEAD"])
    if cp.returncode != 0:
        return None, [
            Violation(
                "<repo>",
                "MISSING_GIT_HEAD",
                "$",
                f"Unable to read current HEAD via git: {cp.stderr.strip()}",
                "Run inside a git checkout with a valid HEAD",
                None,
                None,
            )
        ]
    head = cp.stdout.strip()
    if not head:
        return None, [
            Violation(
                "<repo>",
                "MISSING_GIT_HEAD",
                "$",
                "git rev-parse HEAD returned empty output",
                "Fix repository corruption / shallow checkout issues",
                None,
                None,
            )
        ]
    return head, []


def _is_proofish_json(path: Path) -> bool:
    if path.suffix.lower() != ".json":
        return False
    norm = str(path).replace("\\", "/").lower()
    return "/docs/reports/verification/" in f"/{norm}"


def _git_names(args: list[str]) -> list[Path]:
    cp = _run_git(args)
    if cp.returncode != 0:
        return []
    return [ROOT / line.strip() for line in cp.stdout.splitlines() if line.strip()]


def changed_proof_json_files(ref: str) -> list[Path]:
    """Union discovery for proof-ish JSON under docs/reports/verification/."""
    seen: dict[str, Path] = {}
    commands = [
        ["diff", "--name-only", "--diff-filter=ACM", f"{ref}..HEAD"],
        ["diff", "--name-only", "--cached", "--diff-filter=ACM"],
        ["diff", "--name-only", "--diff-filter=ACM"],
        ["ls-files", "--others", "--exclude-standard", "docs/reports/verification/"],
    ]
    for cmd in commands:
        for p in _git_names(cmd):
            if _is_proofish_json(p):
                seen[str(p.resolve())] = p
    return sorted(seen.values(), key=lambda p: str(p))


def _load_json(path: Path) -> tuple[dict[str, Any] | None, list[Violation]]:
    rel = _rel(path)
    try:
        raw = path.read_text(encoding="utf-8")
    except OSError as exc:
        return None, [
            Violation(rel, "INVALID_PROOF_JSON", "$", f"Cannot read file: {exc}", "Ensure the proof file exists and is readable", None, None)
        ]
    try:
        data = json.loads(raw)
    except json.JSONDecodeError as exc:
        return None, [
            Violation(rel, "INVALID_PROOF_JSON", "$", f"JSON parse error: {exc}", "Emit valid JSON", None, None)
        ]
    if not isinstance(data, dict):
        return None, [
            Violation(rel, "INVALID_PROOF_JSON", "$", "Top-level JSON must be an object", "Emit a JSON object proof envelope", None, None)
        ]
    return data, []


def validate_proof_freshness(
    path: Path,
    *,
    current_head: str,
    allow_dirty_proof: bool,
) -> list[Violation]:
    rel = _rel(path)
    data, viol = _load_json(path)
    if data is None:
        return viol

    # Only enforce freshness for voice synthesis proof schema objects.
    if data.get("schema_version") != "voice_synthesis_proof.v1":
        return []

    git_obj = data.get("git")
    if not isinstance(git_obj, dict):
        return [
            Violation(
                rel,
                "MISSING_GIT_HEAD",
                "$.git",
                "Proof JSON is missing a `git` object",
                "Populate `git.head`, `git.origin_main`, and `git.dirty_summary` from the harness",
                None,
                current_head,
            )
        ]

    recorded = git_obj.get("head")
    if not isinstance(recorded, str) or not recorded.strip():
        return [
            Violation(
                rel,
                "MISSING_GIT_HEAD",
                "$.git.head",
                "`git.head` is missing or not a non-empty string",
                "Record the full 40-char commit SHA from `git rev-parse HEAD` at proof generation time",
                None,
                current_head,
            )
        ]
    recorded = recorded.strip()

    dirty_summary = git_obj.get("dirty_summary")
    dirty_text = str(dirty_summary).strip() if dirty_summary is not None else ""
    is_clean = dirty_text == "" or dirty_text.lower() == "clean"
    if not is_clean and not allow_dirty_proof:
        return [
            Violation(
                rel,
                "DIRTY_PROOF_NOT_ALLOWED",
                "$.git.dirty_summary",
                f"Dirty working tree recorded in proof ({dirty_text!r})",
                "Regenerate proof from a clean tree, or pass `--allow-dirty-proof` with an explicit non-claim in the proof bundle",
                recorded,
                current_head,
            )
        ]

    historical = data.get("historical") is True
    if historical:
        if recorded == current_head:
            return [
                Violation(
                    rel,
                    "HISTORICAL_PROOF_NOT_CURRENT_HEAD",
                    "$.historical",
                    "`historical: true` but `git.head` matches current HEAD (contradiction)",
                    "Clear `historical` or record the correct historical commit SHA in `git.head`",
                    recorded,
                    current_head,
                )
            ]
        return []

    if recorded != current_head:
        return [
            Violation(
                rel,
                "STALE_PROOF_HEAD",
                "$.git.head",
                f"Proof records HEAD {recorded} but repo HEAD is {current_head}",
                "Regenerate proof JSON on current HEAD or mark `historical: true` with honest recorded head + non-claims",
                recorded,
                current_head,
            )
        ]

    return []


def _result_payload(status: str, mode: str, checked: list[Path], violations: list[Violation]) -> dict[str, Any]:
    return {
        "status": status,
        "mode": mode,
        "checked": [_rel(p) for p in checked],
        "violations": [asdict(v) for v in violations],
    }


def run_self_test() -> int:
    head, errs = current_git_head()
    if not head or errs:
        print("[proof_freshness] self-test cannot run: missing current HEAD", file=sys.stderr)
        return 1

    failures: list[str] = []

    def case(name: str, payload: dict[str, Any], *, allow_dirty: bool, expect_rules: list[str] | None, should_pass: bool) -> None:
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / f"{name}.json"
            p.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
            v = validate_proof_freshness(p, current_head=head, allow_dirty_proof=allow_dirty)
            rules = [x.rule for x in v]
            if should_pass and v:
                failures.append(f"{name}: expected PASS, got {rules}")
            if not should_pass:
                if not v:
                    failures.append(f"{name}: expected FAIL, got PASS")
                elif expect_rules:
                    missing = [r for r in expect_rules if r not in rules]
                    if missing:
                        failures.append(f"{name}: expected rules {expect_rules}, got {rules}")

    base_git = {"head": head, "origin_main": "0" * 40, "dirty_summary": "clean"}

    case(
        "pass_clean_current",
        {"schema_version": "voice_synthesis_proof.v1", "git": dict(base_git)},
        allow_dirty=False,
        expect_rules=None,
        should_pass=True,
    )

    case(
        "fail_stale_head",
        {"schema_version": "voice_synthesis_proof.v1", "git": {**base_git, "head": "0" * 40}},
        allow_dirty=False,
        expect_rules=["STALE_PROOF_HEAD"],
        should_pass=False,
    )

    case(
        "fail_dirty_not_allowed",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "git": {**base_git, "dirty_summary": "M file.txt"},
        },
        allow_dirty=False,
        expect_rules=["DIRTY_PROOF_NOT_ALLOWED"],
        should_pass=False,
    )

    case(
        "pass_dirty_allowed",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "git": {**base_git, "dirty_summary": "M file.txt"},
        },
        allow_dirty=True,
        expect_rules=None,
        should_pass=True,
    )

    case(
        "fail_historical_contradiction",
        {"schema_version": "voice_synthesis_proof.v1", "historical": True, "git": dict(base_git)},
        allow_dirty=False,
        expect_rules=["HISTORICAL_PROOF_NOT_CURRENT_HEAD"],
        should_pass=False,
    )

    case(
        "pass_historical_old_head",
        {"schema_version": "voice_synthesis_proof.v1", "historical": True, "git": {**base_git, "head": "0" * 40}},
        allow_dirty=False,
        expect_rules=None,
        should_pass=True,
    )

    case(
        "fail_missing_git",
        {"schema_version": "voice_synthesis_proof.v1"},
        allow_dirty=False,
        expect_rules=["MISSING_GIT_HEAD"],
        should_pass=False,
    )

    # Invalid JSON case (not via the `case` helper since we need raw broken content).
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "bad.json"
        p.write_text("{", encoding="utf-8")
        v = validate_proof_freshness(p, current_head=head, allow_dirty_proof=False)
        if not any(x.rule == "INVALID_PROOF_JSON" for x in v):
            failures.append("invalid_json: expected INVALID_PROOF_JSON")

    if failures:
        for f in failures:
            print(f"[proof_freshness] SELF-TEST FAIL: {f}", file=sys.stderr)
        return 1

    print("[proof_freshness] Self-test: cases PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--proof-json", type=Path)
    group.add_argument("--dir", type=Path)
    group.add_argument("--changed-from", dest="changed_from", default=None)
    parser.add_argument("--json", action="store_true", dest="json_output")
    parser.add_argument("--allow-dirty-proof", action="store_true")
    parser.add_argument("--self-test-examples", action="store_true")
    args = parser.parse_args(argv)

    if args.self_test_examples:
        rc = run_self_test()
        if args.json_output:
            print(json.dumps({"status": "pass" if rc == 0 else "fail", "mode": "self-test"}, indent=2))
        return rc

    head, head_errs = current_git_head()
    if not head:
        payload = _result_payload("fail", "head", [], head_errs)
        if args.json_output:
            print(json.dumps(payload, indent=2))
        else:
            for v in head_errs:
                print(f"FAIL {v.rule}: {v.detail}", file=sys.stderr)
        return 1

    files: list[Path] = []
    mode = ""
    if args.proof_json:
        files = [args.proof_json]
        mode = f"proof-json {_rel(args.proof_json)}"
    elif args.dir:
        mode = f"dir {_rel(args.dir)}"
        if not args.dir.exists():
            payload = _result_payload("fail", mode, [], [Violation(_rel(args.dir), "INVALID_PROOF_JSON", "$", "Dir does not exist", "Fix path", None, head)])
            print(json.dumps(payload, indent=2) if args.json_output else payload, file=sys.stderr if not args.json_output else sys.stdout)
            return 1
        for p in sorted(args.dir.rglob("*.json")):
            if p.is_file() and _is_proofish_json(p):
                files.append(p)
    else:
        ref = args.changed_from or "origin/main"
        files = changed_proof_json_files(ref)
        mode = f"changed-from {ref}"

    violations: list[Violation] = []
    for path in files:
        violations.extend(validate_proof_freshness(path, current_head=head, allow_dirty_proof=args.allow_dirty_proof))

    status = "fail" if violations else "pass"
    payload = _result_payload(status, mode, files, violations)
    if args.json_output:
        print(json.dumps(payload, indent=2))
    elif violations:
        print("PROOF FRESHNESS VIOLATIONS:", file=sys.stderr)
        for v in violations:
            print(
                f"FAIL {v.file}: {v.rule} ({v.field}) recorded={v.recorded_head!r} current={v.current_head!r}: {v.detail} | fix: {v.fix}",
                file=sys.stderr,
            )
    else:
        print(f"[proof_freshness] PASS mode={mode} files={len(files)} head={head}")

    return 1 if violations else 0


if __name__ == "__main__":
    raise SystemExit(main())
