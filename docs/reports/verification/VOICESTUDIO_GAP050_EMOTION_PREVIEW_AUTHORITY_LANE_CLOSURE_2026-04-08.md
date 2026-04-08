# VOICESTUDIO — GAP-050 Emotion Preview Authority — Lane Closure (2026-04-08)

**Execution row:** [GOV_VOICESTUDIO_GAP050_EMOTION_PREVIEW_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP050_EMOTION_PREVIEW_AUTHORITY_01_EXECUTION_ROW.md) **Closed**  
**Lane type:** **runtime-affecting** — `POST /api/emotion/preview` wired to canonical prosody + transform pipeline; WinUI client contract aligned.

## 1. Goal

Satisfy the residual seam from [VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_CLOSURE_2026-04-08.md](VOICESTUDIO_GAP050_PRODUCT_EXIT_CHECKLIST_CLOSURE_2026-04-08.md) §3: **`/api/emotion/preview` must be authoritative** (same inputs as apply-extended path, `resolve_emotion_prosody` → `apply_transform`, real `audio_id` on success).

## 2. Implementation summary

| Area | Change |
|------|--------|
| Backend | [`backend/api/routes/emotion.py`](../../backend/api/routes/emotion.py) — `POST /api/emotion/preview` accepts `EmotionApplyExtendedRequest`, shares pipeline with `apply-extended` (`transform_context="emotion_preview"`, artifact prefix `emotion_preview_`). |
| Client | [`EmotionControlClient`](../../src/VoiceStudio.App/Services/EmotionControlClient.cs) / [`IEmotionControlClient`](../../src/VoiceStudio.App/Core/Services/IEmotionControlClient.cs) — `PreviewEmotionAsync` returns `EmotionApplyExtendedResponse?`; JSON shape matches apply-extended. |
| UI | [`EmotionControlViewModel`](../../src/VoiceStudio.App/ViewModels/EmotionControlViewModel.cs) — preview playback uses server `AudioUrl` or `/api/audio/file/{id}`; status line uses `ProsodyHandling` when present. |
| Models | [`EmotionControlModels.cs`](../../src/VoiceStudio.App/Core/Models/EmotionControlModels.cs) — preview shares `EmotionApplyExtendedResponse` (dedicated preview DTO removed). |
| OpenAPI | `docs/api/openapi.json` + `tests/contract/.openapi_schema_hash` updated via `python scripts/export_openapi_schema.py --update-hash`. |

**Hard OUT honored:** No synthesis-route side work; no second DSP fork; no startup/shell churn.

**Quarantine note:** Unrelated working-tree drift (e.g. `synthesis.py`, `torch_venv_resolver.py`) was **not** merged into this lane; remains out of scope until a separate row.

## 3. Tests

| Surface | Command / scope | Result |
|---------|-----------------|--------|
| Emotion route | `python -m pytest tests/unit/backend/api/routes/test_emotion.py -q` | **7** PASS |
| CI gate | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** selected PASS (**2** deselected) |
| C# seam / contract | `dotnet test ... --filter "FullyQualifiedName~Emotion"` | **47** PASS / **3** skipped |
| Full App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3195** PASS / **274** skipped |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |

## 4. Verification harness

| Step | Artifact | Result |
|------|-----------|--------|
| Quick verify | `.\scripts\verify.ps1 -Quick` → `artifacts/verify/20260407_205133/` | PASS |
| Rolling verifier | `python scripts/run_verification.py` → `.buildlogs/verification/last_run.json` **20260407-210116** (post-governance sync; matrix era **20260407-205758**) | PASS (**completion_guard** PASS) |

## 5. Binary GAP-050 umbrella decision

**Close GAP-050 (product umbrella).** The product-exit audit identified **one** residual seam (preview authority). With this lane **Closed**, no further named GAP-050 execution row is opened. Future emotion-domain work (e.g. `timeline_curve` application, streaming) → **new** tracker gaps / rows when prioritized.

## 6. Rollback

Revert this lane’s commits; restore stub `preview_emotion` + prior client DTOs; re-open execution row and tracker **GAP-050** if rollback is executed.
