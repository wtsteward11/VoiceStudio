"""Guards for engine temp WAV path (non-empty output detection)."""

from __future__ import annotations

import tempfile

from backend.services.synthesis_service import (
    _synth_output_file_ready,
    _synthesis_engine_output_path,
)


def test_synth_output_file_ready_rejects_zero_byte_file(tmp_path) -> None:
    path = tmp_path / "empty.wav"
    path.write_bytes(b"")
    assert _synth_output_file_ready(str(path)) is False


def test_synth_output_file_ready_accepts_nonempty_wav(tmp_path) -> None:
    path = tmp_path / "noise.wav"
    path.write_bytes(b"RIFF" + b"\x00" * 200)
    assert _synth_output_file_ready(str(path)) is True


def test_synthesis_engine_output_path_does_not_create_file() -> None:
    """Pre-created empty temp files must not satisfy 'engine wrote output'."""
    import os

    p = _synthesis_engine_output_path()
    assert not os.path.exists(p)


def test_named_temporary_empty_not_ready() -> None:
    """Regression: NamedTemporaryFile(delete=False) yields 0 bytes before engine runs."""
    with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
        name = tmp.name
    try:
        assert _synth_output_file_ready(name) is False
    finally:
        import os

        try:
            os.unlink(name)
        except OSError:
            pass
