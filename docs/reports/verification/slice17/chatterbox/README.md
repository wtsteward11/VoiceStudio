# Slice 17 — Chatterbox verification artifacts

**Purpose:** Real WAV + optional log snippets for bounded Chatterbox runtime parity ([`PROOF_SLICE17_CHATTERBOX_AUDITION.md`](../PROOF_SLICE17_CHATTERBOX_AUDITION.md)).

**Prerequisite (Slice 17A):** Provision **`venv_advanced_tts`**, install **`chatterbox-tts`** (+ pins) into that venv, warm HF **`ResembleAI/chatterbox`**, until `GET /api/health/preflight` shows `checks.chatterbox.ok == true`. See [`VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md`](../../../design/VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md).

**Expected files (after green `checks.chatterbox.ok` + tests):**

| File | Source |
| --- | --- |
| `chatterbox_output.wav` | `pytest -m real_chatterbox` (integration) |
| `chatterbox_backend_log_snippet.txt` | Same pytest session |
| `chatterbox_csharp_stream.wav` | C# `ChatterboxPlaybackAuditionLiveBackendTests` stream proof |

**Contracts:** [`VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md`](../../../design/VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md), [`VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md`](../../../design/VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md)
