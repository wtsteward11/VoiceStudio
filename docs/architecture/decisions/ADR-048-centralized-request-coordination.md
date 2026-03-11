# ADR-048: Centralized Request Coordination

**Status:** Accepted
**Date:** 2026-03-11
**Decision Makers:** VoiceStudio Architecture Team
**Related:** Centralized Request Coordination Plan, RequestMetricsService, BackendClient

## Context

BackendClient previously used inline per-endpoint coordination (single-flight + TTL cache) for profiles and engines. ProfilesViewModel also had its own per-instance coalescing (`_loadProfilesTask`, `_loadProfilesRefCount`, `_loadProfilesLock`), which was redundant because BackendClient was already the convergence point. This duplicated logic, complicated reasoning, and did not coalesce across multiple ProfilesViewModel instances.

RequestMetricsService records per-endpoint request counts but does not control traffic; it is dashboard-only.

## Options Considered

1. **Keep per-endpoint inline coordination in BackendClient**
   - Pros: No new abstractions
   - Cons: Duplicated patterns for profiles and engines; not reusable for other endpoints; ProfilesViewModel coordination remains redundant

2. **Centralized IRequestCoordinator layer**
   - Pros: Single-flight, TTL, invalidation in one place; reusable; ProfilesViewModel becomes a dumb caller; BackendClient delegates to coordinator
   - Cons: New abstraction and DI wiring

## Decision

Introduce `IRequestCoordinator` with `RequestCoordinator` implementation. BackendClient delegates profiles and engines list fetching to the coordinator. ProfilesViewModel removes local coordination; it simply calls `ProfilesUseCase.ListAsync`, which forwards to BackendClient. RequestMetricsService remains unchanged (records only, no traffic control).

**Layering:**
1. **Metrics layer**: RequestMetricsService — per-endpoint counts, proof snapshots; never acts as traffic cop
2. **Coordination layer**: IRequestCoordinator — single-flight, TTL cache, invalidation, cancellation; owned by BackendClient via DI
3. **Feature layer**: ProfilesViewModel, other ViewModels — dumb callers; no locks, ref-counts, or per-instance coalescing

**Keys:** `profiles:list` (TTL 30s), `engines:list` (TTL 60s). Mutation operations (Create/Update/Delete profile) invalidate `profiles:list`.

## Consequences

### Positive

- Single convergence point for profiles and engines requests across all callers
- Reusable coordinator for future coordinated endpoints
- ProfilesViewModel simplified; no redundant local coalescing
- Clear separation: metrics vs coordination vs feature logic

### Negative

- Additional DI registration (`IRequestCoordinator` singleton)
- BackendClient constructor gains optional `IRequestCoordinator` parameter (default: new instance when null for non-DI paths)

### Neutral

- Engine list invalidation on engine state change remains out of scope (future work)

## Implementation

- `src/VoiceStudio.App/Services/IRequestCoordinator.cs` — interface
- `src/VoiceStudio.App/Services/RequestCoordinator.cs` — in-memory cache, in-flight map, single lock
- `AppServices.cs` — `AddSingleton<IRequestCoordinator, RequestCoordinator>()`; BackendClient factory injects it
- `BackendClient.cs` — uses `_requestCoordinator.GetOrCreateAsync` for `GetProfilesAsync`, `GetEnginesAsync`; `InvalidateProfilesCache` → `_requestCoordinator.Invalidate("profiles:list")`
- `ProfilesViewModel.cs` — no coordination fields; `LoadProfilesAsync` calls `_profilesUseCase.ListAsync` then `ReplaceProfiles`

## Verification

For normal startup (Profiles panel + Timeline + VoiceSynthesis, etc.):

- `RequestMetricsService.GetSnapshot()` should show `/api/profiles: 1` and `/api/engines: 1` (or low counts) within the first window
- Test `LoadProfilesAsync_ConcurrentCalls_CoalescesToSingleRequest` asserts coordinator coalesces concurrent calls to a single factory invocation

## References

- Centralized Request Coordination Plan (attached to implementation task)
- [RequestMetricsService.cs](../../../src/VoiceStudio.App/Services/RequestMetricsService.cs)
- [BackendClient.cs](../../../src/VoiceStudio.App/Services/BackendClient.cs)
