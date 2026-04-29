# Voice Synthesis Error Dialog Recovery

**Date:** 2026-04-29
**Bundle:** Dialog Recovery Hardening
**Status:** VERIFIED

---

## Root Cause

In `VoiceSynthesisViewModel.SynthesizeAsync`, both error catch blocks (`ConsentRequiredException` and `Exception`) followed this sequence:

```
1. WorkflowState = Error           ← error state set
2. await ShowErrorAsync(...)       ← BLOCKS here; IsLoading still true
3. finally: IsLoading = false      ← only runs after dialog dismissed by user
```

In production with `XamlRoot` set (after `Loaded`), `ShowErrorAsync` calls `await dialog.ShowAsync()` which waits indefinitely for user dismissal. During that wait, `IsLoading = true` kept the Synthesize button disabled and any busy-indicator UI visible — even though `WorkflowState` was already `Error`. The user could not retry synthesis until they dismissed the error dialog.

Unit tests did not expose this because `_errorDialogService` was resolved from `ServiceProvider`, which returns `null` in test context — making `await (null?.ShowErrorAsync(...) ?? Task.CompletedTask)` complete instantly.

---

## Files Changed

| File | Change |
|------|--------|
| `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` | Added `IErrorDialogService? errorDialogService = null` optional constructor parameter; both catch-block `await ShowErrorAsync` calls changed to fire-and-log `ContinueWith` |
| `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` | Added `Mock<IErrorDialogService>` field, wired to constructor, added 3 dialog-edge regression tests |

---

## Production Fix

**Constructor change** — added optional injectable parameter (production callers pass nothing; tests inject a mock):

```csharp
public VoiceSynthesisViewModel(...,
    IGeneratedAudioTimelineService? generatedAudioTimelineService = null,
    IErrorDialogService? errorDialogService = null)
```

**Fire-and-log pattern** (applied to both catch blocks):

```csharp
// BEFORE (blocks finally):
await (_errorDialogService?.ShowErrorAsync(ex, ...) ?? Task.CompletedTask);

// AFTER (finally runs immediately):
(_errorDialogService?.ShowErrorAsync(ex, ...) ?? Task.CompletedTask)
    .ContinueWith(t =>
    {
        if (t.IsFaulted)
            _errorLoggingService?.LogError(
                t.Exception?.InnerException ?? t.Exception,
                "VoiceSynthesis.ErrorDialog");
    }, TaskScheduler.Default);
```

The `finally` block now runs without waiting for the modal: `IsLoading = false`, `WorkflowState` remains `Error`, and `SynthesizeCommand` re-enables immediately.

---

## Behavior Before / After

| Scenario | Before | After |
|----------|--------|-------|
| Backend error, dialog shown | `IsLoading = true` until user dismisses | `IsLoading = false` immediately |
| Synthesize button | Disabled until dialog dismissed | Re-enabled as soon as error state set |
| Consent error path | Same block | Same fix — `IsConsentRequired` set, `IsLoading` cleared at once |
| Dialog throws | Unhandled; could surface in test runner | Absorbed and logged via `ContinueWith` |
| Successful synthesis | Unaffected | Unaffected |

---

## Tests Added

Three regression tests added in `#region Error Dialog Recovery Tests`:

1. `SynthesizeAsync_BackendException_DoesNotWaitForErrorDialog` — dialog never completes (hanging `TaskCompletionSource`); asserts `IsLoading == false` and `WorkflowState == Error` before dialog resolves.
2. `SynthesizeAsync_ErrorDialogThrows_StillClearsLoadingAndReportsError` — dialog throws; asserts `IsLoading == false`, `WorkflowState == Error`, `HasError == true`.
3. `SynthesizeAsync_ConsentRequired_DoesNotWaitForErrorDialog` — consent path with hanging dialog; asserts `IsConsentRequired == true`, `IsLoading == false`.

---

## Verification Artifacts

| Gate | Result |
|------|--------|
| `dotnet build VoiceStudio.sln` | 0 errors |
| Focused C# tests (180 tests) | PASS |
| `python -m pytest tests/backend/services/test_consent_local_owner.py` | 9 passed |
| `python scripts/run_verification.py` | Overall: PASS |
| `.\scripts\verify.ps1 -Quick` | VERIFICATION PASSED |

---

## Non-Claims

- NOT a full runtime PASS
- NOT an operator/human verification proof
- NOT related to GAP-008
- NOT related to RHVoice
- NOT related to ENGINE_PARITY_MATRIX

---

## Related

- Prior bundle: `fix(backend): unblock synthesis for locally-owned voice profiles (consent gate)` — `1fde9ee7`
- Regression tests base: `test(runtime): add voice synthesis failure-path regression tests` — `f7836b46`
