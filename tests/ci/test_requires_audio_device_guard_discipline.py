"""CI gate: C# tests that depend on audio I/O must reference AudioDeviceGuard.

Hosted `windows-latest` runners may have zero output devices; NAudio then throws
`BadDeviceId calling waveOutOpen` unless tests skip/inconclude first.

Rules (source scan under ``src/VoiceStudio.App.Tests``):

1. Any ``.cs`` file that declares ``[TestCategory("RequiresAudioDevice")]`` must
   contain ``AudioDeviceGuard.`` (typically ``SkipIfNoAudioOutputDevice`` /
   ``SkipIfNoAudioInputDevice``).

2. Any ``.cs`` file that constructs a concrete ``AudioPlayerService`` and calls
   ``PlayFileAsync`` or ``PlayUrlAsync`` on that path must contain
   ``AudioDeviceGuard.`` (covers live playback audition tests without relying
   only on MSTest category filtering).

See ``src/VoiceStudio.App.Tests/Helpers/AudioDeviceGuard.cs``.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
APP_TESTS = ROOT / "src" / "VoiceStudio.App.Tests"

REQUIRES_AUDIO_DEVICE_ATTR = re.compile(
    r'\[TestCategory\s*\(\s*"(?:RequiresAudioDevice)"\s*\)\]',
)
# Also allow single-quoted category (C# allows either)
REQUIRES_AUDIO_DEVICE_ATTR_SQ = re.compile(
    r"\[TestCategory\s*\(\s*'RequiresAudioDevice'\s*\)\]",
)

PLAY_ASYNC = re.compile(r"\.(?:PlayFileAsync|PlayUrlAsync)\s*\(")

# Concrete construction (not interface / mock names)
NEW_AUDIO_PLAYER = re.compile(
    r"new\s+(?:global::)?(?:VoiceStudio\.App\.Services\.)?AudioPlayerService\s*\(",
)


def _violations() -> list[str]:
    out: list[str] = []
    if not APP_TESTS.is_dir():
        return [f"Missing directory: {APP_TESTS.relative_to(ROOT).as_posix()}"]

    for path in sorted(APP_TESTS.rglob("*.cs")):
        if not path.is_file():
            continue
        try:
            text = path.read_text(encoding="utf-8-sig")
        except OSError as e:
            out.append(f"{path.relative_to(ROOT).as_posix()}: read error: {e}")
            continue
        rel = path.relative_to(ROOT).as_posix()
        has_guard = "AudioDeviceGuard." in text

        if REQUIRES_AUDIO_DEVICE_ATTR.search(text) or REQUIRES_AUDIO_DEVICE_ATTR_SQ.search(
            text
        ):
            if not has_guard:
                out.append(
                    f"{rel}: [TestCategory(\"RequiresAudioDevice\")] without AudioDeviceGuard.*"
                )

        if NEW_AUDIO_PLAYER.search(text) and PLAY_ASYNC.search(text) and not has_guard:
            out.append(
                f"{rel}: AudioPlayerService + PlayFileAsync/PlayUrlAsync without AudioDeviceGuard.*"
            )

    return out


def test_requires_audio_device_guard_discipline() -> None:
    """Fail when device-dependent C# tests omit AudioDeviceGuard."""
    bad = _violations()
    assert not bad, "Audio device guard discipline violations:\n" + "\n".join(bad)
