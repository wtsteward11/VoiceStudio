# Slice 14 — RHVoice proof artifacts

On successful `pytest -m real_rhvoice` (with `checks.rhvoice.ok` and a live backend), tests write:

- `rhvoice_output.wav` — Python integration proof output
- `rhvoice_backend_log_snippet.txt` — metadata (profile_id, audio_id, duration, peak)

C# `RhVoicePlaybackAuditionLiveBackendTests` may write:

- `rhvoice_csharp_stream.wav` — stream proof WAV

**If these files are absent:** RHVoice was not installed / preflight was not green, or proofs were not run. Do not fabricate WAVs.

**Slice 14B (Mode B):** Closure requires a **real** RHVoice CLI and `engine_configs.rhvoice.parameters.executable_path` set to that binary’s absolute path in `backend/config/engine_config.json`, then backend restart — see [PROOF_SLICE14_RHVOICE_AUDITION.md](../PROOF_SLICE14_RHVOICE_AUDITION.md) §Slice 14B.

**Post–control-plane plan (2026-04-25):** **Tasks 14–18** on **Path B** — no WAVs produced; audit table in [PROOF_SLICE14_RHVOICE_AUDITION.md](../PROOF_SLICE14_RHVOICE_AUDITION.md) § **Tasks 14–18** (blocked / N/A). Primary product lane repointed per `.cursor/STATE.md` (CI harness row); RHVoice matrix **still pending**.
