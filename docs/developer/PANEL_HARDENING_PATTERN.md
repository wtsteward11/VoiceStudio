# Panel Hardening Pattern

> **Purpose:** Codify the architectural patterns used in the Profiles panel so future panels do not reinvent bad popup coupling, local coalescing hacks, or accidental analytics storms.  
> **Reference implementation:** Profiles panel (`ProfilesViewModel`, `BackendClient.Profiles.cs`)  
> **Second panel (2026-03-11):** Library panel (`LibraryViewModel`) — delete confirmation uses `IDialogService.ShowConfirmationAsync`

---

## Where Request Coordination Belongs

- **BackendClient + RequestCoordinator:** Single-flight, TTL, and cache invalidation belong in the transport layer, not in ViewModels.
- **ViewModel:** Calls `GetProfilesAsync`, `GetEnginesAsync`, etc. The ViewModel does not implement its own coalescing or caching.
- **Cache keys:** Use domain-scoped keys (e.g. `profiles:list`, `engines:list`). Invalidation uses the same key namespace.

### Coordinated Endpoints (Must Use BackendClient)

| Endpoint | BackendClient Method | Cache Key |
|----------|----------------------|-----------|
| `/api/profiles` | `GetProfilesAsync` | `profiles:list` |
| `/api/engines/list` | `GetEnginesAsync` | `engines:list` |
| `/api/projects` | `GetProjectsAsync` | `projects:list` |

**Rule:** Panels must NOT call these endpoints via `SendRequestAsync` or raw HTTP. Use the BackendClient domain methods (or `IProfilesClient`, `ProfilesUseCase`) so coordination applies. Re-implementing list fetches in each consumer bypasses single-flight and causes request storms.

---

## What Must Stay Out of ViewModels

| Responsibility | Correct Location | Anti-Pattern |
|----------------|------------------|---------------|
| Temp file orchestration | Service (e.g. `IProfilePreviewService`) | ViewModel creating/deleting temp files |
| Transport caching | `RequestCoordinator` in BackendClient | ViewModel-level `Dictionary` for request coalescing |
| Preview synthesis | `IProfilePreviewService` | ViewModel calling backend and managing preview lifecycle |
| HTTP retry/backoff | BackendClient, CircuitBreaker | ViewModel implementing retry loops |

---

## Canonical Dialog Handling

- **Use `IDialogService.ShowConfirmationAsync`** for destructive actions (delete, overwrite).
- **Do not use** static `ConfirmationDialog` or direct `ContentDialog` instantiation from ViewModels.
- **XamlRoot:** `DialogService` obtains `XamlRoot` via `GetXamlRoot()`; panels must ensure XamlRoot is available when dialogs are shown (e.g. defer to Loaded, never fire-and-forget from constructor).

---

## Selection-Change Cancellation Pattern

- When selection changes trigger async work (e.g. loading analytics), cancel the previous operation.
- Use `CancellationTokenSource`: `_profileChangeCts?.Cancel(); _profileChangeCts = new CancellationTokenSource();`
- Pass the token to the async call; respect cancellation in the continuation.

---

## Explicit-Action-Only Analytics Loading

- **Do not** auto-trigger analytics or heavy backend calls on every selection change.
- Load analytics only when the user explicitly requests them (e.g. "View quality history", "Refresh").
- Avoid selection-change handlers that fan out to multiple endpoints.

---

## Cache Invalidation Rules

| Action | Invalidation |
|--------|--------------|
| Create (profile, project, etc.) | `InvalidateProfilesCache()` or equivalent |
| Update | Same |
| Delete | Same |
| Next `Get*Async` call | Refetches from backend |

---

## Proof Requirements for Panel Stability

1. **Endpoint-count proof:** CI test or manual verification that opening a panel and performing steady-state actions does not cause request storms (e.g. `/api/profiles` ≤ 2 for typical flows).
2. **Coalescing proof:** Concurrent `GetProfilesAsync` calls coalesce to a single HTTP request (see `RequestCoordinatorIntegrationTests`).
3. **Create-invalidate proof:** After create, next list fetch hits the backend (cache invalidated).

---

## CI Guardrail (Request Coordination)

- **Convention:** Stable list endpoints (`/api/profiles`, `/api/engines/list`, `/api/projects`) must be fetched via BackendClient domain methods, not `SendRequestAsync` with the path directly.
- **Tests:** `RequestCoordinatorIntegrationTests` proves coalescing for profiles, engines, projects.
- **Review:** When adding new panels that display profiles, engines, or projects, ensure they use `IBackendClient.GetProfilesAsync`, `GetEnginesAsync`, `GetProjectsAsync` (or `IProfilesClient`, `ProfilesUseCase`).

---

## CI Guardrail (Dialog Pattern)

- **Script:** `python scripts/ci/check_dialog_pattern.py`
- **Runs in:** build.yml (build-backend job)
- **Fails on:** New `ConfirmationDialog` or raw `ContentDialog` in ViewModels
- **Baseline:** `.ci/dialog_pattern_baseline.txt` — reduce as ViewModels migrate to IDialogService

---

## Quick Checklist for New Panels

- [ ] BackendClient domain partial exists (or methods in main client) with request coordination where appropriate
- [ ] ViewModel uses `IDialogService` for confirmations; no static `ConfirmationDialog`
- [ ] Selection-change triggers cancellation of prior async work
- [ ] Analytics/heavy loads are explicit-action-only
- [ ] Create/update/delete invalidates cache
- [ ] Proof: endpoint-count and coalescing tests exist or documented manual steps
