# PR-17: Mixer Extraction Scope

**Status:** FROZEN — do not begin implementation without reading this doc
**Date:** 2026-03-23
**Verified against:** live `IBackendClient.cs`, `BackendClient.cs`, `IMixerStateClient.cs`, `MixerStateClient.cs`, `EffectsMixerViewModel.cs`, `AppServices.cs` (2026-03-23)

---

## Context

PR-17 migrates the Mixer domain from `IBackendClient`/`BackendClient` to pipeline-owned `IMixerStateClient`/`MixerStateClient`. The thin client (`MixerStateClient`) already exists and already implements `IMixerStateClient` — but its constructor takes `IBackendClient` and all methods delegate via `_backend.XxxAsync(...)`. This PR cuts that delegation and makes `MixerStateClient` a first-class pipeline owner.

---

## Split Decision

**Single PR-17 for all 19 methods.**

Rationale:
- `EffectsMixerViewModel` already uses `IMixerStateClient _mixerStateClient` for all calls — it does NOT use `IBackendClient` for mixer methods. The ViewModel surface is already clean.
- All 19 methods belong to the same mixer domain with no natural cohesion boundary.
- The change is confined to 5 files: `IMixerStateClient.cs`, `MixerStateClient.cs`, `IBackendClient.cs`, `BackendClient.cs`, `AppServices.cs`.
- The 4 gap methods have no current callers — adding them to the interface and removing from `IBackendClient` is low risk.

Blast radius: **5 files** (interface, implementation, IBackendClient, BackendClient, DI registration).

---

## Methods Leaving IBackendClient / BackendClient

All 19 Mixer Task methods. Grouped by sub-domain:

| Method | HTTP | Endpoint | Leaving BackendClient Line |
|--------|------|----------|---------------------------|
| `GetMixerStateAsync` | GET | `/api/mixer/state/{projectId}` | 1595 |
| `UpdateMixerStateAsync` | PUT | `/api/mixer/state/{projectId}` | 1611 |
| `ResetMixerStateAsync` | POST (null body) | `/api/mixer/state/{projectId}/reset` | 1627 |
| `CreateMixerSendAsync` | POST | `/api/mixer/state/{projectId}/sends` | 1644 (delegates to CreateSendAsync:1700) |
| `UpdateMixerSendAsync` | PUT | `/api/mixer/state/{projectId}/sends/{sendId}` | 1649 (delegates to UpdateSendAsync:1716) |
| `DeleteMixerSendAsync` | DELETE | `/api/mixer/state/{projectId}/sends/{sendId}` | 1654 (delegates to DeleteSendAsync:1732) |
| `CreateMixerReturnAsync` | POST | `/api/mixer/state/{projectId}/returns` | 1659 (delegates to CreateReturnAsync:1748) |
| `UpdateMixerReturnAsync` | PUT | `/api/mixer/state/{projectId}/returns/{returnId}` | 1664 (delegates to UpdateReturnAsync:1764) |
| `DeleteMixerReturnAsync` | DELETE | `/api/mixer/state/{projectId}/returns/{returnId}` | 1669 (delegates to DeleteReturnAsync:1780) |
| `CreateMixerSubGroupAsync` | POST | `/api/mixer/state/{projectId}/subgroups` | 1674 (delegates to CreateSubGroupAsync:1796) |
| `UpdateMixerSubGroupAsync` | PUT | `/api/mixer/state/{projectId}/subgroups/{subgroupId}` | 1679 (delegates to UpdateSubGroupAsync:1812) |
| `DeleteMixerSubGroupAsync` | DELETE | `/api/mixer/state/{projectId}/subgroups/{subgroupId}` | 1684 (delegates to DeleteSubGroupAsync:1828) |
| `UpdateMixerMasterAsync` | PUT | `/api/mixer/state/{projectId}/master` | 1689 (delegates to UpdateMasterAsync:1844) |
| `GetMixerPresetsAsync` | GET | `/api/mixer/presets/{projectId}` | 1694 (delegates to ListMixerPresetsAsync:1878) |
| `GetMixerPresetAsync` *(gap)* | GET | `/api/mixer/presets/{projectId}/{presetId}` | 1894 |
| `CreateMixerPresetAsync` | POST | `/api/mixer/presets/{projectId}` | 1910 |
| `UpdateMixerPresetAsync` *(gap)* | PUT | `/api/mixer/presets/{projectId}/{presetId}` | 1926 |
| `DeleteMixerPresetAsync` *(gap)* | DELETE | `/api/mixer/presets/{projectId}/{presetId}` | 1942 |
| `ApplyMixerPresetAsync` | POST (null body) | `/api/mixer/presets/{projectId}/{presetId}/apply` | 1957 |

*(gap) = method exists on `IBackendClient` but NOT currently on `IMixerStateClient`.*

---

## Interface Gap: IMixerStateClient Missing 4 Methods

`IMixerStateClient.cs` currently has 15 methods. The following 4 must be added:

| Method to Add | Return Type | Reason |
|---|---|---|
| `GetMixerPresetAsync(string projectId, string presetId, ...)` | `Task<MixerPreset>` | On IBackendClient, missing from interface |
| `UpdateMixerPresetAsync(string projectId, string presetId, MixerPreset preset, ...)` | `Task<MixerPreset>` | On IBackendClient, missing from interface |
| `DeleteMixerPresetAsync(string projectId, string presetId, ...)` | `Task<bool>` | On IBackendClient, missing from interface |
| `UpdateMixerMasterAsync(string projectId, MixerMaster master, ...)` | `Task<MixerMaster>` | On IBackendClient, missing from interface |

No current callers for these 4. They represent complete domain coverage.

---

## Destination: IMixerStateClient / MixerStateClient

`MixerStateClient` ctor changes from:
```csharp
public MixerStateClient(IBackendClient backend)
```
to:
```csharp
internal MixerStateClient(BackendClientHttpPipeline pipeline)
```

Each of the 15 existing methods replaces `_backend.XxxAsync(...)` delegation with a direct pipeline call. The 4 new methods are implemented the same way. Use the same call patterns as `EffectChainClient` and `VideoGenClient`:

- **GET → T**: `_pipeline.GetAsync<T>(endpoint, cancellationToken)`
- **POST → T**: `_pipeline.PostAsync<TReq, TResp>(endpoint, req, cancellationToken)`
- **PUT → T**: `_pipeline.PutAsync<TReq, TResp>(endpoint, req, cancellationToken)`
- **POST (null body) → T**: `_pipeline.SendRequestAsync<object, T>(endpoint, null, HttpMethod.Post, cancellationToken)`
- **DELETE → bool**: `await _pipeline.SendRequestAsync<object, object>(endpoint, null, HttpMethod.Delete, cancellationToken); return true;`

---

## Callers

| Caller | File | Methods Used | Access Pattern |
|---|---|---|---|
| `EffectsMixerViewModel` | `Views/Panels/EffectsMixerViewModel.cs` | 15 of 15 existing methods via `_mixerStateClient` | Injected `IMixerStateClient` (already correct) |
| (none) | — | `GetMixerPresetAsync`, `UpdateMixerPresetAsync`, `DeleteMixerPresetAsync`, `UpdateMixerMasterAsync` | No current callers — gap methods added for domain completeness |

No ViewModel or helper class accesses `IBackendClient` for Mixer methods. `EffectsMixerView.xaml.cs` line 31–35 passes `IMixerStateClient` from `ServiceProvider.GetMixerStateClient()` to the ViewModel ctor.

---

## DI Registration Change

**Current (AppServices.cs line 326):**
```csharp
services.AddSingleton<IMixerStateClient, MixerStateClient>();
```
(Resolves `MixerStateClient` by injecting `IBackendClient` from container.)

**New:**
```csharp
services.AddSingleton<IMixerStateClient>(sp =>
    new MixerStateClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));
```

Static accessor at line 698 (`GetMixerStateClient`) remains unchanged.

---

## Proof Requirements

### Seam test (new)
Add to `BackendClientTransportPolicyTests`:

```
GetMixerStateAsync_ResolvesCorrectPath
```

Test pattern: create `MixerStateClient` with a test pipeline (use existing `CreateEffectChainClient` pattern), call `GetMixerStateAsync("proj1")`, assert `_httpClient` received `GET /api/mixer/state/proj1`.

### Anti-regression tests (new)
Add to `BackendClientExtractionRegressionTests`:

```
IBackendClient_DoesNotExposeMixerMethods
BackendClient_DoesNotExposeMixerMethods
```

Add `MixerMethodNames` array with all 19 method strings (same pattern as `VideoMethodNames` at lines 66–67, `IBackendClient_DoesNotExposeVideoMethods` at line 225).

---

## Out of Scope

- **No DTO changes** — `MixerState`, `MixerPreset`, `MixerSend`, `MixerReturn`, `MixerSubGroup`, `MixerMaster` models unchanged.
- **No backend API contract changes** — endpoints unchanged.
- **No UI changes** — `EffectsMixerView.xaml.cs` and `EffectsMixerViewModel.cs` unchanged (already use `IMixerStateClient`).
- **No private BackendClient methods** — `CreateSendAsync`, `UpdateSendAsync`, `DeleteSendAsync`, `CreateReturnAsync`, `UpdateReturnAsync`, `DeleteReturnAsync`, `CreateSubGroupAsync`, `UpdateSubGroupAsync`, `DeleteSubGroupAsync`, `UpdateMasterAsync`, `ListMixerPresetsAsync`, `CreateExceptionFromResponseAsync` are private helpers. They are NOT removed — they stay in `BackendClient` for any remaining callers. Only the 19 public interface-method implementations are removed.
- **No state trim** — deferred per existing plan.

---

## Implementation Sequence

1. Add 4 missing methods to `IMixerStateClient.cs`
2. Rewrite `MixerStateClient.cs`: new ctor, replace all 15 delegating bodies with direct pipeline calls, add 4 new method implementations
3. Remove 19 method signatures from `IBackendClient.cs` (lines 192–222); replace with comment noting extraction
4. Remove 19 public interface method implementations from `BackendClient.cs` (lines 1595–1971 range); leave private helpers in place
5. Update `AppServices.cs` line 326: change to factory registration
6. Add seam test `GetMixerStateAsync_ResolvesCorrectPath` to `BackendClientTransportPolicyTests`
7. Add `MixerMethodNames`, `IBackendClient_DoesNotExposeMixerMethods`, `BackendClient_DoesNotExposeMixerMethods` to `BackendClientExtractionRegressionTests`
8. Run `dotnet build` → 0 errors
9. Run `dotnet test --filter "Mixer|BackendClientExtraction|BackendClientTransportPolicy"` → all pass
10. Run `.\scripts\verify.ps1 -Quick` → PASSED; confirm `latest_pointer.json` advances
11. Update `STATE.md` and `BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md`

---

## Files Changed

| File | Change |
|---|---|
| `src/VoiceStudio.App/Core/Services/IMixerStateClient.cs` | Add 4 gap methods |
| `src/VoiceStudio.App/Services/MixerStateClient.cs` | Ctor `(pipeline)`, 15 bodies replaced, 4 new methods |
| `src/VoiceStudio.App/Core/Services/IBackendClient.cs` | Remove 19 Mixer signatures (lines 192–222) |
| `src/VoiceStudio.App/Services/BackendClient.cs` | Remove 19 public Mixer implementations (1595–1971 range) |
| `src/VoiceStudio.App/Services/AppServices.cs` | Line 326: factory registration |
| `src/VoiceStudio.App.Tests/...BackendClientTransportPolicyTests.cs` | Add seam test |
| `src/VoiceStudio.App.Tests/...BackendClientExtractionRegressionTests.cs` | Add MixerMethodNames + 2 anti-regression tests |
