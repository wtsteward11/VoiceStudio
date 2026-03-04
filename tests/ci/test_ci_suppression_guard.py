"""
CI gate: fail if forbidden suppressions exist in protected workflows.

Suppressions: continue-on-error: true, || true, || echo "::warning::"
Gate jobs must NOT have unallowlisted suppressions.
Advisory jobs may have suppressions only when explicitly allowlisted.

Policy: no-suppression.mdc forbids code-level suppression. CI workflow-level
diagnostic masking is allowed ONLY when labeled in ALLOWED_SUPPRESSIONS.
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parent.parent.parent
WORKFLOWS = [ROOT / ".github" / "workflows" / "ci.yml", ROOT / ".github" / "workflows" / "build.yml", ROOT / ".github" / "workflows" / "test.yml"]

# (workflow_basename, job_id, step_name) -> category label
ALLOWED_SUPPRESSIONS = {
    ("ci.yml", "python-tests", "Run linting"): "advisory:formatting",
    ("ci.yml", "python-tests", "Run type checking"): "advisory:type-diagnostics",
    ("ci.yml", "security-scan", "Check dependencies for vulnerabilities"): "advisory:vuln-scan",
    ("ci.yml", "security-scan", "Run Bandit security scanner"): "advisory:static-analysis",
    ("ci.yml", "code-quality", "Check formatting with Black"): "advisory:formatting",
    ("ci.yml", "code-quality", "Check imports with isort"): "advisory:formatting",
    ("ci.yml", "code-quality", "Lint with Ruff"): "advisory:formatting",
    ("ci.yml", "performance-tests", "Run performance benchmarks"): "advisory:perf-regression",
    ("test.yml", "test-backend", "Upload coverage reports"): "infra:upload",
    ("test.yml", "test-frontend", "Generate coverage report"): "infra:report",
    ("test.yml", "test-frontend", "Display coverage summary"): "infra:report",
    ("test.yml", "test-frontend", "Upload coverage report"): "infra:upload",
    ("test.yml", "test-quality", "Verify no TODO/FIXME in code"): "advisory:placeholder-check",
    ("test.yml", "e2e-full-app", "Install WinAppDriver"): "infra:tool-install",
    ("test.yml", "performance-tests", "Run performance tests"): "advisory:perf",
    ("test.yml", "coverage-gate", "Generate coverage badge data"): "infra:report",
    ("test.yml", "security-scan", "Run pip-audit"): "advisory:vuln-scan",
    ("test.yml", "security-scan", "Run safety check"): "advisory:vuln-scan",
    ("test.yml", "security-scan", "Run NuGet vulnerability scan"): "advisory:vuln-scan",
    ("test.yml", "nightly-ui-automation", "Install FlaUI"): "infra:tool-install",
    ("test.yml", "nightly-ui-automation", "Install Python Test Dependencies"): "infra:install",
    ("test.yml", "nightly-ui-automation", "Start WinAppDriver"): "infra:tool-start",
    ("test.yml", "nightly-ui-automation", "Run Panel UI Tests"): "advisory:nightly",
    ("test.yml", "nightly-ui-automation", "Run Smoke UI Tests"): "advisory:nightly",
    ("test.yml", "nightly-ui-automation", "Run UI Automation Coverage Check"): "advisory:nightly",
    ("build.yml", "build-frontend", "Install MSBuild Structured Log Viewer CLI"): "infra:tool-install",
    ("build.yml", "build-frontend", "Verify StructuredLogger CLI installation"): "infra:tool-verify",
    ("build.yml", "build-frontend", "Extract binlog metrics (proactive analysis)"): "diagnostic:binlog",
    ("build.yml", "build-frontend", "Analyze binlog on build failure"): "diagnostic:binlog",
    ("build.yml", "build-frontend", "Post XAML diagnostic comment to PR"): "diagnostic:pr-comment",
    ("build.yml", "build-frontend", "RuleGuard (no stubs/placeholders)"): "advisory:stubs",
    ("build.yml", "build-frontend", "Log Build Results to Audit System"): "diagnostic:audit",
    ("build.yml", "build-backend", "Install dependencies"): "infra:optional-deps",
    ("build.yml", "validate-contracts", "Install NSwag CLI"): "infra:tool-install",
    ("build.yml", "validate-contracts", "Download OpenAPI schema artifact"): "infra:artifact-fallback",
    ("build.yml", "validate-contracts", "Detect breaking changes"): "diagnostic:breaking",
    ("build.yml", "quality-scorecard", "Generate Quality Scorecard"): "advisory:quality",
    ("build.yml", "quality-scorecard", "Check quality threshold"): "advisory:quality",
    ("build.yml", "regression-detection", "Download quality scorecard"): "infra:artifact",
    ("build.yml", "regression-detection", "Run regression detection"): "advisory:regression",
    ("build.yml", "regression-detection", "Report regressions"): "advisory:regression",
}

# Gate jobs: must NOT have unallowlisted suppressions
GATE_JOBS = {
    ("ci.yml", "python-tests"),
    ("ci.yml", "dotnet-build"),
    ("ci.yml", "integration-tests"),
    ("ci.yml", "golden-path"),
    ("ci.yml", "security-scan"),  # partially gate - some steps are advisory
    ("build.yml", "build-frontend"),
    ("build.yml", "build-backend"),
    ("build.yml", "verify-gates"),
    ("build.yml", "validate-contracts"),
    ("test.yml", "test-backend"),
    ("test.yml", "test-frontend"),
    ("test.yml", "verify-gates"),
}


def _load_workflow(path: Path) -> dict:
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def _step_has_continue_on_error(step: dict) -> bool:
    return step.get("continue-on-error") is True


def _run_has_suppression(run: str) -> bool:
    if not run:
        return False
    return bool(re.search(r"\|\|\s*(true|echo\s+[\"']::warning::)", run))


def _get_step_run(step: dict) -> str:
    run = step.get("run", "")
    if isinstance(run, str):
        return run
    return ""


def collect_suppressions() -> list[tuple[str, str, str, str]]:
    """Return list of (workflow, job_id, step_name, suppression_type)."""
    results = []
    for wf_path in WORKFLOWS:
        if not wf_path.exists():
            continue
        wf = _load_workflow(wf_path)
        wf_name = wf_path.name
        jobs = wf.get("jobs", {})
        for job_id, job_spec in jobs.items():
            if not isinstance(job_spec, dict):
                continue
            steps = job_spec.get("steps", [])
            for step in steps:
                if not isinstance(step, dict):
                    continue
                step_name = step.get("name", "")
                if _step_has_continue_on_error(step):
                    results.append((wf_name, job_id, step_name, "continue-on-error"))
                run = _get_step_run(step)
                if _run_has_suppression(run):
                    results.append((wf_name, job_id, step_name, "|| true/echo"))
    return results


def test_no_forbidden_suppressions_in_gate_jobs() -> None:
    """Gate jobs must not have unallowlisted suppressions."""
    suppressions = collect_suppressions()
    violations = []
    for wf_name, job_id, step_name, supp_type in suppressions:
        key = (wf_name, job_id, step_name)
        if (wf_name, job_id) in GATE_JOBS and key not in ALLOWED_SUPPRESSIONS:
            violations.append(f"{wf_name} / {job_id} / {step_name}: {supp_type} (not in allowlist)")
    assert not violations, (
        "Gate jobs have unallowlisted suppressions. "
        "Add to ALLOWED_SUPPRESSIONS with category label or remove suppression.\n"
        + "\n".join(violations)
    )


def test_all_allowlist_entries_exist() -> None:
    """Every allowlist entry must reference an existing step."""
    suppressions = collect_suppressions()
    supp_set = {(w, j, s) for w, j, s, _ in suppressions}
    orphaned = [k for k in ALLOWED_SUPPRESSIONS if k not in supp_set]
    assert not orphaned, (
        "ALLOWED_SUPPRESSIONS contains entries for non-existent steps "
        "(stale allowlist): " + ", ".join(f"{w}/{j}/{s}" for w, j, s in orphaned)
    )
