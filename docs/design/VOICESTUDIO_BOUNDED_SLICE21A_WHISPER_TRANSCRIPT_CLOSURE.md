# Bounded Slice 21A — Whisper live transcript closure (operator + governance)

**Status:** Closed **Outcome A** (2026-04-22) — one **non-skipped** `real_whisper` session on a **dedicated** backend with green `checks.whisper`.  
**Depends on:** [Slice 21 harness](VOICESTUDIO_BOUNDED_SLICE21_WHISPER_LIVE_TRANSCRIPT_PROOF.md) + [Slice 20 readiness](VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md).

## Goal

Move **`engine_id: whisper`** from **harness-only** to **runtime transcript proven** (Python/HTTP) **or** freeze the first exact runtime seam — without `whisper_cpp` / `vosk` / `parakeet`, without C# STT, without claiming umbrella STT parity.

## In scope

| Element | Contract |
| --- | --- |
| **Endpoint** | `POST {base}/api/transcribe/` with `engine: "whisper"` |
| **Path** | `GET /api/health/preflight` → `checks["whisper"]["ok"]` → `POST /api/library/assets/upload` → transcribe (same as Slice 21) |
| **Base URL** | **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** — one string for all HTTP calls in the session |
| **Fixture** | `tests/fixtures/audio/openvoice_reference_speech.wav` (or `VOICESTUDIO_WHISPER_PROOF_WAV`); [19L provenance](VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md#canonical-fixture-and-provenance) (gTTS+FFmpeg synthetic English; test use) |
| **Anchor** | Default: length ≥ 5 + word-like regex in [`test_transcribe_whisper_real.py`](../../tests/integration/test_transcribe_whisper_real.py); optional **`VOICESTUDIO_WHISPER_PROOF_ANCHOR_SUBSTRING`** for strict substring |
| **Success** | HTTP 200, `text` non-empty, anchor pass, `engine == "whisper"`; **`pytest -m real_whisper` PASS** (not skipped) |
| **Out of scope** | C# `TranscribeAudioAsync`; other STT engines; WER; multi-language product closure |

## Outcomes

| Branch | When | Governance |
| --- | --- | --- |
| **A** | Green preflight + pytest PASS + evidence on disk | [PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md](../reports/verification/PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md) §21A; [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) `whisper` STT; **STATE** + verify bar |
| **B** | First real failure (preflight, upload, transcribe, anchor, timeout) | PROOF **Frozen seam**; matrix **runtime pending** |

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-22 | **Outcome A** — dedicated **8066**; current `backend.api.main`; [slice21a_proof_session.md](../reports/verification/slice21/whisper/slice21a_proof_session.md) |
