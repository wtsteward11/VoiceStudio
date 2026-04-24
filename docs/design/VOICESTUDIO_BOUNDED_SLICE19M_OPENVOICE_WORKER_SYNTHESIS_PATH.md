# VOICESTUDIO BOUNDED SLICE 19M — OpenVoice worker / synthesis path (post–preprocess)

**Status:** **Closed** — **Outcome A** (matrix **PASS** on **2026-04-22** ladder)  
**Date:** 2026-04-20 (ladder **2026-04-22**)

## Goal

After **green** `preprocess-reference` (200) on the **19L** speech reference, move from a vague “*engine returned None*” to a **named call chain** and **one frozen seam** with **command-line evidence** (worker + service file contract), then either a matrix **PASS** (2/2 + 3/3) or **Outcome B** (pending) with a single post-preprocess seam.

## Outcomes (mentor-locked)

| Outcome | Criteria |
|--------|----------|
| **A** | 2/2 `pytest -m real_openvoice` + 3/3 C# OpenVoice `LiveBackend`, **ENGINE_PARITY_MATRIX** `openvoice` **PASS** |
| **B** | One **exact** seam frozen with file paths, stderr, branch + conclusion |

## Non-goals

- No ADR-055 re-litigation, no new weight story, no RHVoice or other engine, no matrix PASS without 2/2 + 3/3, no “fixture is wrong” as the primary story.

## Request path (named functions)

1. **HTTP** — `POST /api/voice/synthesize` — `backend/api/routes/voice/synthesis.py` — `synthesize` → `SynthesisService.synthesize`  
2. **Service** — `backend/services/synthesis_service.py` — `SynthesisService.synthesize` — `engine.synthesize(**synthesis_kwargs)`; on `result is None` uses `_synth_output_file_ready(output_path)` before raising `ServiceError(500, "engine returned None", …)`  
3. **Engine (subprocess)** — `app/core/engines/openvoice_subprocess_engine.py` — `OpenVoiceSubprocessEngine.synthesize` → `_invoke_worker` (JSON tmp + `subprocess.run` → `python -m app.cli.openvoice_worker_synthesize`)  
4. **In-process OpenVoice (worker venv only)** — `app/cli/openvoice_worker_synthesize.py` — `main` → `run_from_request_path` — `OpenVoiceEngine.synthesize` → `se_extractor.get_se` → `BaseSpeakerTTS.tts` → `ToneColorConverter.convert`

**First function after successful preprocess (HTTP) where a wrong contract surfaces:** `SynthesisService.synthesize` (file-ready vs `None`); the worker fails earlier if `OpenVoiceSubprocessEngine.synthesize` returns `None` (non-zero exit, no WAV, or WAV &lt; 64 bytes as enforced in the engine).

## Slice 19M code findings (evidence-based)

- **Vendored `BaseSpeakerTTS.tts` signature** is `tts(self, text, output_path, speaker, language=..., speed=...)`; prior calls used a removed API (`language=`, `speed=` as only kwargs) → **base TTS always failed** until 19M.  
- **myshell `se_extractor.get_se` returns `tuple`** `(embedding, audio_name)`; passing the tuple to `ToneColorConverter.convert` caused **conv1d** type errors. Unpacking is required (`_unpack_se_extractor_result` in `openvoice_engine.py`).  
- **Windows I/O:** library `print` + cp1252 → set `PYTHONIOENCODING=utf-8` in worker `env` and reconfigure stdio in the worker CLI.  
- **Service contract (Conclusion A):** `OpenVoiceSubprocessEngine` returns `None` on success after verifying the output WAV (≥ 64 bytes). `SynthesisService` treats `result is None` + `_synth_output_file_ready` as **success** — verified once the worker produces a real file.

## Acceptance (worker evidence)

All items in [slice19m_worker_capture.md](../reports/verification/slice19/openvoice/slice19m_worker_capture.md) for at least one run.

## References

- [PROOF_SLICE19_OPENVOICE_AUDITION.md](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) — §19M  
- [slice19l_proof_session.md](../reports/verification/slice19/openvoice/slice19l_proof_session.md) (8043, Policy A)  
