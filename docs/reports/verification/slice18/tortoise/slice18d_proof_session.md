# Slice 18D — Tortoise live proof session (single backend, one URL)

**UTC session date:** 2026-04-20  
**Git:** `main` @ `857da0604d8145f7fd61e26354cdd331cd99da50` (record at session time; amend if cherry-picked).

## Authoritative backend

| Field | Value |
| --- | --- |
| Base URL | `http://127.0.0.1:8028` |
| Process | Single `uvicorn backend.api.main:app --host 127.0.0.1 --port 8028` from repo root |
| `PYTHONPATH` | `E:\VoiceStudio` |
| `VOICESTUDIO_MODELS_PATH` | `E:\VoiceStudio\models` |
| `VOICESTUDIO_TORTOISE_DEVICE` | `cpu` |
| `VOICESTUDIO_TEST_MODE` | **Unset** on backend |
| Stress env (optional) | `HF_ENDPOINT` / `HF_INFERENCE_API_BASE` = `https://router.huggingface.co` — Tortoise worker child rewrites router host to `https://huggingface.co` (`TortoiseSubprocessEngine._worker_environ`) |

## Preflight (live)

`GET http://127.0.0.1:8028/health` → **200**, `engines_ready` **true**.

`GET http://127.0.0.1:8028/api/health/preflight` → `checks.tortoise.ok` **true** (sample: models_dir under `VOICESTUDIO_MODELS_PATH\tortoise_models`, `venv_tortoise` python).

## Python

```powershell
$env:PYTHONPATH = "E:\VoiceStudio"
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8028"
python -m pytest tests/integration/test_synthesis_tortoise_real.py -v -m real_tortoise --tb=short
```

**Result:** **2 passed**, **0 skipped** (~21 min wall; per-test Tortoise CPU synthesis).

## C#

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8028"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Tortoise&TestCategory=LiveBackend"
```

**Result:** **3 passed**, **0 skipped** (~34 min wall).

## Artifacts

| Path | Role |
| --- | --- |
| [`tortoise_output.wav`](tortoise_output.wav) | Python `real_tortoise` audible WAV |
| [`tortoise_backend_log_snippet.txt`](tortoise_backend_log_snippet.txt) | Python proof metadata |
| [`tortoise_csharp_stream.wav`](tortoise_csharp_stream.wav) | C# stream seam WAV |

## Harness / engine notes (18D)

- **Timeouts:** CPU Tortoise exceeded legacy **900s** per job; `pytest` marks + `httpx` client + `VOICESTUDIO_TORTOISE_SUBPROCESS_TIMEOUT_SEC` default aligned to **2400s**; C# `BackendClientConfig.RequestTimeout` **40 min**; preflight probe `HttpClient` **120s** (slow `ensure_tortoise` subprocess under load).
- **HF router:** Worker-only rewrite of `router.huggingface.co` → `https://huggingface.co` for Hub file fetches (404 when left on router).

Canonical narrative: [PROOF_SLICE18_TORTOISE_AUDITION.md](../PROOF_SLICE18_TORTOISE_AUDITION.md) §Slice 18D.
