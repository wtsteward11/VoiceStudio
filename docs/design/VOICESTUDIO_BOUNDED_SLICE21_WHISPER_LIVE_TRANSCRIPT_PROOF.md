# Bounded Slice 21 — Whisper live transcript proof (runtime)

**Status:** Proposed (2026-04-22)  
**Purpose:** Move **`engine_id: whisper`** from **readiness-only** (Slice 20) to an **honest** bounded **runtime** proof: HTTP → `transcription_service` → faster-whisper, or **one** exact frozen seam (Outcome B).

**Depends on:** [VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md](VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md) (boolean `checks.whisper` + `ensure_whisper`).

## In scope (runtime proof)

| Element | Contract |
| --- | --- |
| **Base URL** | **One** dedicated backend. Use **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** (default `http://127.0.0.1:8000`) — same env as TTS `real_*` proofs so operators set **one** URL for both gates. All commands in a session use the **identical** string. |
| **Preflight (gate)** | `GET {base}/api/health/preflight` → `checks["whisper"]["ok"]` must be **true** before expecting transcription to succeed. If **false**, runtime proof is **N/A** for that host; record `message` (Outcome B / environment). |
| **Input audio** | `POST {base}/api/library/assets/upload` (multipart) with a committed fixture under `tests/fixtures/audio/`; use returned **`audio_id`** or **`id`** as **`audio_id`** in transcribe. Resolves through [`_resolve_audio_path`](../../backend/services/transcription_service.py) + library asset lookup. |
| **Transcription** | `POST {base}/api/transcribe/` with JSON body: `{"audio_id": "<library id>", "engine": "whisper", "language": "en"}` (adjust `language` if needed). |
| **Success (Outcome A)** | HTTP **200**; JSON includes **`text`** (strip) **non-empty**, length and alphabetic checks per test; **`engine`** equals **`"whisper"`** (set from request in [transcription_service.py](../../backend/services/transcription_service.py)). **Transcript** truth, not audio playback. |
| **C#** | **Slice 21 = Python + HTTP on backend only.** [IBackendClient.TranscribeAudioAsync](../../src/VoiceStudio.App/Core/Services/IBackendClient.cs) shares the same route; a **C# `LiveBackend`** test is **deferred** unless a follow-on slice requires cross-layer STT proof (avoid duplicating JSON for symmetry only). |

## Out of scope (explicit)

- **WhisperX**, **`whisper_cpp`**, **vosk**, **parakeet** — not in this slice.
- **WER** / **per-word** accuracy metrics — not required for Outcome A.
- **STT “product complete”** or full **STT** matrix **PASS** for all engines.
- Re-opening **OpenVoice** or **RHVoice** lanes.
- **E2E UI** transcription — optional future slice.
- **Automatic** engine fallback (e.g. `whisper_cpp` → `whisper`) in tests — forbidden per `no-fallbacks.mdc`; this proof uses **`engine: "whisper"`** only.

## Canonical fixture and anchor

| Item | Detail |
| --- | --- |
| **Path** | `tests/fixtures/audio/openvoice_reference_speech.wav` (same as Slice 19L [Policy A](VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md#canonical-fixture-and-provenance)) — short **English TTS** speech, suitable for STT. |
| **Provenance** | Synthetic: gTTS + FFmpeg, documented in 19L; repo **test** use only. |
| **Assertion** | Minimum: non-empty `text` with at least one alphabetic run (stable across Whisper **tiny/base** paraphrase). **Optional** strict check: set **`VOICESTUDIO_WHISPER_PROOF_ANCHOR_SUBSTRING`** to require that substring in **lower-cased** transcript. |

## Branch A vs B (governance)

| Branch | When | Actions |
| --- | --- | --- |
| **A** | `real_whisper` passes on operator host with green preflight + 200 + assertions | [PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md](../reports/verification/PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md) filled with port, **commands**, artifact paths; [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) **whisper** STT line updated; `.cursor/STATE.md` milestone + **LATEST PROOF INDEX**; verify bar path. |
| **B** | First hard failure (preflight red, 404 audio, 503 engine, 500, empty transcript) | [PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md](../reports/verification/PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md) **Frozen seam** with **one** primary cause, HTTP status, first line of `detail`/`message`, and **one** next action. **No** matrix runtime **PASS**; matrix may say "runtime **pending** — see PROOF B". |

## Regression bar (before claiming Outcome A)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — 0 errors.
- `python scripts/run_verification.py` — PASS.
- `.\scripts\verify.ps1 -Quick` — VERIFICATION PASSED.

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-22 | Initial: endpoint, library upload → transcribe, fixture, no C# in 21, branches A/B. |
| 2026-04-22 | **Slice 21A closure:** [VOICESTUDIO_BOUNDED_SLICE21A_WHISPER_TRANSCRIPT_CLOSURE.md](VOICESTUDIO_BOUNDED_SLICE21A_WHISPER_TRANSCRIPT_CLOSURE.md) (success/fail table, session policy). Outcome A recorded in [PROOF](../reports/verification/PROOF_SLICE21_WHISPER_LIVE_TRANSCRIPT.md). |
