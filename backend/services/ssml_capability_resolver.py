"""
GAP-054: Single authority for SSML detection and per-engine synthesis policy.

Resolves manifest-backed capability, applies preserve / strip+warn / reject,
and returns structured diagnostics for API responses.
"""

from __future__ import annotations

import json
import logging
import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Literal

logger = logging.getLogger(__name__)

SsmlCapabilityClass = Literal["supports_ssml", "plain_text_only", "supports_subset", "unknown"]
SsmlAction = Literal["none", "preserved", "stripped_warned", "rejected"]

_SSML_HINT = re.compile(
    r"<\s*/?\s*(speak|break|prosody|emphasis|say-as|sub|phoneme|p|s)\b",
    re.IGNORECASE,
)

# Subset engines: only these SSML elements are kept as markup; others flattened to text.
_SUBSET_ALLOWED = frozenset(
    {
        "speak",
        "break",
        "prosody",
        "emphasis",
        "say-as",
        "sub",
        "phoneme",
        "p",
        "s",
    }
)

_manifest_by_engine: dict[str, dict[str, Any] | None] = {}


class SsmlPolicyRejected(Exception):
    """SSML input cannot be processed (malformed markup). Maps to HTTP 422."""

    def __init__(self, message: str, *, engine_id: str = "") -> None:
        self.message = message
        self.engine_id = engine_id
        super().__init__(message)


def detect_ssml_markup(text: str) -> bool:
    """True when text likely contains SSML control markup (conservative hints)."""
    if not text or not text.strip():
        return False
    return _SSML_HINT.search(text) is not None


def _engines_root() -> Path:
    return Path(__file__).resolve().parents[2] / "engines"


def _load_manifest_for_engine(engine_id: str) -> dict[str, Any] | None:
    if engine_id in _manifest_by_engine:
        return _manifest_by_engine[engine_id]

    root = _engines_root()
    if not root.is_dir():
        logger.debug("Engines root missing: %s", root)
        _manifest_by_engine[engine_id] = None
        return None

    for path in root.rglob("engine.manifest.json"):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as e:
            logger.debug("Skip manifest %s: %s", path, e)
            continue
        if data.get("engine_id") == engine_id:
            _manifest_by_engine[engine_id] = data
            return data

    _manifest_by_engine[engine_id] = None
    return None


def resolve_ssml_capability_class(engine_id: str) -> SsmlCapabilityClass:
    """
    Map manifest contract.input to normalized capability class.
    """
    manifest = _load_manifest_for_engine(engine_id)
    if not manifest:
        return "unknown"

    contract = manifest.get("contract") or {}
    inp = contract.get("input") or {}
    if not isinstance(inp, dict):
        return "unknown"

    mode = inp.get("ssml_capability")
    if mode == "subset":
        return "supports_subset"
    if mode == "full" or inp.get("supports_ssml_tags") is True:
        return "supports_ssml"
    if inp.get("supports_ssml_tags") is False:
        return "plain_text_only"
    return "unknown"


def _wrap_speak_if_needed(text: str) -> str:
    stripped = text.strip()
    if not stripped.lower().startswith("<speak"):
        return f"<speak>{stripped}</speak>"
    return stripped


def _try_parse_speak(text: str) -> ET.Element | None:
    wrapped = _wrap_speak_if_needed(text)
    try:
        return ET.fromstring(wrapped)
    except ET.ParseError:
        return None


def _element_to_ssml_string(elem: ET.Element) -> str:
    """Serialize a single element subtree back to SSML-ish XML string."""
    return ET.tostring(elem, encoding="unicode")


def _extract_plain_text_from_element(elem: ET.Element) -> str:
    parts: list[str] = []
    if elem.text:
        parts.append(elem.text)
    for child in elem:
        parts.append(_extract_plain_text_from_element(child))
        if child.tail:
            parts.append(child.tail)
    return " ".join(p for p in " ".join(parts).split() if p).strip()


def strip_ssml_to_plain_text(text: str) -> tuple[str, list[str]]:
    """Remove SSML to plain text; uses XML when parseable, else regex fallback."""
    warnings: list[str] = []
    root = _try_parse_speak(text)
    if root is not None:
        plain = _extract_plain_text_from_element(root).strip()
        if plain:
            return plain, warnings
        warnings.append("SSML stripped to plain text (no speakable content after parse).")
        return "", warnings

    # Parse failed but caller only strips when policy allows — use regex fallback
    warnings.append("SSML could not be parsed as XML; stripped tags with regex fallback.")
    no_tags = re.sub(r"<[^>]+>", " ", text)
    plain = " ".join(no_tags.split()).strip()
    return plain, warnings


def _local_tag(tag: str) -> str:
    return tag.split("}")[-1] if "}" in tag else tag


def normalize_ssml_subset(text: str) -> tuple[str, list[str]]:
    """
    Keep only allowed SSML tags; unsupported tags become text (with warnings).
    """
    warnings: list[str] = []
    root = _try_parse_speak(text)
    if root is None:
        raise SsmlPolicyRejected(
            "Invalid SSML: could not parse XML after <speak> normalization.",
            engine_id="",
        )

    def transform(elem: ET.Element, path: str) -> ET.Element | str:
        tname = _local_tag(elem.tag)
        if tname not in _SUBSET_ALLOWED:
            warnings.append(
                f"Unsupported SSML tag <{tname}> at {path} was flattened to text "
                f"(subset engine policy)."
            )
            text_out = "".join(elem.itertext())
            if elem.tail:
                text_out += elem.tail
            return text_out

        new_el = ET.Element(tname, elem.attrib)
        new_el.text = elem.text or ""
        for child in elem:
            cname = _local_tag(child.tag)
            out = transform(child, f"{path}/{cname}")
            if isinstance(out, str):
                if len(new_el) == 0:
                    new_el.text = (new_el.text or "") + out
                else:
                    new_el[-1].tail = (new_el[-1].tail or "") + out
            else:
                new_el.append(out)
        if elem.tail:
            if len(new_el) == 0:
                new_el.text = (new_el.text or "") + elem.tail
            else:
                new_el[-1].tail = (new_el[-1].tail or "") + elem.tail
        return new_el

    top = root if _local_tag(root.tag) == "speak" else ET.Element("speak")
    if top is not root:
        top.append(root)

    out = transform(top, "/speak")
    if isinstance(out, str):
        out_el = ET.Element("speak")
        out_el.text = out
    else:
        out_el = out
    return _element_to_ssml_string(out_el), warnings


@dataclass
class SsmlSynthesisPolicyResult:
    """Outcome of apply_ssml_synthesis_policy for the synthesis pipeline."""

    effective_text: str
    skip_text_preprocessor: bool
    pass_ssml_to_engine: bool
    diagnostics: dict[str, Any] | None = None


def apply_ssml_synthesis_policy(engine_id: str, text: str) -> SsmlSynthesisPolicyResult:
    """
    Decide effective synthesis text, NLP skip, optional engine ssml flag, and diagnostics.

    Raises SsmlPolicyRejected when SSML-like input is malformed XML.
    """
    raw = text or ""
    if not detect_ssml_markup(raw):
        return SsmlSynthesisPolicyResult(
            effective_text=raw,
            skip_text_preprocessor=False,
            pass_ssml_to_engine=False,
            diagnostics=None,
        )

    cap = resolve_ssml_capability_class(engine_id)

    if cap == "supports_ssml":
        root = _try_parse_speak(raw)
        if root is None:
            raise SsmlPolicyRejected(
                "Invalid SSML markup; fix XML or send plain text.",
                engine_id=engine_id,
            )
        diag = {
            "ssml_detected": True,
            "capability_class": cap,
            "action": "preserved",
            "warnings": [],
            "engine_id": engine_id,
        }
        return SsmlSynthesisPolicyResult(
            effective_text=raw.strip(),
            skip_text_preprocessor=True,
            pass_ssml_to_engine=True,
            diagnostics=diag,
        )

    if cap == "supports_subset":
        normalized, warns = normalize_ssml_subset(raw)
        diag = {
            "ssml_detected": True,
            "capability_class": cap,
            "action": "stripped_warned",
            "warnings": warns,
            "engine_id": engine_id,
        }
        return SsmlSynthesisPolicyResult(
            effective_text=normalized,
            skip_text_preprocessor=True,
            pass_ssml_to_engine=True,
            diagnostics=diag,
        )

    if cap in ("plain_text_only", "unknown"):
        plain, warns = strip_ssml_to_plain_text(raw)
        extra = (
            "Engine manifest does not declare SSML support; markup was stripped."
            if cap == "unknown"
            else "Engine is plain-text-only; SSML tags were stripped."
        )
        all_warns = [extra, *warns]
        diag = {
            "ssml_detected": True,
            "capability_class": cap,
            "action": "stripped_warned",
            "warnings": all_warns,
            "engine_id": engine_id,
        }
        return SsmlSynthesisPolicyResult(
            effective_text=plain,
            skip_text_preprocessor=False,
            pass_ssml_to_engine=False,
            diagnostics=diag,
        )

    # Exhaustive for Literal
    logger.warning("Unexpected SSML capability %s for engine %s", cap, engine_id)
    plain, warns = strip_ssml_to_plain_text(raw)
    diag = {
        "ssml_detected": True,
        "capability_class": "unknown",
        "action": "stripped_warned",
        "warnings": ["Unexpected capability state; stripped SSML.", *warns],
        "engine_id": engine_id,
    }
    return SsmlSynthesisPolicyResult(
        effective_text=plain,
        skip_text_preprocessor=False,
        pass_ssml_to_engine=False,
        diagnostics=diag,
    )


def preview_policy_summary(engine_id: str, content: str) -> dict[str, Any]:
    """
    Non-throwing policy summary for SSML preview alignment (422 still possible).

    Returns dict with keys: ok (bool), diagnostics or error.
    """
    try:
        result = apply_ssml_synthesis_policy(engine_id, content)
    except SsmlPolicyRejected as e:
        return {"ok": False, "error": e.message, "engine_id": engine_id}
    return {"ok": True, "diagnostics": result.diagnostics}
