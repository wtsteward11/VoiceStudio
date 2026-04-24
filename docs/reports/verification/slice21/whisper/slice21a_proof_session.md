# Slice 21A — proof session (Outcome A)

**Date (local):** 2026-04-22  
**Backend:** `http://127.0.0.1:8066`  
**Start command:** `e:\VoiceStudio\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8066` (cwd: repo root)  
**Env:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8066`  
**Python (pytest):** `E:\VoiceStudio\.venv\Scripts\python.exe`

## Gates

- **`GET /health`:** 200, `engines_ready: true` (prerequisite for `live_whisper_client` fixture).
- **`GET /api/health/preflight`:** `checks.whisper.ok == true` (see [slice21a_preflight_whisper.json](slice21a_preflight_whisper.json)).

## Pytest

```text
python -m pytest tests/integration/test_transcribe_whisper_real.py -v -m real_whisper --tb=short
```

**Result:** **1 passed** in ~16s (not skipped). Log: [slice21a_pytest_session.txt](slice21a_pytest_session.txt).

## Transcript (canonical fixture)

- **Fixture:** `tests/fixtures/audio/openvoice_reference_speech.wav`
- **Resolved transcript text:**  
  `This is a voice studio open voice reference proof clip for voice cloning.`
- **Anchor rule:** default harness (min length + `[a-zA]{3,}`); optional env substring not set for this session.
- **Response payload:** [slice21a_transcribe_response.json](slice21a_transcribe_response.json) (upload + `POST /api/transcribe/` JSON; includes `segments`, `duration`, `engine: whisper`).

## C#

**Out of scope** for 21A — no `TranscribeAudioAsync` live test.

## Uvicorn lifecycle

Listener on **8066** was **stopped** after the session (`Stop-Process` on the listening PID) to avoid orphan servers. Re-run 21A on a **fresh** process for replay.
