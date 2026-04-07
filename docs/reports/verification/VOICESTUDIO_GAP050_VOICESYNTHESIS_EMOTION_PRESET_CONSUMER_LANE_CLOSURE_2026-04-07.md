# VOICESTUDIO — GAP-050 bounded slice: Voice Synthesis emotion preset consumer — Lane closure

**Date:** 2026-04-07  
**Execution row:** [GOV_VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_01_EXECUTION_ROW.md)  
**Tracker:** [GAP-050](../../design/PROFESSIONAL_GAP_TRACKER.md) — **product umbrella remains Open**; this document closes the **Voice Synthesis panel consumer** slice only (depends on mapping lane + GAP-023 authority).

## 1. Goal

Wire the Voice Synthesis panel to the canonical preset authority: **base** `POST /api/voice/synthesize` → **`IEmotionControlClient.ApplyEmotionAsync`** (`/api/emotion/apply-extended`) for presets `neutral`, `warm`, `energetic`, `calm`, with **no** ViewModel-local preset→prosody math, no engine `emotion` double-stack for those presets, and **one** combined capability warning toast (SSML + prosody + preset-apply failure copy).

## 2. Code truth

| Area | Artifact |
|------|-----------|
| Orchestration | `src/VoiceStudio.App/Services/VoiceSynthesisService.cs` — canonical preset normalization, strips `Emotion` on shaped synthesis request, merges apply-extended response |
| Response model | `src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs` — `VoiceSynthesisResponse.ProsodyHandling`, `EmotionMappingSource`, `EmotionPresetApplyFailureMessage` |
| UI | `VoiceSynthesisView.xaml` — `ItemsSource` = `CanonicalEmotionPresets` |
| ViewModel | `VoiceSynthesisViewModel.cs` — `IsEmotionSupported` when profile selected; `BuildSynthesisCapabilityCombinedNotice` toast |
| Translator | `ActionableErrorTranslator.cs` — `BuildProsodyHandlingUserNotice`, `BuildSynthesisCapabilityCombinedNotice` |
| Toast contract | `IToastNotificationService` extended with `ShowSuccess` / `ShowWarning` / `ShowError` for test doubles |
| Tests | `VoiceSynthesisServiceTests.cs`, `VoiceSynthesisViewModelTests.cs`, `VoiceSynthesisSsmlDiagnosticsTests.cs` |

## 3. Verification matrix (lane)

| Step | Command | Result |
|------|---------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| App tests (synthesis seam) | `dotnet test src/VoiceStudio.App.Tests/... --filter "FullyQualifiedName~VoiceSynthesis"` | PASS (**49** passed; **22** skipped UI) |
| Full App.Tests | `dotnet test src/VoiceStudio.App.Tests/...` | PASS (**3185** passed / **274** skipped) |
| CI pytest | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** passed (**2** deselected) |
| XAML | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260407_073454/` |
| Rolling verifier | `python scripts/run_verification.py` | `.buildlogs/verification/last_run.json` **20260407-074018** — **completion_guard** PASS. |

**Note (Windows / collection):** Invoking `tests/unit/backend/api/routes/test_emotion.py` in isolation can shadow the repo `backend` package with `tests.unit.backend`. Anti-regression for `/api/emotion/apply-extended` remains enforced via **`pytest tests/ci`** and the prior GAP-050 mapping lane suite run from full `tests/` collection in CI.

## 4. Honesty / diagnostics

- Preset apply failure sets `emotion_preset_apply_failure_message`; base synthesis audio ids remain valid.
- Success path merges `prosody_handling` + `emotion_mapping_source` from apply-extended into `VoiceSynthesisResponse`.
- UI uses a **single** `ShowWarning` for combined SSML + prosody + preset-failure narrative (plus one success toast).

## 5. Hard OUT (unchanged)

Per execution row: no new emotion ML, second DSP path, streaming rewrite, shell changes, broad synthesis UX redesign, `IBackendClient` on `VoiceSynthesisViewModel`.

## 6. Rollback

Revert this lane’s commits only; preserve GAP-050 mapping lane, GAP-023 authority, and GAP-064 translator patterns.
