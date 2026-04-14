"""Contract tests for Windows CI verify-harness workflow and checkpoint lineage tooling."""

from __future__ import annotations

from pathlib import Path


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def test_lineage_fields_consistent_between_harness_and_helper() -> None:
    """verify.ps1 and show-checkpoint-lineage.ps1 must agree on checkpoint/pointer lineage fields."""
    verify = (_repo_root() / "scripts" / "verify.ps1").read_text(encoding="utf-8")
    helper = (_repo_root() / "scripts" / "show-checkpoint-lineage.ps1").read_text(encoding="utf-8")

    for field in ("artifact_dir", "run_timestamp", "last_completed_stage"):
        assert field in verify, f"verify.ps1 must reference {field}"
        assert field in helper, f"show-checkpoint-lineage.ps1 must reference {field}"

    assert "latest_pointer.json" in verify
    assert "latest_pointer.json" in helper
    assert "run_dir" in verify
    assert "run_dir" in helper


def test_verify_harness_workflow_stage_names() -> None:
    """Workflow must reference stage names that match scripts/verify.ps1."""
    workflow = (_repo_root() / ".github" / "workflows" / "verify-harness.yml").read_text(
        encoding="utf-8"
    )

    assert "C# Unit Tests - Other" in workflow, "checkpoint stage name must match verify.ps1"
    assert "Python Unit Tests" in workflow, "resume stage name must match verify.ps1"


def test_verify_harness_workflow_uploads_on_failure() -> None:
    """Artifact uploads must use if: always() so failure runs retain logs."""
    workflow = (_repo_root() / ".github" / "workflows" / "verify-harness.yml").read_text(
        encoding="utf-8"
    )
    lines = workflow.split("\n")
    upload_indices = [
        i for i, line in enumerate(lines) if "uses: actions/upload-artifact@v4" in line
    ]
    assert upload_indices, "verify-harness.yml must define upload-artifact steps"

    for idx in upload_indices:
        # `if: always()` may appear before or after `uses:` in the same step
        step_window = "\n".join(lines[max(0, idx - 15) : min(len(lines), idx + 8)])
        assert "always()" in step_window, (
            f"upload-artifact near line {idx + 1} must include 'if: always()' in the same step "
            "so failed runs still upload artifacts"
        )


def test_verify_harness_resume_stage_names_list_matches_invoke_stage_names() -> None:
    """The -ResumeFrom allowlist in verify.ps1 must cover every Invoke-Stage -Name string."""
    verify = (_repo_root() / "scripts" / "verify.ps1").read_text(encoding="utf-8")

    import re

    invoke_names = set(re.findall(r'Invoke-Stage -Name "([^"]+)"', verify))
    block_match = re.search(
        r"\$knownResumeStages = @\((.*?)\)\s*\r?\n\s*if \(\$ResumeFrom -notin",
        verify,
        re.DOTALL,
    )
    assert block_match, "verify.ps1 must define $knownResumeStages before -ResumeFrom validation"
    listed = {m.group(1).strip() for m in re.finditer(r'"([^"]+)"', block_match.group(1))}
    missing = invoke_names - listed
    assert not missing, f"knownResumeStages missing Invoke-Stage names: {sorted(missing)}"
