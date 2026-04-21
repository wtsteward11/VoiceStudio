# PROOF — Bounded Slice 18 — Tortoise readiness + optional runtime parity

**Date:** 2026-04-20  
**Contract:** [VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md)

## Outcome (latest settled — Slice 18D)

**Runtime parity PASS (live HTTP, dedicated port):** One uvicorn on **`http://127.0.0.1:8028`**, same `VOICESTUDIO_REAL_XTTS_HTTP_BASE` for Python + C#. **`GET /api/health/preflight`** → **`checks.tortoise.ok: true`**. **`pytest -m real_tortoise`** → **2 passed, 0 skipped**. **`dotnet test`** Tortoise `LiveBackend` filter → **3 passed, 0 skipped**. Artifacts + operator log: [`slice18/tortoise/`](slice18/tortoise/) including [`slice18d_proof_session.md`](slice18/tortoise/slice18d_proof_session.md). Matrix: [`ENGINE_PARITY_MATRIX.md`](ENGINE_PARITY_MATRIX.md) **`tortoise` → PASS**.

**Historical (audit trail):** Initial **Outcome B** (`ModuleNotFoundError: tortoise` in backend `.venv`); **Slice 18A** `transformers` schism; **Slice 18B** ADR-052 + probe **424** (weights cache) — superseded for **matrix** row once **18D** live proofs ran with warm `tortoise_models` + `venv_tortoise`.

## Slice 18D — dedicated-port runtime parity PASS (2026-04-20)

| Gate | Evidence |
| --- | --- |
| Backend | `http://127.0.0.1:8028` — `PYTHONPATH` = repo root; `VOICESTUDIO_MODELS_PATH` = `E:\VoiceStudio\models`; `VOICESTUDIO_TORTOISE_DEVICE=cpu`; **`VOICESTUDIO_TEST_MODE` unset** on server |
| `/health` | **200**, `engines_ready: true` |
| Preflight | **`checks.tortoise.ok: true`** (live JSON) |
| Python | `pytest tests/integration/test_synthesis_tortoise_real.py -m real_tortoise` → **2 passed, 0 skipped** (~1263s session) |
| C# | `--filter "FullyQualifiedName~Tortoise&TestCategory=LiveBackend"` → **3 passed, 0 skipped** (~2046s) |
| Artifacts | `slice18/tortoise/tortoise_output.wav`, `tortoise_backend_log_snippet.txt`, `tortoise_csharp_stream.wav` |

**Engine / harness notes**

- **`TortoiseSubprocessEngine._worker_environ`:** parent `HF_ENDPOINT` / `HF_INFERENCE_API_BASE` pointing at **`router.huggingface.co`** is rewritten to **`https://huggingface.co`** in the **worker child** so Hub file URLs do not 404.
- **Timeouts:** CPU Tortoise exceeded **900s** per job in session; default `VOICESTUDIO_TORTOISE_SUBPROCESS_TIMEOUT_SEC` + `real_tortoise` pytest/`httpx` aligned to **2400s**; C# `BackendClientConfig.RequestTimeout` **40 min**; Tortoise live preflight probe `HttpClient` **120s**.

## Probe artifact (static / supplementary)

- **Path:** [`slice18/engine_readiness_probe.json`](slice18/engine_readiness_probe.json) (mirrored from primary `slice12` probe run)  
- **Historical timestamps:** e.g. `2026-04-20T16:48:45.543576+00:00` (initial Outcome B) — **not** a substitute for live **`GET /api/health/preflight`** on the proof-session backend.  
- **Command (operator):** from repo root, `.venv` or backend interpreter on `PYTHONPATH`:

```powershell
cd E:\VoiceStudio
$env:VOICESTUDIO_ENGINE_PROBE_FULL = '1'
python scripts/engine_readiness_probe.py
```

## Regression bar (post–18D governance sync)

Record current artifacts in [`.cursor/STATE.md`](../../.cursor/STATE.md) after each bar run:

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors**
2. `python scripts/run_verification.py` → **PASS** (`.buildlogs/verification/last_run.json`)
3. `.\scripts\verify.ps1 -Quick` → **VERIFICATION PASSED** [`artifacts/verify/20260420_192459/verification_report.md`](../../artifacts/verify/20260420_192459/verification_report.md) *(2026-04-20 post-governance sync)*

`pytest tests/unit/backend/services/test_model_preflight.py` → **8 passed** (environment-dependent optional skips may still apply on hosts without torch stacks).

## Slice 18A follow-up (2026-04-20)

Provisioning **`tortoise-tts`** into the backend interpreter was attempted under [PROOF_SLICE18A_TORTOISE_PROVISIONING.md](PROOF_SLICE18A_TORTOISE_PROVISIONING.md). **Outcome B:** a **second blocker** was isolated — **`transformers` version/API conflict** between **`tortoise-tts`** (pins `transformers==4.31.0`) and **`coqui-tts` / XTTS** (requires `transformers>=4.52.1`). The `tortoise` package was **not** left installed. Matrix row for **`tortoise`** was **pending** until **Slice 18D** runtime proof — **now PASS** (see Outcome above); this paragraph is **historical** provisioning narrative only.

## Slice 18B — isolated venv + subprocess bridge

**Matrix note:** **Slice 18D** records **PASS** for `tortoise` on the operator session (`ENGINE_PARITY_MATRIX.md`); **18B** remains the **implementation** narrative (ADR-052).

**Implementation landed:** 2026-04-18 (ADR-052 merge). **Evidence rows below** use probe timestamps (e.g. 2026-04-20) — not a contradiction.

**ADR:** [ADR-052](../../architecture/decisions/ADR-052-tortoise-isolated-venv-subprocess.md). **Runtime:** `runtime/venvs/tortoise` provisioned via `python scripts/engines/create_engine_venv.py --family tortoise`; **`ensure_tortoise`** validates **`venv_tortoise`** (`from tortoise.api import TextToSpeech` in subprocess); FastAPI **`.venv`** remains XTTS/Coqui authority. **Router:** `TortoiseSubprocessEngine` + `app/cli/tortoise_worker_synthesize.py`.

**Probe refresh:** [`slice18/engine_readiness_probe.json`](slice18/engine_readiness_probe.json) **`timestamp_utc`:** **`2026-04-20T18:35:46.064196+00:00`**. **`router.engines.tortoise.preflight_assets`:** `ok: false`, **`status_code`:** **`424`** — **no cached weights** under `E:\VoiceStudio\models\tortoise_models` (import path resolved; **next single blocker** = warm/cache weights in isolated venv, not dependency schism). **Matrix `tortoise`:** was **pending** at probe time; **Slice 18D** recorded **PASS** (see Outcome above). This probe row remains a **historical** readiness snapshot.

## Changelog

| Date | Notes |
| --- | --- |
| 2026-04-20 | **Slice 18D:** Dedicated port **8028**; `real_tortoise` **2/2**; C# Tortoise **3/3**; artifacts + [`slice18d_proof_session.md`](slice18/tortoise/slice18d_proof_session.md); matrix **`tortoise` → PASS**; HF router sanitization + timeout alignment (subprocess + pytest + C#). |
| 2026-04-18 | **Slice 18B:** ADR-052; `venv_tortoise` + subprocess; probe `2026-04-20T18:35:46.064196+00:00` — preflight **424** (weights cache); matrix **pending**. |
| 2026-04-20 | **Slice 18A:** see [PROOF_SLICE18A_TORTOISE_PROVISIONING.md](PROOF_SLICE18A_TORTOISE_PROVISIONING.md) — coqui vs tortoise `transformers` conflict; matrix still **pending**. |
| 2026-04-20 | Initial proof: Outcome B; `ModuleNotFoundError: tortoise`; matrix remains **pending**. |
