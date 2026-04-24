# PROOF — Slice 17 — Chatterbox readiness and runtime parity

**Document state:** **Slice 17D (2026-04-20) — runtime parity PASS** on dedicated-port backend with green preflight — see §Slice 17D.

**Does not claim:** RHVoice closure, other engines, umbrella TTS.

**Contract:** [`VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md`](../design/VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md) — **Slice 17A venv surface:** [`VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md`](../design/VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md).

## Slice 17A (2026-04-20) — subprocess-aligned `ensure_chatterbox`

| Item | Detail |
| --- | --- |
| **Change** | `ensure_chatterbox` no longer imports `chatterbox.tts` in the FastAPI worker process. It resolves **`venv_advanced_tts`** via `VenvFamilyManager` and runs **import + Hugging Face cache** checks in **that** interpreter (subprocess), matching the manifest runtime. |
| **Proof host outcome** | `venv_advanced_tts` **not provisioned** on this machine → `checks.chatterbox` red with `reason: venv_advanced_tts_not_created` (see §Preflight sample 17A). Earlier Slice 17 **import/conformer** narrative remains valid for hosts that had a default venv but no advanced family venv. |
| **Operator unblock** | Create **`venv_advanced_tts`** (`scripts/engines/create_engine_venv.py`, Advanced TTS family), then `pip install chatterbox-tts` (+ resolve pins), warm HF **`ResembleAI/chatterbox`**, re-run preflight/probe. |

## Slice 17B (2026-04-20) — `runtime/venvs/torch26` provision + preflight green

| Item | Detail |
| --- | --- |
| **Path alignment** | `VenvFamilyManager.get_venv_path(VenvFamily.ADVANCED_TTS)` → **`runtime/venvs/torch26`** (same as `create_engine_venv` family `torch26`). Manifest `venv_family` remains **`venv_advanced_tts`** (logical name). |
| **Provision** | `python scripts/engines/create_engine_venv.py --family torch26 --force` — uses Windows **Python 3.11** when available (`get_python_executable`); **staged pip** (numpy/torch stack, then `chatterbox-tts`) avoids `pkuseg` build without numpy. |
| **HF** | `huggingface_hub` may 404 via default router; **`ensure_chatterbox` subprocess** sets `HF_ENDPOINT` default to `https://huggingface.co` when unset. |
| **`ensure_chatterbox`** | **PASS** in-process: `ok: true`, `python_exe` = `runtime\venvs\torch26\Scripts\python.exe`. |
| **`GET /api/health/preflight`** | **`checks.chatterbox.ok == true`** on a backend started from **current** repo code (e.g. `py -3.11 -m uvicorn backend.api.main:app --port 8001`). Older long-running listeners may omit `checks.chatterbox` entirely. |
| **Probe** | [`slice17/engine_readiness_probe.json`](slice17/engine_readiness_probe.json) **`timestamp_utc` `2026-04-20T11:55:08.699504+00:00`**, **`mode`:** `manifest_scan_plus_router_chatterbox_preflight_only` — Chatterbox **`preflight_assets.ok: true`**; **`instantiable: false`** (unchanged from prior snapshot; API worker does not host the package). |
| **`pytest -m real_chatterbox`** | **Skipped** after **`POST /api/voice/synthesize` → HTTP 500** — Chatterbox engine **not initialized** in `EngineRouter` in the API process (`Engine failed to initialize`), not a preflight failure. |
| **C#** (`RealSynthesisChatterbox` / `ChatterboxPlaybackAudition`) | **Fail** with same synthesis **500** / engine-unavailable posture when run against that backend. |
| **Pins (observed)** | See [`slice17/pip_show_chatterbox_stack.txt`](slice17/pip_show_chatterbox_stack.txt) (`pip show` in `torch26`). |
| **Drift route** | `backend/api/routes/drift.py` import corrected to `backend.services.model_drift_detector` so **uvicorn** can load the full app (blocked startup before). |

**Next bounded blocker (matrix runtime PASS):** wire **synthesis** so **`chatterbox`** runs in the **family venv** (or otherwise initializes successfully in the router), then re-run `real_chatterbox` + C# proofs.

## Execution row

| Stage | Result (Slice 17B — 2026-04-20) |
| --- | --- |
| `ensure_chatterbox` + `checks.chatterbox` | **Green** — family venv at **`runtime/venvs/torch26`**; subprocess import + HF probe **PASS** |
| `GET /api/health/preflight` | **`checks.chatterbox.ok: true`** on current-code backend (see §Slice 17B) |
| Probe `slice17/engine_readiness_probe.json` | **`timestamp_utc` `2026-04-20T11:55:08.699504+00:00`** — `preflight_assets.ok` **true** (Chatterbox-only refresh mode); `instantiable` **false** |
| `pytest -m real_chatterbox` | **Skipped** — synthesis **500** (engine not initialized in router), not preflight |
| C# `RealSynthesisChatterbox` / `ChatterboxPlaybackAudition` | **Fail** — same synthesis/engine-init posture |

## First blocker (proof host — isolated)

On the development venv used for this slice, **`chatterbox.tts` import fails** before HF cache is relevant: missing transitive dependencies (e.g. `conformer`, earlier `s3tokenizer`) — pip resolver reports **chatterbox-tts 0.1.6** pins **torch==2.6.0**, **torchaudio==2.6.0**, **numpy&lt;1.26**, etc., which **conflict** with the repo’s current torch stack. **Exact first seam:** Python import of `chatterbox.tts` → dependency resolution / dedicated **`venv_advanced_tts`** install per manifest, not VoiceStudio routing.

**Remediation (operator):** Install **`chatterbox-tts` and its declared deps** into the **advanced TTS venv** (see `engines/audio/chatterbox/engine.manifest.json` `venv_family`), warm **Hugging Face** cache for **`ResembleAI/chatterbox`** (`ve.safetensors` probe), restart backend, confirm `checks.chatterbox.ok == true`, then re-run `real_chatterbox` + C# filters.

## Preflight sample (`checks.chatterbox` red — import failure)

```json
{
  "ok": false,
  "downloaded": false,
  "message": "chatterbox-tts import failed (ModuleNotFoundError: No module named 'conformer'). Install chatterbox-tts and its dependencies in the advanced TTS venv (see engines/audio/chatterbox/engine.manifest.json).",
  "status_code": 503
}
```

## Preflight sample 17A (`checks.chatterbox` red — `venv_advanced_tts` missing)

When the Advanced TTS family venv is not created, preflight fails fast with a boolean result and explicit reason (no `ok: null`):

```json
{
  "ok": false,
  "downloaded": false,
  "message": "Chatterbox requires the venv_advanced_tts virtual environment (engines/audio/chatterbox/engine.manifest.json). Create it with scripts/engines/create_engine_venv.py for the Advanced TTS family, then pip install chatterbox-tts into that venv.",
  "status_code": 503,
  "reason": "venv_advanced_tts_not_created"
}
```

## Regression bar (repo hygiene)

Recorded after implementation edits:

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (run in closure step)
- `python scripts/run_verification.py` — **Overall PASS**
- `.\scripts\verify.ps1 -Quick` — **VERIFICATION PASSED** — Slice 17A closure: [`artifacts/verify/20260419_230144/verification_report.md`](../../../artifacts/verify/20260419_230144/verification_report.md) (prior Slice 17 run: [`20260419_162115`](../../../artifacts/verify/20260419_162115/verification_report.md))

## Python — `real_chatterbox`

**Tests:** `tests/integration/test_synthesis_chatterbox_real.py`

```text
VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:<port> python -m pytest tests/integration/test_synthesis_chatterbox_real.py -v -m real_chatterbox --tb=short
```

## C# — live backend

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RealSynthesisChatterbox|FullyQualifiedName~ChatterboxPlaybackAudition"
```

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-20 | Initial proof: `ensure_chatterbox`, health + probe wiring, harness tests; **blocker** = chatterbox-tts deps on proof host; matrix **pending** PASS. |
| 2026-04-20 | **Slice 17A:** `ensure_chatterbox` uses **`venv_advanced_tts` subprocess** + contract [VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md](../design/VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md); probe refresh **`2026-04-20T03:56:14.058108+00:00`** — first blocker on this host = **venv not provisioned**; matrix still **pending**. |
| 2026-04-20 | **Slice 17B:** `torch26` venv provisioned; `ADVANCED_TTS` → `runtime/venvs/torch26`; preflight **green**; probe **`2026-04-20T11:55:08.699504+00:00`**; synthesis/router init still **blocking** `real_chatterbox` / C# — matrix **pending** runtime PASS. |
| 2026-04-20 | **Slice 17C:** `ChatterboxTorch26Engine` + worker CLI (Model B); matrix/runtime WAV proofs **pending** live session — see §Slice 17C. |
| 2026-04-20 | **Slice 17D:** Dedicated port **8027** + `VOICESTUDIO_REAL_XTTS_HTTP_BASE`; preflight green; `real_chatterbox` **2/2**; C# **3/3**; worker `HF_ENDPOINT` canonical; IEEE float WAV handling in Python + C# proofs; matrix **`chatterbox` PASS** — see §Slice 17D. |

## Slice 17C — router/runtime initialization (Model B)

**Authoritative runtime model (single sentence):** Chatterbox **synthesis** runs in the **`VenvFamily.ADVANCED_TTS`** interpreter (`runtime/venvs/torch26`); the FastAPI **API worker is not required to import `chatterbox.tts`** — synthesis is delegated via **`python -m app.cli.chatterbox_worker_synthesize`** using that family `python.exe`.

### Task 1 — First failing point (frozen)

| Layer | Finding |
| --- | --- |
| **Route** | `POST /api/voice/synthesize` → `SynthesisService.synthesize` → `engine_router.get_engine("chatterbox")`. |
| **Router** | [`EngineRouter.get_engine`](../../../app/core/engines/router.py) instantiates the manifest `entry_point` class in the **API process** and calls `initialize()`. |
| **Prior manifest** | `app.core.engines.chatterbox_engine.ChatterboxEngine` imports `chatterbox.tts` at **module load**; when the package is absent from the API venv, `ChatterboxTTS` is `None` and **`__init__` raises `ImportError`** → `get_engine` logs `Failed to initialize engine 'chatterbox': ...` and returns **`None`** → **`EngineUnavailableException` / HTTP 500** (“Engine failed to initialize”). |
| **Not the first seam** | Preflight **`ensure_chatterbox`** (Slice 17A/17B) already proved import + HF in **torch26**; the gap was **in-process** adapter vs family venv. |

### Task 3 — Implementation

| Artifact | Role |
| --- | --- |
| [`app/cli/chatterbox_worker_synthesize.py`](../../../app/cli/chatterbox_worker_synthesize.py) | One-shot worker: loads `ChatterboxTTS` **only** under torch26; reads JSON request path; writes WAV. |
| [`app/core/engines/chatterbox_torch26_engine.py`](../../../app/core/engines/chatterbox_torch26_engine.py) | `ChatterboxTorch26Engine`: **no** `chatterbox` import; resolves family `python.exe`; `subprocess.run` (no `shell=True`) with `PYTHONPATH` = repo root. |
| [`engines/audio/chatterbox/engine.manifest.json`](../../../engines/audio/chatterbox/engine.manifest.json) | `entry_point` → `ChatterboxTorch26Engine`. Legacy [`chatterbox_engine.py`](../../../app/core/engines/chatterbox_engine.py) retained for reference/tests. |

### Task 4–6 — Runtime proofs + regression

| Stage | Result (2026-04-20 — implementation session) |
| --- | --- |
| `pytest -m real_chatterbox` | **SKIPPED** — `checks.chatterbox.ok` not true at `VOICESTUDIO_REAL_XTTS_HTTP_BASE` (`http://127.0.0.1:8000`); use a current-code backend with green Chatterbox preflight to obtain WAV + PASS lines. |
| C# `RealSynthesisChatterbox` / `ChatterboxPlaybackAudition` | **3 skipped** (preflight gate). |
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| `python scripts/run_verification.py` | **PASS** |
| `.\scripts\verify.ps1 -Quick` | **VERIFICATION PASSED** — [`artifacts/verify/20260420_073030/verification_report.md`](../../../artifacts/verify/20260420_073030/verification_report.md) |

**Honest closure (superseded by §Slice 17D):** ~~`chatterbox` **matrix row stays pending**~~ — **Slice 17D** recorded PASS lines + artifacts — see **§Slice 17D** below.

## Slice 17D (2026-04-20) — live proof on current-code backend (dedicated port)

**Goal:** One authoritative base URL for preflight + `pytest -m real_chatterbox` + C# Chatterbox filters — no stale `:8000` ambiguity (`VOICESTUDIO_REAL_XTTS_HTTP_BASE` set for every command).

| Item | Detail |
| --- | --- |
| **Backend** | `py -3.11 -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8027` from repo root (same tree as proof). |
| **Session env** | `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8027` for pytest and `dotnet test`. |
| **Preflight** | `GET /health` → 200, `engines_ready: true`; `GET /api/health/preflight` → `checks.chatterbox.ok: true`. |
| **Worker HF seam** | `ChatterboxTorch26Engine._worker_environ` always sets `HF_ENDPOINT` to `https://huggingface.co` (override `VOICESTUDIO_CHATTERBOX_WORKER_HF_ENDPOINT`) so torch26 worker does not inherit a bad `HF_ENDPOINT` (e.g. `router.huggingface.co` → 404 on weights). |
| **IEEE float WAV** | Chatterbox worker may emit **WAVE_FORMAT_IEEE_FLOAT** (32-bit) WAV; live proofs updated: Python `_wav_duration_and_peak` handles format tag 3; C# uses [`LiveBackendWavInspection`](../../../src/VoiceStudio.App.Tests/Helpers/LiveBackendWavInspection.cs) for peak in 16-bit-equivalent scale. |

### Commands (exact)

```text
set VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8027
python -m pytest tests/integration/test_synthesis_chatterbox_real.py -v -m real_chatterbox --tb=short
```

```text
set VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8027
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RealSynthesisChatterbox|FullyQualifiedName~ChatterboxPlaybackAudition"
```

### Outcomes

| Stage | Result |
| --- | --- |
| `pytest -m real_chatterbox` | **2/2 PASS** |
| C# `RealSynthesisChatterbox` + `ChatterboxPlaybackAudition` | **3/3 PASS** |

### Artifacts

| Artifact | Path |
| --- | --- |
| Python proof WAV + snippet | [`slice17/chatterbox/chatterbox_output.wav`](slice17/chatterbox/chatterbox_output.wav), [`slice17/chatterbox/chatterbox_backend_log_snippet.txt`](slice17/chatterbox/chatterbox_backend_log_snippet.txt) |
| C# stream WAV | [`slice17/chatterbox/chatterbox_csharp_stream.wav`](slice17/chatterbox/chatterbox_csharp_stream.wav) |

### Regression bar (closure session)

Recorded after Slice 17D code + doc updates: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors**; `python scripts/run_verification.py` → **PASS**; `.\scripts\verify.ps1 -Quick` → **VERIFICATION PASSED** — [`artifacts/verify/20260420_100103/verification_report.md`](../../../artifacts/verify/20260420_100103/verification_report.md).
