# PROOF — Slice 13 — RHVoice engine readiness unblocker (RHVoice only)

**Status:** **Closed — Path B (unblocker only)** — `ensure_rhvoice` + `checks.rhvoice` on `GET /api/health/preflight`; **runtime synth/retrieval/playback proof deferred to Slice 14** until `rhvoice-say` / `rhvoice-cli` (or manifest `executable_path`) is available on the operator host.

**Date:** 2026-04-18  
**Scope:** **RHVoice readiness seam only** — same eventual shape as Slices 9–12: preflight → `POST /api/voice/synthesize` → `GET /api/audio/file/{id}` → client stream → optional NAudio. **`routed_engine` must equal `rhvoice` when proofs run.** **Not** umbrella “all TTS engines” or “synthesis is done.”

**Selection sentence (frozen):**

> Selected engine: `rhvoice` because readiness probe shows `instantiable: true`, `preflight_assets.ok: null`, manifest is local CLI-shaped (mirror of closed `espeak_ng`), GPL-3.0 license (operator due-diligence noted).

---

## Frozen contract (Slice 13)

| Item | Value |
| --- | --- |
| Authoritative engine id | `rhvoice` |
| HTTP | `POST /api/voice/synthesize` with `engine: "rhvoice"` → `GET /api/audio/file/{id}` |
| Binary | RHVoice CLI — engine resolves `rhvoice-say` first (see `app/core/engines/rhvoice_engine.py`); manifest default name `rhvoice-client` is also checked in `ensure_rhvoice` |
| Preflight key | `checks.rhvoice` on `GET /api/health/preflight` |
| Output | WAV (validate actual RIFF header in proof runs; manifest lists 22050 Hz — engine may differ) |
| No fallback | Missing binary → honest `ok: false` / 503-style payload in checks — **no** substitution to XTTS/Piper/eSpeak |

---

## Path B — Operator host (this closure)

**Gate:** `where.exe rhvoice-client` / `where.exe rhvoice-say` — **no RHVoice executable on PATH** on the machine used for this slice.

**Preflight evidence:** With repo `.venv` backend, `GET /api/health/preflight` includes `checks.rhvoice` with `ok: false` and an actionable message when the executable is missing (see `ensure_rhvoice` in `backend/services/model_preflight.py`).

**503 / checks payload (expected shape when binary missing):**

- `ok`: `false`
- `message`: indicates RHVoice executable not found; install RHVoice and ensure CLI on PATH or set `parameters.executable_path` for engine `rhvoice`

**Install (operator):**

- Upstream: [RHVoice](https://github.com/Olga-Yakovleva/RHVoice) — Windows builds from GitHub releases; after install, ensure `rhvoice-say.exe` or equivalent is on `PATH` or set `executable_path` in engine config.

**Artifact:** `docs/reports/verification/slice13/engine_readiness_probe.json` — `engines.rhvoice.preflight_assets` must **not** be `ok: null` (“no ensure_*”); it records honest preflight result.

---

## Slice 14 handoff

**Update (2026-04-18):** Harness landed — see [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md). Items below are **done in repo**; operator still must install RHVoice and record **runtime PASS** lines.

When `checks.rhvoice.ok == true` on a fresh uvicorn from repo `.venv`:

1. ~~Add/run `tests/integration/test_synthesis_rhvoice_real.py` with marker `real_rhvoice`~~ **Done** — run `pytest -m real_rhvoice` to populate WAVs.
2. ~~Add C# `RealSynthesisRhVoiceLiveBackendTests` + `RhVoicePlaybackAuditionLiveBackendTests` + `LivePreflightGuards.AssertRhVoicePreflightOkAsync`~~ **Done** — run `dotnet test` filter per Slice 14 proof doc.
3. Write WAV artifacts under `docs/reports/verification/slice14/rhvoice/` and update [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md) with command lines and PASS lines.

---

## Regression bar (Slice 13 Path B)

Re-ran baseline engines and gates **without** `real_rhvoice` (not yet present). Backend: `http://127.0.0.1:8030` (operator session).

| Step | Command | Result (2026-04-18) |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (5 pre-existing warnings) |
| Python real engines | `pytest` … `test_synthesis_xtts_real.py` + `test_synthesis_piper_real.py` + `test_synthesis_espeak_ng_real.py` `-m "real_xtts or real_piper or real_espeak_ng"` | **6 passed** |
| C# live (filter) | `dotnet test` … `--filter "…ProfilesRuntime…|LibraryRuntime…|GlobalSearch…|RealSynthesisESpeakNg…"` | **Passed: 3**, Skipped: 2 (search tests) |
| Gates | `python scripts/run_verification.py` | **Overall: PASS** |
| Quick verify | `.\scripts\verify.ps1 -Quick` | **exit 0** |

See `.cursor/STATE.md` **Last Verified Commands** for the canonical one-liners.

**GPL-3.0:** Manifest license for `rhvoice` is GPL-3.0 — same operator due-diligence posture as Slice 12 (eSpeak NG).
