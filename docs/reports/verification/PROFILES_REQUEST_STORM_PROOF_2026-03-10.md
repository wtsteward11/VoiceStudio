# Profiles Panel Request Storm Mitigation Proof

**Date:** 2026-03-10  
**Purpose:** Capture proof that Profiles panel request storm is mitigated via RequestCoordinator coalescing.  
**Related:** [RequestCoordinatorIntegrationTests](../../../src/VoiceStudio.App.Tests/Services/RequestCoordinatorIntegrationTests.cs), [RequestCoordinator](../../../src/VoiceStudio.App/Services/RequestCoordinator.cs)

---

## Context

When the Profiles panel loads, multiple consumers may call `LoadProfilesAsync` concurrently. Without coalescing, this would trigger multiple HTTP requests to `/api/profiles`. The `RequestCoordinator` (used by `BackendClient.GetProfilesAsync`) coalesces concurrent calls into a single factory invocation, reducing the request storm.

---

## Manual Verification Steps

1. **Start backend** (e.g. `python -m uvicorn backend.api.main:app` or via `run_studio.ps1`).
2. **Start VoiceStudio app** (Debug, x64).
3. **Reset metrics baseline:**
   - Via Diagnostics panel (if exposed), or
   - Programmatically: `AppServices.GetService<IRequestMetricsService>()?.Reset()`
4. **Open Profiles panel** (View > Profiles or NavRail Profiles).
   - This triggers `LoadProfilesAsync`; multiple consumers may request profiles.
5. **Obtain snapshot:**
   - Via Diagnostics panel if it exposes `RequestMetricsService.GetSnapshot()`, or
   - Programmatically after the panel has loaded.
6. **Record** `/api/profiles` count from the snapshot.

---

## Expected vs Actual

| Metric | Expected (after coalescing) | Actual |
|--------|-----------------------------|--------|
| `/api/profiles` count | ≤ 2 (allow small tolerance for health/other) | _TBD_ |

**Path normalization:** `RequestMetricsService` normalizes `/api/profiles` and `/api/profiles/{id}` to `/api/profiles` (line 98 of RequestMetricsService.cs).

---

## Snapshot Output (Example)

```json
{
  "/api/profiles": 1,
  "/api/health": 2
}
```

---

## Verification Result

- [ ] Manual run completed
- [ ] Snapshot captured
- [ ] `/api/profiles` count ≤ 2 (coalescing effective)

**Scenario test:** `ProfilesPanelScenario_LoadCreateRefresh_BoundedRequestCounts` simulates load → create → refresh and asserts GetProfilesAsync is called exactly twice (load + post-create refetch); no request storm.

---

## Automation Notes

**CI-capable test:** `RequestCoordinatorIntegrationTests.ProfilesUseCase_ConcurrentListAsync_CoalescesToBoundedRequests` mocks HTTP handler, runs `ProfilesUseCase.ListAsync` (the path used by `ProfilesViewModel.LoadProfilesAsync`) concurrently 3 times, and asserts `GetSnapshot()["/api/profiles"] <= 2`. Run with:

```powershell
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ProfilesUseCase_ConcurrentListAsync"
```

**Scenario:** open Profiles → select profile → create profile → refresh. Expected bounded counts: `/api/profiles` ≤ 2, `/api/engines/list` ≤ 1. Create action invalidates cache; next GetProfiles refetches (see `CreateProfile_InvalidatesCache_NextGetProfilesRefetches`).
