# GOV-VOICESTUDIO-GAP064-SYNTHESIS-ACTIONABLE-ERRORS-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP064_SYNTHESIS_ACTIONABLE_ERRORS_01`  
**Status:** **Closed** (2026-04-06)  
**Tracker:** [GAP-064](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** runtime-affecting (client error presentation)

## Problem statement

Synthesis and SSML preview paths still surface transport-oriented or opaque text (`ex.Message`, duplicated toasts/dialogs, raw HTTP fragments). Backend now exposes structured envelopes (`BackendException` + `StandardErrorResponseParser`) and SSML `ssml_handling` on success; the WinUI layer needs **one translation authority** so operators see actionable, plain-English outcomes without new `IBackendClient` surface.

## Frozen architecture decisions

1. **Authority:** `ActionableErrorTranslator` in [src/VoiceStudio.App/Utilities/ActionableErrorTranslator.cs](../../src/VoiceStudio.App/Utilities/ActionableErrorTranslator.cs) is the single mapping from `(exception, operation context, optional SSML diagnostics)` → [ActionableErrorInfo](../../src/VoiceStudio.App/Core/Models/ActionableErrorInfo.cs).
2. **ErrorHandler integration:** [ErrorHandler.cs](../../src/VoiceStudio.App/Utilities/ErrorHandler.cs) delegates user-facing strings to the translator for synthesis/SSML contexts; rate-limit and degraded-mode branching stay in [ErrorPresentationService.cs](../../src/VoiceStudio.App/Services/ErrorPresentationService.cs).
3. **Five classes (bounded):** `ValidationInput`, `CapabilityUnsupported`, `EnvironmentUnavailable`, `TransientRetryable`, `Unknown`.
4. **No raw HTTP codes in default user copy:** primary message must not be of the form `HTTP 4xx` / `Network error (503):` for mapped `BackendException` paths.
5. **SSML honesty:** `stripped_warned` on success → warning toast + status; `preserved` → success as today; reject remains HTTP error → translator maps to fix-input guidance.
6. **Boundary:** No new methods on `IBackendClient`; extend DTOs only (`SSMLPreviewResult.ssml_handling`).

## Translation matrix (inventory + contract)

| Signals | Class | Primary message intent | Retry | Notes |
|--------|--------|------------------------|-------|------|
| `BackendValidationException`, 400/422, codes `INVALID_INPUT`, `VALIDATION_ERROR` | ValidationInput | Fix input / SSML | No | Prefer `RecoverySuggestion` when present |
| 404 `PROFILE_NOT_FOUND`, `RESOURCE_NOT_FOUND`, `BackendNotFoundException` | ValidationInput / Capability | Profile or resource missing | No | Operation-specific title |
| 403 consent/demo, `AUTHORIZATION_FAILED` | ValidationInput | Permission / consent | No | |
| 503 circuit breaker / `SERVICE_UNAVAILABLE`, engine unavailable hints | EnvironmentUnavailable | Engine/backend temporarily down | Yes | Align with `IsRetryable` |
| 429, 502, 504, timeouts, `BackendUnavailableException`, `BackendTimeoutException` | TransientRetryable | Wait / retry | Yes | Degraded path unchanged |
| 500 generic / `INTERNAL_SERVER_ERROR` / `ENGINE_PROCESSING_ERROR` | Unknown | Server/engine failure | Yes | Short operator-safe text |
| `InvalidOperationException` wrapping `BackendNotFoundException` (VoiceSynthesisService) | ValidationInput | Profile/engine selection | No | Unwrap inner |
| `HttpRequestException` without typed backend (VoiceSynthesisService) | EnvironmentUnavailable | Backend unreachable | Yes | No raw socket text in primary |

SSML success: if `SsmlHandling.Action == stripped_warned`, append warnings to secondary detail and use warning toast for preview/synthesis where applicable.

## Acceptance contract (all required)

- [x] `ActionableErrorTranslator` + `ActionableErrorInfo`; `ErrorHandler` uses translator for synthesis/SSML-related exceptions.
- [x] `VoiceSynthesisService` does not append raw `HttpRequestException.Message` to user strings; preserves typed `BackendException` where possible.
- [x] `VoiceSynthesisViewModel` synthesis failure uses one actionable primary message for multi-surface display (no conflicting narratives).
- [x] `SSMLControlViewModel` preview/validate errors use translator, not raw `ex.Message` in user strings.
- [x] `SSMLPreviewResult` includes `SsmlHandling`; `stripped_warned` surfaces warning on success.
- [x] C# tests: translator mapping, ErrorHandler no-raw-code, service mapping, SSML preview warning.
- [x] Closure matrix + proof — [closure](../reports/verification/VOICESTUDIO_GAP064_SYNTHESIS_ACTIONABLE_ERRORS_LANE_CLOSURE_2026-04-06.md).

## Allowlist

`src/VoiceStudio.App/Core/Models/ActionableErrorInfo.cs`, `ActionableErrorTranslator.cs`, `ErrorHandler.cs`, `VoiceSynthesisService.cs`, `VoiceSynthesisViewModel.cs`, `SSMLControlViewModel.cs`, `ISSMLClient.cs` (`SSMLPreviewResult` only), optional `VoiceSynthesisView.xaml.cs` if duplicate toast removed, tests under `VoiceStudio.App.Tests`, execution row, closure, tracker, registry, STATE.

## Hard OUT

Startup flow; first-run wizard; shell redesign; localization overhaul; broad dialog rewrite; notification center; backend API changes.

## Rollback

Revert scoped commit(s). Prior string behavior restores.

## Changelog

- **2026-04-06:** Row frozen; implementation and closure.
