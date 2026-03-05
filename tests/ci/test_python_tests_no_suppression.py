"""
CI gate: python-tests job must not contain forbidden suppressions.

Forbidden: || true, || echo, continue-on-error: true, if: always()
Allowlisted: fail_ci_if_error: false on Codecov (advisory upload).

Policy: python-tests is a real gate; no diagnostic masking.
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parent.parent.parent
CI_YML = ROOT / ".github" / "workflows" / "ci.yml"


def _load_ci() -> dict:
    with open(CI_YML, encoding="utf-8") as f:
        return yaml.safe_load(f)


def _get_python_tests_steps() -> list[dict]:
    wf = _load_ci()
    job = wf.get("jobs", {}).get("python-tests")
    if not job:
        pytest.skip("python-tests job not found in ci.yml")
    return job.get("steps", [])


def test_python_tests_no_shell_suppression() -> None:
    """python-tests steps must not use || true or || echo."""
    steps = _get_python_tests_steps()
    violations = []
    for step in steps:
        name = step.get("name", "")
        run = step.get("run", "")
        if not isinstance(run, str):
            continue
        if re.search(r"\|\|\s*true\b", run):
            violations.append(f"{name}: contains '|| true'")
        if re.search(r'\|\|\s*echo\s+["\']?::', run):
            violations.append(f"{name}: contains '|| echo ::warning::'")
    assert not violations, (
        "python-tests job has shell suppressions. Remove them.\n" + "\n".join(violations)
    )


def test_python_tests_no_continue_on_error() -> None:
    """python-tests steps must not use continue-on-error: true."""
    steps = _get_python_tests_steps()
    violations = []
    for step in steps:
        name = step.get("name", "")
        if step.get("continue-on-error") is True:
            violations.append(f"{name}: has continue-on-error: true")
    assert not violations, (
        "python-tests job has continue-on-error. Remove it.\n" + "\n".join(violations)
    )


def test_python_tests_no_if_always() -> None:
    """python-tests steps must not use if: always()."""
    steps = _get_python_tests_steps()
    violations = []
    for step in steps:
        name = step.get("name", "")
        if_expr = step.get("if", "")
        if isinstance(if_expr, str) and "always()" in if_expr:
            violations.append(f"{name}: has if: always()")
    assert not violations, (
        "python-tests job has if: always(). Remove it.\n" + "\n".join(violations)
    )
