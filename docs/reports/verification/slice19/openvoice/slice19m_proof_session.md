# Slice 19M — proof session (worker path + code fixes + optional live ladder)

**Date:** 2026-04-20

| Item | Value |
| --- | --- |
| **Slice** | **19M** — [VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md) |
| **Command / evidence** | [slice19m_worker_capture.md](slice19m_worker_capture.md) — worker command, exit code, stderr notes, `output_path` size post-fix |
| **Branch** | **A** for 19L-class *engine returned None* (no WAV before 19M fixes) |
| **Conclusion** | **A** for file-driven `None` + `_synth_output_file_ready` once WAV exists (see [PROOF §19M](../../PROOF_SLICE19_OPENVOICE_AUDITION.md) ) |
| **Live ladder** | **2/2** `pytest -m real_openvoice` + **3/3** C# `OpenVoice` `LiveBackend` on **`http://127.0.0.1:8055`**, `VOICESTUDIO_MODELS_PATH` = repo `models`, fresh Uvicorn (2026-04-22) |

**Code touchpoints (19M):** `app/core/engines/openvoice_engine.py` — `BaseSpeakerTTS.tts` + `se_extractor` unpack; `app/core/engines/openvoice_subprocess_engine.py` — `PYTHONIOENCODING`; `app/cli/openvoice_worker_synthesize.py` — stdio reconfigure on Windows.
