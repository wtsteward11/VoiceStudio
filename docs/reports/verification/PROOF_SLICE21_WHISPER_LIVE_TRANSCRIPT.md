# PROOF — Bounded Slice 21 / 21A — Whisper live transcript (runtime)

**Date:** 2026-04-22 (Slice 21 harness); **2026-04-22** (Slice **21A** **Outcome A** — runtime proof)  
**Contract:** [VOICESTUDIO_BOUNDED_SLICE21_WHISPER_LIVE_TRANSCRIPT_PROOF.md](../../design/VOICESTUDIO_BOUNDED_SLICE21_WHISPER_LIVE_TRANSCRIPT_PROOF.md)  
**21A brief:** [VOICESTUDIO_BOUNDED_SLICE21A_WHISPER_TRANSCRIPT_CLOSURE.md](../../design/VOICESTUDIO_BOUNDED_SLICE21A_WHISPER_TRANSCRIPT_CLOSURE.md)  
**Depends on:** [Slice 20 readiness](PROOF_SLICE20_WHISPER_READINESS.md) (`checks.whisper` boolean).

## Scope

- **In:** `GET /api/health/preflight` → `checks["whisper"]["ok"]` → `POST /api/library/assets/upload` → `POST /api/transcribe/` with `engine: "whisper"`, valid `audio_id`.
- **Out:** C# `TranscribeAudioAsync` — **deferred** (Slice 21/21A = **Python + HTTP** only; same route as WinUI when needed later).
- **Base URL:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` (default `http://127.0.0.1:8000`).

## Implementation (Slice 21 — harness)

| Deliverable | Path |
| --- | --- |
| Opt-in pytest | `tests/integration/test_transcribe_whisper_real.py` |
| Marker | `real_whisper` in `pytest.ini` (excluded from default `-m`) |
| Fixture | `tests/fixtures/audio/openvoice_reference_speech.wav` (override: `VOICESTUDIO_WHISPER_PROOF_WAV`) |
| Anchor | Min length + word-like letters; optional `VOICESTUDIO_WHISPER_PROOF_ANCHOR_SUBSTRING` |

## §21A — Runtime transcript closure (Outcome A)

**Status:** **Closed Outcome A** (2026-04-22).

### What counts as success (21A)

| Criterion | Required |
| --- | ---: |
| One **dedicated** Uvicorn (current `backend.api.main:app`) on a **known** port | Yes |
| **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** = `http://127.0.0.1:<port>` for **all** client calls | Yes |
| `GET /api/health/preflight` with **`checks.whisper.ok: true`** | Yes |
| `python -m pytest tests/integration/test_transcribe_whisper_real.py -v -m real_whisper --tb=short` → **1 passed, 0 skipped** | Yes |
| C# / WinUI | **Out of scope** |

**Failure (Outcome B) would be:** any **non-skip** HTTP error, empty transcript, anchor failure, or misconfigured base URL after honest retry — documented as **Frozen seam** with status + `message` + one next action.

### Session record (this closure)

| Field | Value |
| --- | --- |
| **Base URL** | `http://127.0.0.1:8066` |
| **Interpreter** | `E:\VoiceStudio\.venv\Scripts\python.exe` |
| **Preflight `checks.whisper`** | [slice21/whisper/slice21a_preflight_whisper.json](slice21/whisper/slice21a_preflight_whisper.json) |
| **Pytest log** | [slice21/whisper/slice21a_pytest_session.txt](slice21/whisper/slice21a_pytest_session.txt) |
| **Upload + transcribe JSON** | [slice21/whisper/slice21a_transcribe_response.json](slice21/whisper/slice21a_transcribe_response.json) |
| **Narrative** | [slice21/whisper/slice21a_proof_session.md](slice21/whisper/slice21a_proof_session.md) |

### Transcript (actual)

**Text returned** (`response.text`):

```text
This is a voice studio open voice reference proof clip for voice cloning.
```

**Engine:** `whisper` (JSON). **Duration (API):** ~4.96 s. **Segments:** one segment in [slice21a_transcribe_response.json](slice21/whisper/slice21a_transcribe_response.json) (shape documented there).

**Fixture:** `tests/fixtures/audio/openvoice_reference_speech.wav` (same gTTS+FFmpeg English clip as 19L; see [19L contract](../../design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md)).

**Anchor:** default rules only (no `VOICESTUDIO_WHISPER_PROOF_ANCHOR_SUBSTRING` in this run).

## Operator commands (replay)

1. From repo root, start Uvicorn on a **dedicated** port, e.g.:
   ```text
   .\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8066
   ```
2. In another shell:
   ```powershell
   $env:VOICESTUDIO_REAL_XTTS_HTTP_BASE="http://127.0.0.1:8066"
   python -m pytest tests/integration/test_transcribe_whisper_real.py -v -m real_whisper --tb=short
   ```
3. Before claiming green: `GET /api/health/preflight` → `checks.whisper.ok: true` (install **faster-whisper** in the API venv if false).

## Historical: Slice 21 harness-only (superseded by 21A)

**Branch B (harness-only closure, pre-21A):** Contract, test harness, and first governance pass are **landed**; first **non-skipped** `real_whisper` was not recorded that day.

### Frozen seam (environment N/A)

When `checks.whisper.ok` is **false**, runtime proof is **N/A**: follow [PROOF_SLICE20_WHISPER_READINESS.md](PROOF_SLICE20_WHISPER_READINESS.md) / `ensure_whisper` — install **faster-whisper** (and model cache permissions) in the **API** Python environment; re-run preflight before `real_whisper`.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-22 | **Slice 21A Outcome A:** dedicated **8066**, `real_whisper` **PASS**, artifacts under `slice21/whisper/`; matrix may record **runtime transcript PASS** for `whisper`. |
| 2026-04-22 | Initial: harness + **Outcome B** (pending operator); matrix honesty. |
