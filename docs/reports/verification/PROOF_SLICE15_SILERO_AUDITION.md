# Slice 15 — Silero TTS bounded parity (audition proof)

**Document state:** **Runtime PASS (2026-04-19)** — `checks.silero.ok == true`, `real_silero` **2/2**, C# Silero filters **3/3**, artifacts under `docs/reports/verification/slice15/silero/`, matrix row **PASS** in [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md).

**Does not claim:** RHVoice / other engines / umbrella TTS closure.

**Plan:** [VOICESTUDIO_BOUNDED_SLICE15_SILERO_PLAN.md](../../design/VOICESTUDIO_BOUNDED_SLICE15_SILERO_PLAN.md)

## Execution row (readiness vs closure)

**Readiness** means `checks.silero.ok == true` (torch + successful `torch.hub` load for `snakers4/silero-models` under preflight policy). **Closure** means matrix **PASS** row + non-skipped `real_silero` + C# proofs + artifacts under `docs/reports/verification/slice15/silero/` — **not** implied for any other engine.

## Operator session (proof host)

| Item | Value |
| --- | --- |
| Backend | `python tools/_uvicorn_slice15_env.py` (uvicorn on **`http://127.0.0.1:8002`**) |
| Client env | `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8002` |
| Models / hub | `VOICESTUDIO_MODELS_PATH=E:\VoiceStudio\models`, `TORCH_HOME=E:\VoiceStudio\models\torch`, `PYTHONPATH=E:\VoiceStudio` |

### Remediation (first green preflight)

1. **Warm `torch.hub`** — ensure `snakers4/silero-models` is present under `%TORCH_HOME%\hub` (one-time network: run `ensure_silero(auto_download=True)` or a successful Silero synthesis with download allowed). Preflight uses **`auto_download=False`** — no silent hub fetch.
2. **Device** — `SileroEngine` defaults to **CPU** (`gpu=False`) and avoids CUDA when the installed PyTorch build does not support the GPU’s compute capability (e.g. newer RTX + older wheels), so synthesis does not fail with an opaque device error.

## Preflight

| Check | Expected |
| --- | --- |
| `GET /api/health/preflight` | `checks.silero.ok` boolean; actionable `message` on failure |

### `GET /api/health/preflight` — `checks.silero` (verbatim — 2026-04-19)

```json
{
  "ok": true,
  "paths": [
    "E:\\VoiceStudio\\models\\torch\\hub"
  ],
  "downloaded": false,
  "message": "Silero TTS ready (language=en, speaker=v3_en)",
  "model_id": "v4",
  "language": "en"
}
```

### Probe mirror (`VOICESTUDIO_ENGINE_PROBE_FULL=1`)

`docs/reports/verification/slice15/engine_readiness_probe.json` — `timestamp_utc` **2026-04-19T15:15:53.911195+00:00** — `engines["silero"].preflight_assets`:

```json
"preflight_assets": {
  "ok": true,
  "paths": [
    "E:\\VoiceStudio\\models\\torch\\hub"
  ],
  "downloaded": false,
  "message": "Silero TTS ready (language=en, speaker=v3_en)",
  "model_id": "v4",
  "language": "en"
}
```

## Python (`pytest -m real_silero`)

| Step | Command | Result |
| --- | --- | --- |
| Opt-in real Silero | `python -m pytest tests/integration/test_synthesis_silero_real.py -m real_silero --tb=short` | **2 passed** in ~7s (not skipped) |

### Verbatim (2026-04-19)

```text
======================== 2 passed, 2 warnings in 6.94s ========================
```

## C# (live backend)

| Step | Command | Result |
| --- | --- | --- |
| All Silero live tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Silero"` | **Passed: 3**, Failed: 0, Skipped: 0 |

### Verbatim (2026-04-19)

```text
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: ~9 s - VoiceStudio.App.Tests.dll
```

## Artifacts

| Path | Description |
| --- | --- |
| `docs/reports/verification/slice15/silero/silero_output.wav` | From successful `real_silero` pytest |
| `docs/reports/verification/slice15/silero/silero_backend_log_snippet.txt` | Log / metadata snippet |
| `docs/reports/verification/slice15/silero/silero_csharp_stream.wav` | C# stream test output |

## Regression bar (closure)

| Step | Command |
| --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` |
| Python gates | `pytest` default suite; optional `real_xtts` / `real_piper` / `real_espeak_ng` / `real_silero` with live backend |
| Verify | `python scripts/run_verification.py` |
| Quick | `.\scripts\verify.ps1 -Quick` |

### Verbatim (2026-04-19)

**Build**

```text
Build succeeded.
    0 Error(s)
(Time varies; warnings from existing projects only.)
```

**`python scripts/run_verification.py`**

```text
  Overall: PASS
  JSON: E:\VoiceStudio\.buildlogs\verification\last_run.json
```

**`.\scripts\verify.ps1 -Quick`**

```text
  VERIFICATION PASSED
Report: E:\VoiceStudio\artifacts\verify\20260419_102144\verification_report.md
```

**Optional opt-in regressions (same backend `8002`, `VOICESTUDIO_REAL_XTTS_HTTP_BASE` set)**

| Marker | Result |
| --- | --- |
| `pytest tests/integration/test_synthesis_xtts_real.py -m real_xtts` | **2 passed** |
| `pytest tests/integration/test_synthesis_piper_real.py -m real_piper` | **2 passed** |
| `pytest tests/integration/test_synthesis_espeak_ng_real.py -m real_espeak_ng` | **2 passed** |

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-19 | **Runtime PASS:** preflight `checks.silero.ok`; probe `slice15` `2026-04-19T15:15:53Z` `preflight_assets.ok: true`; `real_silero` **2/2**; C# **3/3**; matrix **PASS**; `run_verification.py` + `verify.ps1 -Quick` PASS; optional `real_xtts` / `real_piper` / `real_espeak_ng` **2/2** each vs `http://127.0.0.1:8002`. |
| 2026-04-19 | Harness + `ensure_silero` landed; earlier probe `2026-04-19T04:15:17Z` showed `preflight_assets.ok: false` (hub cache miss) — superseded after hub warm + engine CPU default. |
| 2026-04-19 | `empty_catch_check`: replaced silent `except: pass` after `torch.cuda.empty_cache()` with `logger.debug` in both `model_preflight` modules. |
