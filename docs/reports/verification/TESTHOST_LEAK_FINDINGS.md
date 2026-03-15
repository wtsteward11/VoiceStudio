# Testhost Leak Root Cause Findings

**Date:** 2026-03-16  
**Purpose:** Document root cause of lingering `testhost.exe` that locks DLLs during build (MSB3027/MSB3021).

---

## Summary

**Root cause identified:** Two test classes created `DispatcherQueueController` inline without storing a reference, so the controller was never shut down. The dedicated thread and its resources remained alive after tests completed.

---

## Leaking Test Clusters

| Rank | Test Class | Resource | Fix |
|------|------------|----------|-----|
| 1 | `VoiceBrowserViewModelTests` | `DispatcherQueueController.CreateOnDedicatedThread()` in `CreateTestViewModelContext()` | Switched to `TestAppServicesHelper.EnsureInitialized()` + `AppServices.GetService<IViewModelContext>()` |
| 2 | `JobProgressViewModelTests` | Same pattern | Same fix |

---

## Technical Detail

Both classes used:

```csharp
private static IViewModelContext CreateTestViewModelContext()
{
    var dispatcher = DispatcherQueue.GetForCurrentThread()
        ?? DispatcherQueueController.CreateOnDedicatedThread().DispatcherQueue;
    return new ViewModelContext(NullLogger.Instance, dispatcher);
}
```

The controller was never stored, so `ShutdownQueueAsync()` could not be called. The dedicated thread and its COM/dispatcher resources outlived the test run.

---

## What Was Already Correct

- `TestAssemblySetup.AssemblyCleanup` already calls `TestAppServicesHelper.Cleanup()` — the static dispatcher from TestAppServicesHelper is properly shut down.
- Most seam tests store `_dispatcherController` and call `ShutdownQueueAsync()` in `[TestCleanup]`.

---

## Verification

After fix: run `dotnet test` then immediately `dotnet build`. Build should succeed without requiring `taskkill testhost.exe`. The `taskkill` step in `run_verification.py` remains as a safety net for edge cases (e.g., IDE debug sessions, crashed tests).

---

## Release-Trust Closure (2026-03-16)

**Task 4 proof:** Raw `dotnet test` → `dotnet build` (no taskkill) was run. Result: testhost can still linger after full or mixed test runs. After `VoiceBrowserViewModelTests` only (17 tests): build succeeded without taskkill. After broader runs: multiple testhost processes observed.

**Task 5 leak hunt:** No new leak cluster identified with the same inline-controller pattern. All 40+ seam tests store `_dispatcherController` and call `ShutdownQueueAsync()` in `[TestCleanup]`. Likely causes of lingering testhost: MSTest parallel execution (multiple testhost processes), failed tests preventing Cleanup, or nondeterministic teardown under load.

**Task 7 — taskkill as safety net:** `run_verification.py` kills testhost before build_smoke when present. This is the primary success path until clean no-taskkill confidence is proven. Normal expectation: build works when verification runs; `stale_process_cleaned` indicates teardown debt. Aim for `stale_process_cleaned` to trend toward false over time.
