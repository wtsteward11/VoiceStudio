"""GAP-069 Slice 13: regression guard for failure-path smoke producer/consumer contract.

The harness expects failure_smoke_summary.json under LocalAppData VoiceStudio\\crashes.
That file is only written when OnLaunched runs the normal backend path (not Gate C
ui-smoke early return). Guard against accidental removal of env isolation or
failure-path routing overrides.
"""

from __future__ import annotations

import json
import re
from pathlib import Path


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


# ---------------------------------------------------------------------------
# Existing contract tests (symbol-presence guards)
# ---------------------------------------------------------------------------


def test_app_xaml_forces_normal_path_when_failure_smoke_env_requested() -> None:
    app_cs = (_repo_root() / "src" / "VoiceStudio.App" / "App.xaml.cs").read_text(encoding="utf-8")
    assert "IsSmokeFailurePortRequested()" in app_cs
    assert "IsSmokeFailureRuntimeRequested()" in app_cs
    assert "smokeExit = false" in app_cs and "uiSmoke = false" in app_cs
    assert "Port/runtime failure proofs" in app_cs or "failure_smoke_summary" in app_cs


def test_icon_launch_failure_smoke_script_strips_conflicting_smoke_env() -> None:
    script = (_repo_root() / "scripts" / "icon-launch-failure-smoke.ps1").read_text(encoding="utf-8")
    assert "VOICE_STUDIO_SMOKE_FAILURE_PORT" in script
    assert "VOICE_STUDIO_SMOKE_UI" in script
    assert "Environment.Remove" in script or ".Remove(" in script
    assert "VoiceStudio.App" in script and "Stop-Process" in script


def test_runtime_missing_failure_smoke_script_strips_conflicting_smoke_env() -> None:
    script = (_repo_root() / "scripts" / "runtime-missing-failure-smoke.ps1").read_text(encoding="utf-8")
    assert "VOICE_STUDIO_SMOKE_FAILURE_RUNTIME" in script
    assert "VOICE_STUDIO_SMOKE_UI" in script


# ---------------------------------------------------------------------------
# Post-closure hardening: schema, restore, and timing contract tests
# ---------------------------------------------------------------------------


def test_startup_decision_template_schema_v2_contract() -> None:
    """Validate startup_decision_success_template.json conforms to schema v2."""
    path = _repo_root() / "scripts" / "ci" / "startup_decision_success_template.json"
    assert path.exists(), f"Template missing: {path}"
    data = json.loads(path.read_text(encoding="utf-8"))

    assert data.get("schema_version") == 2, "schema_version must be 2"
    assert data.get("decision") in ("reuse", "spawn", "skip"), (
        f"decision '{data.get('decision')}' not in allowed set"
    )
    assert data.get("status") == "success", "template status must be 'success'"
    assert "health_probe_result" in data, "health_probe_result field required"
    assert "timeout_seconds" in data, "timeout_seconds field required"
    assert isinstance(data.get("timeout_seconds"), (int, float)), "timeout_seconds must be numeric"


def test_runtime_missing_harness_restores_startup_decision() -> None:
    """Validate runtime-missing-failure-smoke.ps1 backs up and restores startup_decision.json."""
    script = (_repo_root() / "scripts" / "runtime-missing-failure-smoke.ps1").read_text(encoding="utf-8")

    assert "startup_decision.json" in script, "must reference startup_decision.json"
    assert "startupDecisionBackup" in script, "must create a backup variable"
    assert "finally" in script.lower() or "finally {" in script, (
        "must have a finally block to guarantee restore"
    )
    assert "startup_decision_success_template.json" in script, (
        "must reference the success template for restore"
    )


def test_icon_launch_poll_timeout_within_bounds() -> None:
    """Validate icon-launch-failure-smoke.ps1 poll deadline is 30-120s inclusive."""
    script = (_repo_root() / "scripts" / "icon-launch-failure-smoke.ps1").read_text(encoding="utf-8")

    match = re.search(r"AddSeconds\(\s*(\d+)\s*\)", script)
    assert match, "could not find AddSeconds(N) poll deadline in icon-launch-failure-smoke.ps1"

    timeout_seconds = int(match.group(1))
    assert 30 <= timeout_seconds <= 120, (
        f"poll timeout {timeout_seconds}s outside allowed range [30, 120]"
    )
