# GOV-VOICESTUDIO-GAP050-VOICESYNTHESIS-EMOTION-PRESET-CONSUMER-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_01`  
**Status:** **Closed** (2026-04-07) — verification + governance sync complete  
**Tracker:** [GAP-050](PROFESSIONAL_GAP_TRACKER.md) — bounded consumer slice (product umbrella **Open**)  
**Lane type:** **runtime-affecting** (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md))  
**Depends on:** [GOV-VOICESTUDIO-GAP050-EMOTION-PRESET-PROSODY-MAPPING-01](GOV_VOICESTUDIO_GAP050_EMOTION_PRESET_PROSODY_MAPPING_01_EXECUTION_ROW.md) — `/api/emotion/apply-extended` + `emotion_preset_prosody_mapper` + GAP-023 `apply_transform`

## Problem statement

Voice Synthesis panel exposed free-form emotion strings and passed `emotion` into `/api/voice/synthesize`, duplicating affect semantics with the canonical preset→prosody authority. There was no consumer path that chained **base synthesis** → **emotion apply-extended** with honest `prosody_handling` / `emotion_mapping_source` in the panel response.

## Frozen architecture decisions

1. **Preset set (UI):** Canonical four — `neutral`, `warm`, `energetic`, `calm` — aligned with GAP-050 mapper.
2. **DSP / mapping:** No ViewModel-local pitch/rate/volume math for presets. All preset prosody comes from backend `resolve_emotion_prosody` via `POST /api/emotion/apply-extended` only.
3. **Orchestration:** [IVoiceSynthesisService](../../src/VoiceStudio.App/Services/IVoiceSynthesisService.cs) / [VoiceSynthesisService](../../src/VoiceStudio.App/Services/VoiceSynthesisService.cs) — `SynthesizeVoiceAsync` calls backend synthesis, then when a canonical preset is selected, calls [IEmotionControlClient.ApplyEmotionAsync](../../src/VoiceStudio.App/Core/Services/IEmotionControlClient.cs) (transport only; **no** `IBackendClient` on `VoiceSynthesisViewModel`).
4. **Double-stack guard:** For canonical presets, **omit** `emotion` on the synthesis request to the engine so prosody is not applied twice (engine hint + authority).
5. **Diagnostics:** Extend [VoiceSynthesisResponse](../../src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs) with `prosody_handling`, `emotion_mapping_source`, and an explicit optional `emotion_preset_apply_failure_message` when post-apply fails after successful base synth.
6. **UI honesty:** [VoiceSynthesisViewModel](../../src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs) surfaces SSML + prosody/preset notices in **one** warning narrative where applicable ([ActionableErrorTranslator](../../src/VoiceStudio.App/Utilities/ActionableErrorTranslator.cs)).

## Contract (chosen)

- **Preset selection:** `VoiceSynthesisRequest.Emotion` carries the selected preset label for orchestration; backend synthesis request uses `emotion: null` when preset is canonical.
- **Intensity:** Fixed **100** for primary on apply-extended for this slice (matches full-strength preset UX).
- **Post-apply failure:** Base synthesis audio is retained; user sees an explicit failure message (no silent success).

## HTTP policy (client-observed)

| Condition | Behavior |
|-----------|----------|
| Synthesis fails | Propagate existing mapped exceptions |
| Synthesis OK, apply-extended fails / null | Return base audio + `emotion_preset_apply_failure_message` |

## Acceptance contract (Close)

- [x] `VoiceSynthesisViewModel` does not compute preset pitch/rate/volume locally.
- [x] Canonical preset path invokes `IEmotionControlClient.ApplyEmotionAsync` after successful base synthesis.
- [x] Response diagnostics reach UI without raw HTTP/transport leakage (translator + toasts).
- [x] Unsupported / post-apply failure is explicit (message + optional prosody null).
- [x] GAP-050 first-consumer (Emotion panel) behavior unchanged.
- [x] Tests: service seam, ViewModel notice behavior, JSON contract for extended `VoiceSynthesisResponse` fields.
- [x] Closure matrix + `run_verification.py` **completion_guard** PASS; tracker + registry + STATE synced.

## Allowlist

`src/VoiceStudio.App/Services/VoiceSynthesisService.cs`, `src/VoiceStudio.App/Services/ToastNotificationService.cs`, `src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs`, `src/VoiceStudio.App/Utilities/ActionableErrorTranslator.cs`, `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs`, `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml`, `src/VoiceStudio.App.Tests/Services/VoiceSynthesisServiceTests.cs`, `src/VoiceStudio.App.Tests/Services/ProjectWorkflowCoordinatorTests.cs`, `src/VoiceStudio.App.Tests/Services/SearchOverlayCoordinatorTests.cs`, `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs`, `src/VoiceStudio.App.Tests/Core/VoiceSynthesisSsmlDiagnosticsTests.cs`, `src/VoiceStudio.App.Tests/UI/CommonActionsSmokeTests.cs`, `src/VoiceStudio.App.Tests/UI/CriticalPathSmokeTests.cs`, execution row, closure report, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`.

## Hard OUT

New emotion ML models, affective inference, second DSP path, streaming synthesis rewrite, shell/startup changes, broad Voice Synthesis UX redesign, new `IBackendClient` surface on `VoiceSynthesisViewModel`.

## Rollback

Revert this lane’s commits only; preserve GAP-050 mapping lane and GAP-023 authority.

## Changelog

- **2026-04-07:** Execution row frozen — Voice Synthesis preset consumer bounded slice.
- **2026-04-07:** **Closed** — [VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_LANE_CLOSURE_2026-04-07.md](../reports/verification/VOICESTUDIO_GAP050_VOICESYNTHESIS_EMOTION_PRESET_CONSUMER_LANE_CLOSURE_2026-04-07.md); Quick `artifacts/verify/20260407_073454/`; rolling **20260407-074018** (**completion_guard** PASS); `pytest tests/ci` **217**; App.Tests **3185** passed / **274** skipped.
