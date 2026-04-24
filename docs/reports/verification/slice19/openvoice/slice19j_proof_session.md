# Slice 19J — proof session (authentic-weights live ladder)

**Date (UTC):** 2026-04-22 (session wall clock local — commands below from agent host).  
**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md)

## 1. Weight / host truth (before live ladder)

| Check | Result |
| --- | --- |
| `E:\VoiceStudio\models\openvoice\base_speakers\EN\checkpoint.pth` | **2 bytes** (placeholder — not authentic MyShell weights) |
| `E:\VoiceStudio\models\openvoice\converter\checkpoint.pth` | **2 bytes** (placeholder) |
| `config.json` present | Yes (layout satisfies `ensure_openvoice` / `_openvoice_has_checkpoints`) |

**Operator action for Outcome A:** Replace with real OpenVoice v2 EN + converter checkpoints per [MyShell OpenVoice](https://github.com/myshell-ai/OpenVoice) documentation; this session did **not** download or install multi-GB weights into the repo or models tree.

## 2. One backend, one port, one base URL

| Field | Value |
| --- | --- |
| **Uvicorn** | `E:\VoiceStudio\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8041` |
| **PYTHONPATH** | `e:\VoiceStudio` |
| **VOICESTUDIO_MODELS_PATH** | `e:\VoiceStudio\models` |
| **VOICESTUDIO_REAL_XTTS_HTTP_BASE** | `http://127.0.0.1:8041` (pytest + `dotnet test` same shell) |

**Stale-listener note:** do not reuse ad-hoc ports from **19G–19I** without confirming process is current code; this session used **8041** fresh.

## 3. Preflight gate

- **Artifact:** [`slice19j_preflight_openvoice.json`](slice19j_preflight_openvoice.json) (verbatim `GET /api/health/preflight`).
- **`checks.openvoice.ok`:** **true** (import + structural checkpoint layout).
- **Overall `ok`:** **false** (other engines, e.g. `rhvoice` / `chatterbox`, unchanged — not a 19J blocker for OpenVoice row).

## 4. Python `real_openvoice`

```text
python -m pytest tests/integration/test_synthesis_openvoice_real.py -m real_openvoice -v --tb=short
```

**Result:** **2 failed** (not skipped).

- **Failure class:** `POST /api/voice/synthesize` **500** — `"Synthesis failed - engine returned None. Check engine logs for details."`
- **Example `request_id`:** `81a6b8fc-dbcd-47b5-89ca-839cfd309c67` (first test)

## 5. C# OpenVoice `LiveBackend`

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OpenVoice&TestCategory=LiveBackend" --no-build
```

**Result:** **3 failed, 0 passed** — `BackendServerException: Synthesis failed - engine returned None. Check engine logs for details.` (same backend URL as §4).

## 6. File-contract seam (`return None` + `SynthesisService`) — Conclusion B

- **Expected happy path (Conclusion A):** `OpenVoiceSubprocessEngine.synthesize` returns `None` **after** worker writes a valid WAV; `SynthesisService` treats `result is None` + file-ready as success.
- **This session:** Synthesis never reaches a valid file output (invalid / placeholder `checkpoint.pth` → worker or init failure). API returns **500** with *engine returned None* — **not** the file-driven success path. Same **Conclusion B** class as [PROOF §19I](../PROOF_SLICE19_OPENVOICE_AUDITION.md) (placeholder tensors / failed init).

## 7. Branch ruling

**Outcome B** — authentic weights **not** applied on this host; **2/2** and **3/3** **not** green; **matrix `openvoice` remains pending**.

**Frozen seam (primary):** **Runtime weight authenticity** — `load_ckpt` / synthesis cannot succeed with 2-byte placeholder `.pth` files; preflight **structural** green is **insufficient** for matrix PASS.

## 8. Regression bar (no PASS claim)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors**
- `python scripts/run_verification.py` → **PASS**
- `.\scripts\verify.ps1 -Quick` → **VERIFICATION PASSED** — [`artifacts/verify/20260422_062302/verification_report.md`](../../../artifacts/verify/20260422_062302/verification_report.md)
