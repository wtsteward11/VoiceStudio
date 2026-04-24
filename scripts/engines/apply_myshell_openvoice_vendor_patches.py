#!/usr/bin/env python3
"""
Re-apply VoiceStudio patches to vendored MyShell OpenVoice (Slice 19I / ADR-055).

Upstream pin: commit 74a1d147b17a8c3092dd5430504bd83ef6c7eb23
Vendor root: runtime/vendor/myshell-openvoice

Run after replacing the vendor tree (e.g. re-copy from upstream) so edits are reproducible.
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent / "runtime" / "vendor" / "myshell-openvoice"


def patch_setup() -> None:
    p = ROOT / "setup.py"
    if not p.exists():
        print("Missing", p, file=sys.stderr)
        sys.exit(1)
    s = p.read_text(encoding="utf-8")
    s = s.replace(
        "install_requires=[\n            'librosa==0.9.1',\n            'faster-whisper==0.9.0',",
        "install_requires=[\n            'librosa>=0.10.0',\n"
        "            # VoiceStudio: avoid mandatory faster-whisper (PyAV on Windows; ADR-055).\n"
        "            # Optional: pip install 'MyShell-OpenVoice[whisper]'\n",
    )
    s = s.replace("'numpy==1.22.0',", "'numpy>=1.24.0,<2.0',")
    if "extras_require" not in s:
        s = s.replace(
            "      ],\n      zip_safe=False",
            "      ],\n      extras_require={\n            'whisper': ['faster-whisper==0.9.0'],\n      },\n      zip_safe=False",
        )
    p.write_text(s, encoding="utf-8")


def patch_se_extractor() -> None:
    p = ROOT / "openvoice" / "se_extractor.py"
    if not p.exists():
        print("Missing", p, file=sys.stderr)
        sys.exit(1)
    s = p.read_text(encoding="utf-8")
    marker = "def split_audio_whisper"
    if marker not in s:
        print("Unexpected se_extractor layout", p, file=sys.stderr)
        sys.exit(1)
    head, _, tail = s.partition(marker)
    # Remove only module-level import (before first def split_audio_whisper).
    head = head.replace("from faster_whisper import WhisperModel\n", "", 1)
    s = head + marker + tail
    if "def split_audio_whisper" in s and "    from faster_whisper import WhisperModel" not in s:
        s = s.replace(
            "def split_audio_whisper(audio_path, audio_name, target_dir='processed'):\n    global model\n    if model is None:\n        model = WhisperModel(",
            "def split_audio_whisper(audio_path, audio_name, target_dir='processed'):\n    from faster_whisper import WhisperModel\n    global model\n    if model is None:\n        model = WhisperModel(",
            1,
        )
    p.write_text(s, encoding="utf-8")


def main() -> None:
    patch_setup()
    patch_se_extractor()
    print("Patched", ROOT)


if __name__ == "__main__":
    main()
