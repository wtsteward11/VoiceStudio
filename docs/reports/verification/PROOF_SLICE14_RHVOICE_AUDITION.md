# PROOF — Slice 14 — RHVoice runtime parity (RHVoice only)

**Document state:** **Harness implemented — runtime parity evidence is recorded only when the tables below contain real PASS lines** (no fabricated synthesis). Operator must install RHVoice CLI so `GET /api/health/preflight` → `checks.rhvoice.ok == true`, then run the regression bar and paste outputs here.

**Date:** 2026-04-18  
**Scope:** Same seam as Slices 9–12: preflight → `POST /api/voice/synthesize` → `GET /api/audio/file/{id}` → client stream → optional NAudio. **`routed_engine` must equal `rhvoice`.** **Not** umbrella “all TTS engines” closure.

## Frozen contract (Slice 14)

| Item | Value |
| --- | --- |
| Authoritative engine id | `rhvoice` |
| HTTP | `POST /api/voice/synthesize` with `engine: "rhvoice"` → `GET /api/audio/file/{id}` |
| Binary | RHVoice CLI — see `ensure_rhvoice` in `backend/services/model_preflight.py` and `_find_executable` in `app/core/engines/rhvoice_engine.py` |
| Preflight key | `checks.rhvoice` on `GET /api/health/preflight` |
| Output | WAV — validate RIFF + non-silent PCM peak (sample rate per adapter) |
| No fallback | Missing or failed engine → explicit error — **no** substitution to XTTS/Piper/eSpeak |

**Slice 16 (support-contract truth):** Authoritative modes + `executable_path` wiring to `RHVoiceEngine` — [VOICESTUDIO_BOUNDED_SLICE16_RHVOICE_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE16_RHVOICE_SUPPORT_CONTRACT.md). Matrix **PASS** for `rhvoice` still requires the operator gate below; Slice 16 does **not** claim stock-Windows RHVoice readiness.

## Operator gate (must be true before claiming PASS)

1. `where.exe rhvoice-say` (or `rhvoice-cli`, `rhvoice-client`, `RHVoice-test`) returns an executable path.
2. Live uvicorn (repo `.venv`) — `GET /api/health/preflight` includes `checks.rhvoice` with `"ok": true`.
3. `docs/reports/verification/slice14/engine_readiness_probe.json` — `router.engines.rhvoice.preflight_assets.ok == true` after `VOICESTUDIO_ENGINE_PROBE_FULL=1 python scripts/engine_readiness_probe.py`.

**Agent sessions:** RHVoice CLI was **not** on PATH; probe shows `preflight_assets.ok: false`. Harness tests (`real_rhvoice`, C# `LiveBackend`) **skip** until the gate is green.

### Slice 14B (2026-04-19) — Mode B (`parameters.executable_path`) session; binary absent; no matrix PASS

**Intent:** Prove RHVoice only via Mode B per bounded plan — explicit `engine_configs.rhvoice.parameters.executable_path`, no stock-Windows PATH fiction.

**Binary acquisition:** `where.exe rhvoice-say` / `rhvoice-cli` / `rhvoice-client` — **not found**. Recursive search under repo and common roots for `rhvoice*.exe` — **none**. No operator-provisioned CLI available in this session → **cannot** satisfy Task 1 of the Mode B proof chain.

**Config:** `engine_configs.rhvoice` added to [backend/config/engine_config.json](../../../backend/config/engine_config.json) with `parameters.executable_path` **empty string** (placeholder for Mode B), `voice` / `language` per manifest defaults. **Operator:** set `executable_path` to the **absolute path** of a real `rhvoice-say`, `rhvoice-cli`, `rhvoice-client`, or `RHVoice-test` binary (or a directory containing one), **restart** the FastAPI process, then re-run gates.

**Probe:** `VOICESTUDIO_ENGINE_PROBE_FULL=1 python scripts/engine_readiness_probe.py` — [`slice14/engine_readiness_probe.json`](slice14/engine_readiness_probe.json) **`timestamp_utc` `2026-04-19T20:13:05.165476+00:00`** — `engines["rhvoice"].preflight_assets.ok` **false** (Slice 16-shaped message; CLI still missing).

**Preflight (live backend `http://127.0.0.1:8002`):** `checks.rhvoice.ok` **false** — gate not green; do not claim synthesis closure.

**Python:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8002` `python -m pytest tests/integration/test_synthesis_rhvoice_real.py -v -m real_rhvoice` — **2 skipped** (preflight gate).

**C#:** same base URL — `dotnet test` … `--filter "FullyQualifiedName~RealSynthesisRhVoice|FullyQualifiedName~RhVoicePlaybackAudition"` — **Skipped: 3** (`Synthesize_RhVoice_LiveBackend_*`, stream, playback).

**Artifacts:** No `rhvoice_output.wav` / `rhvoice_backend_log_snippet.txt` / `rhvoice_csharp_stream.wav` — **not produced** (skipped proofs). **No fabricated WAVs.**

**Verdict:** Slice 14 **still open** — **Mode B pending operator-supplied real CLI**. Matrix row **pending**. Next session: supply binary path → green preflight → re-run harness → real artifacts → then matrix PASS.

**Regression bar (Slice 14B post–STATE closure, 2026-04-19):** `python scripts/run_verification.py` **Overall: PASS** (`.buildlogs/verification/last_run.json`); `.\scripts\verify.ps1 -Quick` **VERIFICATION PASSED** — [`artifacts/verify/20260419_151839/verification_report.md`](../../../artifacts/verify/20260419_151839/verification_report.md); harness clean build **0 errors** (`artifacts/verify/20260419_151839/logs/clean_build.log`).

### Path 1 attempt (2026-04-19) — install blocked; readiness honest; no matrix PASS

**Install check:** `where.exe rhvoice-say`, `rhvoice-cli`, `rhvoice-client` — **not found**. `winget search rhvoice` — **no package**. Chocolatey / Scoop — **no package**. Upstream [RHVoice releases](https://github.com/RHVoice/RHVoice/releases) do not publish a standalone Windows `rhvoice-say.exe` MSI in recent assets (NVDA addon / SAPI voices do not satisfy the **CLI** contract used by `ensure_rhvoice` + `RHVoiceEngine`). **Operator action:** build from source, use Linux/WSL with CLI on PATH, or supply `parameters.executable_path` in engine config pointing at a working `rhvoice-say` / `rhvoice-cli` binary — then re-run this proof chain.

**`GET /api/health/preflight` — `checks.rhvoice` (verbatim, backend `http://127.0.0.1:8002`):**

```json
{
  "ok": false,
  "downloaded": false,
  "message": "RHVoice CLI not found. Stock Windows does not ship RHVoice; install a supported RHVoice binary externally (see RHVoice project), then set engine_configs.rhvoice.parameters.executable_path in backend/config/engine_config.json to the full path of rhvoice-say, rhvoice-cli, or rhvoice-client, or place one of those names on PATH.",
  "status_code": 503
}
```

**Probe mirror:** [`slice14/engine_readiness_probe.json`](slice14/engine_readiness_probe.json) — `timestamp_utc` **2026-04-19T19:13:20.102383+00:00** — `engines["rhvoice"].preflight_assets.ok` **false** (boolean; not `null`).

**Python:** `pytest -m real_rhvoice` — **2 skipped** (preflight gate).

**C#:** `dotnet test --filter "FullyQualifiedName~RhVoice"` — **3 skipped** (preflight not green).

**Regression bar (2026-04-19, same session):** `dotnet build` **0 errors**; `python scripts/run_verification.py` **Overall: PASS**; `.\scripts\verify.ps1 -Quick` **VERIFICATION PASSED** — `artifacts/verify/20260419_141644/verification_report.md`.

## Python — `real_rhvoice`

**Tests:** `tests/integration/test_synthesis_rhvoice_real.py`

**Command (after gate green):**

```text
.venv\Scripts\python.exe -m pytest tests/integration/test_synthesis_rhvoice_real.py -v -m real_rhvoice --tb=short
```

**PASS lines (paste from terminal when run):**

```text
(TBD — operator run after RHVoice install)
```

**Artifacts (written by test on PASS):**

- `docs/reports/verification/slice14/rhvoice/rhvoice_output.wav`
- `docs/reports/verification/slice14/rhvoice/rhvoice_backend_log_snippet.txt`

## C# — live backend

**Classes:** `RealSynthesisRhVoiceLiveBackendTests`, `RhVoicePlaybackAuditionLiveBackendTests`  
**Base URL:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` (default `http://127.0.0.1:8000`)

**Command (after gate green):**

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RealSynthesisRhVoice|FullyQualifiedName~RhVoicePlaybackAudition"
```

**PASS lines (paste when run):**

```text
(TBD — operator run after RHVoice install)
```

**Stream artifact (stream test):** `docs/reports/verification/slice14/rhvoice/rhvoice_csharp_stream.wav` (on PASS)

**Playback:** `RhVoicePlaybackAuditionLiveBackendTests.Synthesize_ThenPlayback_*` may be **Inconclusive** on headless hosts (same as Slice 12 eSpeak NG).

## Regression bar (mandatory before claiming Slice 14 CLOSED)

Run in order against the **same** backend:

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. `python -m pytest tests/integration/test_synthesis_xtts_real.py tests/integration/test_synthesis_piper_real.py tests/integration/test_synthesis_espeak_ng_real.py tests/integration/test_synthesis_rhvoice_real.py -m "real_xtts or real_piper or real_espeak_ng or real_rhvoice" -v --tb=short`
3. `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ProfilesRuntimeLiveBackendTests|FullyQualifiedName~LibraryRuntimeLiveBackendTests|FullyQualifiedName~GlobalSearchLiveBackendTests|FullyQualifiedName~RealSynthesisESpeakNg|FullyQualifiedName~RealSynthesisRhVoice|FullyQualifiedName~RhVoicePlaybackAudition"`
4. `python scripts/run_verification.py` → Overall: PASS
5. `.\scripts\verify.ps1 -Quick` → exit 0

**Results (paste):**

```text
(TBD — operator run)
```

### Agent session (2026-04-18) — harness + regression (RHVoice skipped / inconclusive; no fake PASS)

| Step | Command | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| Python real engines (4) | `pytest` … four `test_synthesis_*_real.py` `-m "real_xtts or real_piper or real_espeak_ng or real_rhvoice"` | **6 passed**, **2 skipped** (`real_rhvoice` — `checks.rhvoice.ok` not true) |
| C# live filter | `dotnet test` … `ProfilesRuntime\|LibraryRuntime\|GlobalSearch\|RealSynthesisESpeakNg\|RealSynthesisRhVoice\|RhVoicePlaybackAudition` | **Passed: 3**, **Skipped: 3** (RHVoice preflight not green) |
| Gates | `python scripts/run_verification.py` | **Overall: PASS** (after proof doc wording avoids completion_guard false positive) |
| Quick verify | `.\scripts\verify.ps1 -Quick` | **exit 0** |

Backend base for pytest: `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8030` (implicit or set in environment).

## GPL-3.0

Manifest license for `rhvoice` is GPL-3.0 — operator due diligence (same posture as Slice 12 eSpeak NG).

## Closure standard

Slice 14 is **CLOSED** only when **all** of the following are true:

- `checks.rhvoice.ok == true` on the uvicorn used for proofs.
- `test_real_rhvoice_synthesize_returns_audible_wav` PASS; `routed_engine == "rhvoice"`; WAV non-silent.
- `test_real_rhvoice_primary_audio_file_route_content_type` PASS.
- `RealSynthesisRhVoiceLiveBackendTests` PASS (Inconclusive only for 403 consent / missing fixture — not for wrong engine).
- `RhVoicePlaybackAuditionLiveBackendTests` stream test PASS; playback PASS or Inconclusive (headless).
- Artifacts exist under `docs/reports/verification/slice14/rhvoice/`.
- Regression bar GREEN.
- This doc updated with real command output lines (no invented PASS text).

Until then: **harness only** — matrix row remains **pending runtime PASS** (see `ENGINE_PARITY_MATRIX.md`).
