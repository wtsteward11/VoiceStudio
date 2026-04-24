# Slice 19K — proof session (real-weights + live ladder)

**Date (UTC):** 2026-04-22 (session wall clock local).  
**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md)

## 1. Weight / host truth

| Check | Result |
| --- | --- |
| **Source** | [myshell-ai/OpenVoice](https://huggingface.co/myshell-ai/OpenVoice) (not OpenVoiceV2 tree) — `checkpoints/base_speakers/EN/*` and `checkpoints/converter/*` downloaded via `curl` from `huggingface.co/.../resolve/main/...` into a staging tree, then copied to VoiceStudio layout. |
| `E:\VoiceStudio\models\openvoice\base_speakers\EN\checkpoint.pth` | **~153 MB** (non-placeholder). |
| `E:\VoiceStudio\models\openvoice\converter\checkpoint.pth` | **~125 MB** (non-placeholder). |
| `config.json` siblings | Present (upstream layout). |
| **Worker load (manual probe)** | `OpenVoice version: v1`; **both** `load_ckpt` calls **missing/unexpected keys: [] []** (models load in **`venv_openvoice`**). |

## 2. One backend, one port, one base URL

| Field | Value |
| --- | --- |
| **Uvicorn** | `E:\VoiceStudio\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8042` |
| **PYTHONPATH** | `e:\VoiceStudio` |
| **VOICESTUDIO_MODELS_PATH** | `e:\VoiceStudio\models` |
| **VOICESTUDIO_REAL_XTTS_HTTP_BASE** | `http://127.0.0.1:8042` (pytest + `dotnet test` same value) |

## 3. Preflight gate

- **Artifact:** [`slice19k_preflight_openvoice.json`](slice19k_preflight_openvoice.json) (verbatim `GET /api/health/preflight`).
- **`checks.openvoice.ok`:** **true** (import + **venv_openvoice** + checkpoint layout under `e:\VoiceStudio\models\openvoice\...`).
- **Overall `ok`:** **false** (other engines unchanged — not a 19K blocker for the OpenVoice row).

## 4. Python `real_openvoice`

```text
python -m pytest tests/integration/test_synthesis_openvoice_real.py -m real_openvoice -v --tb=short
```

**Result:** **2 failed, 0 passed** — `POST /api/voice/synthesize` **500** — `"Synthesis failed - engine returned None. Check engine logs for details."`  
- **Example `request_id`:** `ddd09776-52a2-40fb-854a-2a68efc69871` (first test)

**Manual `app.cli.openvoice_worker_synthesize` (same `venv_openvoice`, CPU, real checkpoints):** after Silero VAD cache resolution (see §6), `se_extractor.get_se(..., vad=True)` on **`tests/fixtures/audio/test_440hz_2s.wav`**: **`after vad: dur = 0.0`** → **`Failed to extract speaker embedding: input audio is too short`**. The fixture is a **pure 440 Hz tone** — **not** speech; Silero VAD removes all audio, so **no** embedding and **no** WAV output.

## 5. C# OpenVoice `LiveBackend`

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OpenVoice&TestCategory=LiveBackend" --no-build
```

**Result:** **3 failed, 0 passed** — `BackendServerException: Synthesis failed - engine returned None` (same backend URL as §4).

## 6. Supporting diagnostics (not matrix evidence)

| Topic | Note |
| --- | --- |
| **HuggingFace `huggingface_hub` vs `huggingface.co`** | `router.huggingface.co` **404** in this environment; **`curl`/`https://huggingface.co/.../resolve/...`** succeeded for **myshell-ai/OpenVoice** (v1 checkpoint tree). |
| **Silero VAD hub path** | `se_extractor` error initially referenced `C:\Users\Tyler\.cache\torch\hub\snakers4_silero-vad_master` while **`TORCH_HOME`**-directed cache existed under `e:\VoiceStudio\models\torch\hub\`. **Session fix:** pre-populate VAD via `torch.hub.load` with **`TORCH_HOME=e:\VoiceStudio\models\torch`**; **NTFS directory junction** `C:\Users\Tyler\.cache\torch\hub\snakers4_silero-vad_master` → `e:\VoiceStudio\models\torch\hub\snakers4_silero-vad_master` so the **hard-coded path check** in upstream **`openvoice.se_extractor`** succeeds. (Host-specific; not committed.) |
| **`slice19k_worker_probe.json` / `slice19k_worker_probe_out.wav`** | Operator-only local probe request under `docs/reports/.../slice19/openvoice/` — not a new harness test. |

## 7. `return None` + file contract (Task 7)

**N/A** — HTTP **200** not reached. No **Conclusion A** vs **B** for file-driven `None` + `SynthesisService` in this session (blocker is **before** a valid worker WAV).

## 8. Branch ruling

**Outcome B** — **2/2** and **3/3** not green despite **authentic** checkpoint files on disk; **matrix `openvoice` remains pending**.

**Primary frozen seam (19K):** **`se_extractor` + VAD** — shared integration harness uses **`test_440hz_2s.wav`** (non-speech). With **`vad=True`**, **no** usable speech segment → embedding step fails → worker **exit ≠ 0** or **no** output → **500** / *engine returned None*.

**Secondary (resolved for this host during diagnosis):** Silero VAD **torch.hub** layout path vs `se_extractor` expectations; **`TORCH_HOME` + junction`** as above.

## 9. Regression bar

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors** (session)
- `python scripts/run_verification.py` → **PASS**
- `.\scripts\verify.ps1 -Quick` → **VERIFICATION PASSED** — [`artifacts/verify/20260422_073049/verification_report.md`](../../../artifacts/verify/20260422_073049/verification_report.md)

**Note:** `scripts/check_empty_catches.py` — **`models/`** added to **SKIP_DIRS** so **torch.hub**–cached third-party trees under the operator model root are not scanned as repo source (fixes gate after Silero VAD download under `models/torch/hub/`).
