"""
CI gate: enforce workflow strictness contract.

Reads .ci/STRICTNESS_CONTRACT.md as SSOT for gate jobs, validates that no gate job
has unallowlisted continue-on-error or || true on steps.
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parent.parent.parent
CONTRACT_PATH = ROOT / ".ci" / "STRICTNESS_CONTRACT.md"
WORKFLOWS_DIR = ROOT / ".github" / "workflows"


def _parse_gate_jobs_from_contract() -> set[tuple[str, str]]:
    """Parse gate job (workflow, job_id) from STRICTNESS_CONTRACT.md."""
    if not CONTRACT_PATH.exists():
        pytest.fail(f"Contract missing: {CONTRACT_PATH}")
    text = CONTRACT_PATH.read_text(encoding="utf-8")
    jobs = set()
    in_table = False
    for line in text.splitlines():
        if "| ci.yml |" in line or "| build.yml |" in line or "| test.yml |" in line:
            parts = [p.strip() for p in line.split("|")]
            if len(parts) >= 3 and parts[1] and parts[2]:
                wf = parts[1]
                job_id = parts[2]
                if wf.endswith(".yml") and job_id:
                    jobs.add((wf, job_id))
    return jobs


def _load_workflow(path: Path) -> dict:
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def _step_has_suppression(step: dict) -> bool:
    if step.get("continue-on-error") is True:
        return True
    run = step.get("run", "")
    if isinstance(run, str) and re.search(r"\|\|\s*(true|echo\s+[\"']::warning::)", run):
        return True
    return False


def _get_allowlist() -> set[tuple[str, str, str]]:
    """Import allowlist from suppression guard (single source)."""
    from tests.ci.test_ci_suppression_guard import ALLOWED_SUPPRESSIONS

    return set(ALLOWED_SUPPRESSIONS.keys())


def test_contract_exists() -> None:
    """STRICTNESS_CONTRACT.md must exist."""
    assert CONTRACT_PATH.exists(), f"Missing {CONTRACT_PATH}"


def test_gate_jobs_no_unallowlisted_suppressions() -> None:
    """Gate jobs from contract must not have unallowlisted suppressions."""
    gate_jobs = _parse_gate_jobs_from_contract()
    allowlist = _get_allowlist()

    violations = []
    for wf_path in WORKFLOWS_DIR.glob("*.yml"):
        wf = _load_workflow(wf_path)
        wf_name = wf_path.name
        for job_id, job_spec in wf.get("jobs", {}).items():
            if (wf_name, job_id) not in gate_jobs:
                continue
            if not isinstance(job_spec, dict):
                continue
            for step in job_spec.get("steps", []):
                if not isinstance(step, dict):
                    continue
                step_name = step.get("name", "")
                if _step_has_suppression(step):
                    key = (wf_name, job_id, step_name)
                    if key not in allowlist:
                        violations.append(f"{wf_name} / {job_id} / {step_name}")

    assert not violations, (
        "Gate jobs have unallowlisted suppressions. "
        "Add to ALLOWED_SUPPRESSIONS in test_ci_suppression_guard.py or remove suppression.\n"
        + "\n".join(violations)
    )
