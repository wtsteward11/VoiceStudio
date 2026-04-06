"""Unit tests for GAP-054 SSML capability resolver."""

from __future__ import annotations

import pytest

from backend.services import ssml_capability_resolver as m
from backend.services.ssml_capability_resolver import (
    SsmlPolicyRejected,
    apply_ssml_synthesis_policy,
    detect_ssml_markup,
    normalize_ssml_subset,
    resolve_ssml_capability_class,
)


@pytest.fixture(autouse=True)
def _clear_manifest_cache() -> None:
    m._manifest_by_engine.clear()
    yield
    m._manifest_by_engine.clear()


def test_detect_ssml_markup_plain_false() -> None:
    assert detect_ssml_markup("hello") is False
    assert detect_ssml_markup("") is False


def test_detect_ssml_markup_hint_true() -> None:
    assert detect_ssml_markup("<speak>hi</speak>") is True
    assert detect_ssml_markup("<break time='200ms'/>") is True


def test_resolve_capability_bark_and_tacotron2() -> None:
    assert resolve_ssml_capability_class("bark") == "supports_ssml"
    assert resolve_ssml_capability_class("tacotron2") == "plain_text_only"


def test_resolve_unknown_engine() -> None:
    assert resolve_ssml_capability_class("definitely_missing_engine_xyz") == "unknown"


def test_plain_text_no_diagnostics() -> None:
    r = apply_ssml_synthesis_policy("bark", "just words")
    assert r.diagnostics is None
    assert r.effective_text == "just words"
    assert r.skip_text_preprocessor is False
    assert r.pass_ssml_to_engine is False


def test_bark_preserves_ssml() -> None:
    text = "<speak>Hello <break time='200ms'/> world</speak>"
    r = apply_ssml_synthesis_policy("bark", text)
    assert r.diagnostics is not None
    assert r.diagnostics["action"] == "preserved"
    assert r.diagnostics["capability_class"] == "supports_ssml"
    assert r.effective_text.strip() == text.strip()
    assert r.skip_text_preprocessor is True
    assert r.pass_ssml_to_engine is True


def test_tacotron2_strips_with_warning() -> None:
    r = apply_ssml_synthesis_policy("tacotron2", "<speak>Hello there</speak>")
    assert r.diagnostics is not None
    assert r.diagnostics["action"] == "stripped_warned"
    assert r.effective_text == "Hello there"
    assert "plain-text" in " ".join(r.diagnostics["warnings"]).lower()


def test_unknown_engine_strips_ssml() -> None:
    r = apply_ssml_synthesis_policy(
        "definitely_missing_engine_xyz", "<speak>Yo</speak>"
    )
    assert r.diagnostics is not None
    assert r.diagnostics["capability_class"] == "unknown"
    assert r.diagnostics["action"] == "stripped_warned"
    assert r.effective_text == "Yo"


def test_malformed_ssml_rejected_for_ssml_engine() -> None:
    with pytest.raises(SsmlPolicyRejected):
        apply_ssml_synthesis_policy("bark", "<speak><prosody rate='slow' unclosed")


def test_subset_flattens_unknown_tag(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(m, "resolve_ssml_capability_class", lambda _e: "supports_subset")
    r = apply_ssml_synthesis_policy(
        "dummy",
        "<speak>Hello <lexicon>bad</lexicon> end</speak>",
    )
    assert r.diagnostics is not None
    assert r.diagnostics["action"] == "stripped_warned"
    assert "Hello" in r.effective_text and "end" in r.effective_text
    assert any("lexicon" in w.lower() for w in r.diagnostics["warnings"])


def test_normalize_ssml_subset_rejects_invalid_xml() -> None:
    with pytest.raises(SsmlPolicyRejected):
        normalize_ssml_subset("<<<")
