"""Contract checks for generate_engine_truth (Slice 30 / Task 33)."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[3]
_GEN = _REPO_ROOT / "scripts" / "generate_engine_truth.py"
_VER = _REPO_ROOT / "docs" / "reports" / "verification" / "generated"


def test_generate_engine_truth_all_writes_valid_schemas() -> None:
    proc = subprocess.run(
        [sys.executable, str(_GEN), "--schema", "all"],
        cwd=str(_REPO_ROOT),
        capture_output=True,
        text=True,
        check=False,
    )
    assert proc.returncode == 0, proc.stdout + proc.stderr

    v1_path = _VER / "engine_truth.json"
    v2_path = _VER / "engine_truth_v2.json"
    assert v1_path.is_file()
    assert v2_path.is_file()

    v1 = json.loads(v1_path.read_text(encoding="utf-8"))
    assert v1.get("schema") == "voicestudio.engine_truth.v1"
    assert isinstance(v1.get("engines"), list)
    assert len(v1["engines"]) >= 1

    v2 = json.loads(v2_path.read_text(encoding="utf-8"))
    assert v2.get("schema") == "voicestudio.engine_truth.v2"
    engines = v2.get("engines")
    assert isinstance(engines, list)
    by_id = {
        str(e.get("engine_id")): e
        for e in engines
        if e.get("engine_id") and "error" not in e
    }
    wc = by_id.get("whisper_cpp")
    assert wc is not None
    assert "readiness_status" in wc
    assert "runtime_proof_status" in wc
    assert "manifest_consistency_ok" in wc
    assert wc.get("engine_kind") == "stt"
    pk = by_id.get("parakeet")
    assert pk is not None
    assert pk.get("engine_kind") == "tts"
    assert pk.get("manifest_consistency_ok") is True

    chatter = by_id.get("chatterbox")
    assert chatter is not None
    assert chatter.get("runtime_proof_status") == "pass"

    openv = by_id.get("openvoice")
    assert openv is not None
    assert openv.get("runtime_proof_status") == "pass"

    rh = by_id.get("rhvoice")
    assert rh is not None
    assert rh.get("runtime_proof_status") == "pending"
    assert rh.get("first_blocker") is not None

    bark = by_id.get("bark")
    assert bark is not None
    assert bark.get("runtime_proof_status") == "pending"
    assert bark.get("readiness_status") == "preflight_not_boolean"

    wx = by_id.get("whisperx")
    assert wx is not None
    assert wx.get("engine_kind") == "stt"
    assert wx.get("runtime_proof_status") == "pending"

    rvc = by_id.get("rvc")
    assert rvc is not None
    assert rvc.get("engine_kind") == "sts"
    assert rvc.get("manifest_consistency_ok") is True

    rvc2 = by_id.get("rvc_v2")
    assert rvc2 is not None
    assert rvc2.get("engine_kind") == "sts"
    assert rvc2.get("manifest_consistency_ok") is True

    gpt = by_id.get("gpt_sovits")
    assert gpt is not None
    assert gpt.get("engine_kind") == "vc"
    assert gpt.get("manifest_consistency_ok") is True


def _load_generate_engine_truth_module():
    spec = importlib.util.spec_from_file_location("generate_engine_truth", _GEN)
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def test_manifest_consistency_subtype_map() -> None:
    mod = _load_generate_engine_truth_module()
    assert mod._manifest_consistency_ok("voice_conversion", "sts")
    assert not mod._manifest_consistency_ok("voice_conversion", "tts")
    assert mod._manifest_consistency_ok("stt", "stt")
    assert not mod._manifest_consistency_ok("stt", "tts")
    assert mod._manifest_consistency_ok("tts", "tts")
    assert not mod._manifest_consistency_ok("vc", "tts")
    assert mod._manifest_consistency_ok("vc", "vc")
