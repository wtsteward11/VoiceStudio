# 429 Degraded Mode Flow

> **Purpose:** Document how 429 (Too Many Requests) responses enter degraded mode and a persistent banner instead of the toast pipeline.  
> **Related:** [ErrorPresentationService](../../src/VoiceStudio.App/Services/ErrorPresentationService.cs), [GracefulDegradationService](../../src/VoiceStudio.App/Services/GracefulDegradationService.cs), [ERROR_HANDLING_GUIDE](./ERROR_HANDLING_GUIDE.md)

---

## Flow Diagram

```
BackendClient (HTTP 429)
    │
    ▼
BackendException / BackendServerException (StatusCode=429)
    │
    ▼
ViewModel / ErrorDialogService catches
    │
    ▼
IErrorPresentationService.ShowError()
    │
    ▼
ErrorPresentationService.ShowErrorToast()
    │
    ├─► ErrorHandler.IsBackendStressException(ex) == true (429, 502, 503, timeouts)
    │       │
    │       ▼
    │   GracefulDegradationService.EnterDegradedMode(message)
    │       │   message = "Too many requests. Please wait before trying again."
    │       │
    │       ▼
    │   return  (suppress toast)
    │
    ├─► GracefulDegradationService.IsDegradedMode == true
    │       │
    │       ▼
    │   return  (suppress additional toasts; banner is the surface)
    │
    └─► else: ToastNotificationService.ShowError(...)
            │
            ▼
        ToastNotificationService.ShowError()
            │
            ├─► GracefulDegradationService.IsDegradedMode == true
            │       │
            │       ▼
            │   return  (silently drop)
            │
            └─► else: ShowToast(ToastType.Error, ...)
```

---

## Banner Visibility

```
GracefulDegradationService.EnterDegradedMode()
    │
    ▼
DegradedModeChanged event (isDegraded=true)
    │
    ▼
MainWindow.OnDegradedModeChanged()
    │
    ▼
DegradedModeBanner.IsOpen = true
DegradedModeBanner.Message = "Too many requests. Please wait before trying again."
```

---

## Verification

### CI Gate (2026-03-11)

The `ci.yml` dotnet-build job includes a dedicated **429 degraded mode gate** that runs:
- `DegradedModeIntegrationTests` (TestCategory=DegradedMode): 3 tests
- `RateLimitToastDedupeTests`: 4 tests

Failures block the pipeline. See `.github/workflows/ci.yml` step "429 degraded mode gate".

### Manual Steps

1. **5×429 → 0 toasts, 1 banner**
   - Start app and backend; ensure backend can return 429 (or use a mock/proxy).
   - Trigger 5 rapid-fire requests that hit a rate-limited endpoint (e.g. synthesis, profiles).
   - **Expected:** 0 toast notifications; 1 persistent InfoBar banner with "Too many requests. Please wait before trying again."
   - **Dedupe:** Repeated 429s do not produce duplicate toasts; `ErrorPresentationService` enters degraded mode on first 429 and suppresses all subsequent toasts while `IsDegradedMode` is true.

2. **Recovery clears state**
   - After entering degraded mode, either:
     - Wait for cooldown (`DegradedModeCooldownSeconds`); or
     - Dismiss the banner (IsClosable="True").
   - **Expected:** `GracefulDegradationService.ExitDegradedMode()` runs; banner hides; next transient errors can show toast again (until next 429).

3. **RateLimitToastDedupe**
   - `RateLimitToastDedupe` deduplicates identical 429 toasts per endpoint when used by callers. In the main 429 path, dedupe is achieved via degraded mode: first 429 → EnterDegradedMode → return; subsequent 429s → IsDegradedMode → return. No toast is ever shown for 429, so no per-toast dedupe is needed in that path.

### Executable Proof

- `DegradedModeIntegrationTests.cs`: `Repeated429_EntersDegradedMode_NoToast`, `ExitDegradedMode_ClearsState`, `ErrorHandler_Recognizes429_AsBackendStress`

### Key Files

- `ErrorPresentationService.cs` lines 213–231: detects 429, calls `EnterDegradedMode`, suppresses toast; lines 228–230 suppress additional toasts when already degraded
- `ToastNotificationService.cs` line 56: checks `IsDegradedMode` before showing error toast
- `RateLimitToastDedupe.cs`: optional dedupe for callers that show 429 toasts before degraded mode; main flow uses degraded mode instead
- `MainWindow.xaml` line 138: `DegradedModeBanner` InfoBar
- `MainWindow.xaml.cs` lines 811–821: `OnDegradedModeChanged` wires banner visibility
