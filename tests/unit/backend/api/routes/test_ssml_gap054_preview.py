"""GAP-054: SSML preview aligns with canonical policy (preview_policy_summary)."""

from __future__ import annotations

import pytest

from backend.services.ssml_capability_resolver import preview_policy_summary
from backend.services.voice_helpers import normalize_engine_id


def test_preview_policy_summary_rejects_malformed() -> None:
    eid = normalize_engine_id("bark")
    out = preview_policy_summary(eid, "<speak><broken")
    assert out["ok"] is False
    assert "error" in out


def test_preview_policy_summary_ok_plain() -> None:
    eid = normalize_engine_id("bark")
    out = preview_policy_summary(eid, "no ssml here")
    assert out["ok"] is True
    assert out["diagnostics"] is None


def test_preview_policy_summary_ok_ssml_bark() -> None:
    eid = normalize_engine_id("bark")
    out = preview_policy_summary(eid, "<speak>Hi</speak>")
    assert out["ok"] is True
    d = out["diagnostics"]
    assert d is not None
    assert d["action"] == "preserved"
