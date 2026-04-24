"""
One-shot OpenVoice synthesis in the **venv_openvoice** interpreter (Slice 19F).

Invoked by OpenVoiceSubprocessEngine via subprocess — not imported by the API worker.
"""

from __future__ import annotations

import json
import logging
import sys
from pathlib import Path

logger = logging.getLogger(__name__)


def run_from_request_path(request_path: Path) -> None:
    """Load JSON request, synthesize, write WAV. Raises on failure."""
    raw = request_path.read_text(encoding="utf-8")
    data = json.loads(raw)

    text = data.get("text") or ""
    speaker_wav = data.get("speaker_wav")
    output_path = data.get("output_path")
    language = data.get("language") or "en"
    base_speaker_model = data.get("base_speaker_model") or "openvoice/base_speakers/EN"
    tone_color_converter_model = data.get("tone_color_converter_model") or "openvoice/converter"
    device = data.get("device")
    gpu = bool(data.get("gpu", True))
    enhance_quality = bool(data.get("enhance_quality", False))
    calculate_quality = bool(data.get("calculate_quality", False))
    enable_style_control = bool(data.get("enable_style_control", True))
    speed = data.get("speed", 1.0)

    if not text.strip():
        raise ValueError("text is empty")
    if not speaker_wav:
        raise ValueError("speaker_wav is required")
    if not output_path:
        raise ValueError("output_path is required")
    if not Path(speaker_wav).is_file():
        raise FileNotFoundError(f"speaker_wav not found: {speaker_wav}")

    from app.core.engines.openvoice_engine import OpenVoiceEngine

    engine = OpenVoiceEngine(
        base_speaker_model=str(base_speaker_model),
        tone_color_converter_model=str(tone_color_converter_model),
        device=device,
        gpu=gpu,
        enable_style_control=enable_style_control,
    )
    if not engine.initialize():
        raise RuntimeError("OpenVoiceEngine.initialize() failed")

    result = engine.synthesize(
        text,
        speaker_wav,
        language=language,
        output_path=output_path,
        enhance_quality=enhance_quality,
        calculate_quality=calculate_quality,
        speed=speed,
    )
    out_p = Path(output_path)
    if not out_p.is_file() or out_p.stat().st_size < 64:
        raise RuntimeError(
            f"OpenVoice worker did not produce a valid WAV at {output_path} (result={result!r})"
        )


def main() -> int:
    # Windows: avoid UnicodeEncodeError when myshell OpenVoice prints sentence splits.
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
            sys.stderr.reconfigure(encoding="utf-8", errors="replace")
        except (OSError, ValueError, AttributeError):
            pass
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    if len(sys.argv) < 2:
        sys.stderr.write(
            "usage: python -m app.cli.openvoice_worker_synthesize <request.json>\n",
        )
        return 2
    req = Path(sys.argv[1])
    if not req.is_file():
        sys.stderr.write(f"request file not found: {req}\n")
        return 2
    try:
        run_from_request_path(req)
    except Exception as e:
        logger.exception("openvoice_worker_synthesize failed")
        sys.stderr.write(f"{type(e).__name__}: {e}\n")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
