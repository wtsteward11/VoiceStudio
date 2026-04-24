# Bounded Slice 19L — OpenVoice reference-audio and VAD contract

**Status:** **Accepted (2026-04-20)** — reference contract + Policy A; **live ladder Outcome B** (2026-04-22) in [PROOF §19L](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) and [`.cursor/STATE.md`](../.cursor/STATE.md).  
**Depends on:** [Slice 19K](VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md) (Outcome B — real weights; **VAD** + **non-speech** harness; matrix **pending**)

## Goal

After **19K**, the **OpenVoice** live ladder failed because `se_extractor.get_se(..., vad=True)` (see [`openvoice_engine.py`](../../app/core/engines/openvoice_engine.py) and vendored `openvoice.se_extractor`) uses **Silero VAD** to gate usable audio. The shared harness file [`test_440hz_2s.wav`](../../tests/fixtures/audio/test_440hz_2s.wav) is a **pure tone** — VAD can classify **no speech**, yielding **0.0 s** usable duration and blocking speaker embedding.

**19L** defines the **authoritative reference-audio contract** for **OpenVoice instant clone** proofs, adds a **canonical speech** fixture, wires **OpenVoice-only** Python/C# tests to it, and re-runs the **live ladder** (one port, one `VOICESTUDIO_REAL_XTTS_HTTP_BASE`) to attempt **2/2 + 3/3** and document **Conclusion A / B / N/A** for file-driven `return None` when HTTP **200** exists.

## What OpenVoice requires (technical)

- **Reference for `se_extractor.get_se(..., vad=True)`** must leave **non-zero** speech after VAD. Non-speech signals (e.g. pure sine, continuous test tones) are **not** valid for this path.
- **Format:** Standard **WAV** readable by the existing **preprocess-reference** flow (mono or stereo; engine/path may resample — follow [`openvoice_engine`](../../app/core/engines/openvoice_engine.py) and upstream [MyShell OpenVoice](https://github.com/myshell-ai/OpenVoice) README for practical limits).
- **Duration:** Use a **short speech clip** (pragmatic **~1–10 s**) so VAD and embedding are stable; avoid **silent-only** or **noise-only** clips.
- **Regression / utility tone:** [`test_440hz_2s.wav`](../../tests/fixtures/audio/test_440hz_2s.wav) remains valid for **other engines** and regression checks; for **OpenVoice with `vad=True`**, it is an **invalid** reference (matches **19K** observation).

## Product policy (A vs B) — default for this repo

| Policy | Meaning | Repo implication |
| --- | --- | --- |
| **A (default)** | OpenVoice **live proofs** require **speech-like** reference audio | Use [`openvoice_reference_speech.wav`](../../tests/fixtures/audio/openvoice_reference_speech.wav) (or **`VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV`** override); **do not** use **440 Hz** for OpenVoice harness. |
| **B** | Product must accept **non-speech** or **VAD-degenerate** references | **Code** changes (e.g. `vad=False` or alternate embedding) + tests — **separate** follow-up; **19L** does **not** adopt B unless explicitly decided. |

**This slice adopts Policy A.**

## Canonical fixture and provenance

| Item | Detail |
| --- | --- |
| **Path (committed)** | `tests/fixtures/audio/openvoice_reference_speech.wav` |
| **Content** | English TTS **speech** (short phrase) suitable for Silero VAD. |
| **How produced** | One-time: **gTTS** 2.5.4 (MIT) → MP3; **FFmpeg** (GPL build from environment) to **mono PCM 22050 Hz** WAV. **No** secrets in file. |
| **License / use** | Synthetic speech via **gTTS**; repository use is for **local/engine testing** only. Operators who cannot commit a clip may set **`VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV`** to a host path (document **Outcome B** in CI if unmet). |

**Free-only:** No paid API subscription required for generation tool (gTTS is commonly used in dev/test); if policy tightens, replace fixture using an explicitly **CC0** or **public-domain** recording with the same VAD+speech properties.

## Harness wiring (Policy A)

- **Python:** `tests/integration/test_synthesis_openvoice_real.py` — `_openvoice_reference_wav()`; not [`test_440hz_2s.wav`](../../tests/fixtures/audio/test_440hz_2s.wav).
- **C#:** `RealSynthesisOpenVoiceLiveBackendTests` + `OpenVoicePlaybackAuditionLiveBackendTests` — shared resolver in [`OpenVoiceProofFixtures`](../../src/VoiceStudio.App.Tests/Helpers/OpenVoiceProofFixtures.cs).
- **Do not** change `test_synthesis_xtts_real.py` or other `real_*` files still using `_repo_fixture_wav()` unless a separate product decision unifies all engines on speech.

## `return None` + `SynthesisService` (settle on first HTTP 200)

Same table as [19K brief](VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md): **Conclusion A / B** only if synthesis reaches **200** and a real artifact; else **N/A** with the **new** primary seam if still **500**.

## Artifacts (this slice)

| Artifact | Role |
| --- | --- |
| [slice19l_proof_session.md](../reports/verification/slice19/openvoice/slice19l_proof_session.md) | Port **8043**, URL, **2/2 + 3/3** or **Outcome B**, return-`None` conclusion |
| [slice19l_preflight_openvoice.json](../reports/verification/slice19/openvoice/slice19l_preflight_openvoice.json) | Verbatim `GET /api/health/preflight` |

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-20 | Initial bounded brief — **19L** reference + **VAD** contract + **Policy A**. |
| 2026-04-20 | **Accepted** — contract + fixture + harness wire; live ladder + matrix per PROOF **§19L**. |
