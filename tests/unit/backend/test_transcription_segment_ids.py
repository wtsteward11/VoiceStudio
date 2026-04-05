"""GAP-033: transcription segments carry stable ids."""

from __future__ import annotations

import uuid

from backend.api.routes.transcribe import TranscriptionSegment


def test_transcription_segment_has_id_by_default() -> None:
    seg = TranscriptionSegment(text="hi", start=0.0, end=1.0)
    assert seg.id
    uuid.UUID(seg.id)


def test_transcription_segment_preserves_explicit_id() -> None:
    sid = str(uuid.uuid4())
    seg = TranscriptionSegment(id=sid, text="x", start=0.0, end=0.5)
    assert seg.id == sid
