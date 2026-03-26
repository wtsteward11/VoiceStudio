# PR-15 Model Manager Extraction Scope

**Status:** Frozen (2026-03-23)  
**Related:** [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md), [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)  
**Pattern:** scope → extract → delete → sweep → prove → ledger. No improvisation.

---

## Decision

**PR-15 = Models (9 methods)**

Chosen after PR-14 BackupRestore: thin client exists; coherent `/api/models` family; smaller blast radius than Mixer (22 methods); single client surface (unlike Video split).

---

## Methods Leaving IBackendClient

| Method | Endpoint | HTTP | Notes |
|--------|----------|------|-------|
| GetModelsAsync | `/api/models` | GET | Optional `?engine=` |
| GetModelAsync | `/api/models/{engine}/{modelName}` | GET | Not yet on IModelManagerClient |
| RegisterModelAsync | `/api/models` | POST | JSON body `engine`, `model_name`, `model_path`, optional `version`, `metadata` |
| VerifyModelAsync | `/api/models/{engine}/{modelName}/verify` | POST | Empty body |
| UpdateModelChecksumAsync | `/api/models/{engine}/{modelName}/update-checksum` | PUT | Empty body |
| DeleteModelAsync | `/api/models/{engine}/{modelName}` | DELETE | Returns bool |
| ExportModelAsync | `/api/models/{engine}/{modelName}/export` | GET | Returns Stream |
| ImportModelAsync | `/api/models/import` | POST | Multipart `file`; optional `?engine=` |
| GetStorageStatsAsync | `/api/models/stats/storage` | GET | Returns StorageStats |

---

## Destination

- **Interface:** `IModelManagerClient` — add `GetModelAsync`, `RegisterModelAsync` (7 methods already declared; 2 missing vs monolith).
- **Client:** `ModelManagerClient` — replace `IBackendClient _backend` with `BackendClientHttpPipeline`; internal ctor pattern (same as BackupRestoreClient / EffectChainClient).
- **DI:** [AppServices.cs](../../src/VoiceStudio.App/Services/AppServices.cs) — register `IModelManagerClient` with `BackendHttpContext().Pipeline`.

---

## Callers

| Caller | Usage |
|--------|-------|
| ModelManagerViewModel | GetModelsAsync, VerifyModelAsync, UpdateModelChecksumAsync, DeleteModelAsync, GetStorageStatsAsync, ExportModelAsync, ImportModelAsync via `IModelManagerClient` |
| ModelManagerView | Resolves `IModelManagerClient` for ViewModel ctor |
| ModelActions | `IModelManagerClient _client` (undoable actions) |
| TrainingViewModelTests | `GetModelAsync_ReturnsModelInfo` mocks `IBackendClient.GetModelAsync` — migrate to mock `IModelManagerClient` or keep transport test honest per [TEST_CLASSIFICATION.md](../governance/TEST_CLASSIFICATION.md) |

**No other production callers** of these nine methods on `IBackendClient` expected; confirm with sweep during implementation.

---

## Pipeline / Retained Exceptions

- **ExportModelAsync:** Use existing `BackendClientHttpPipeline.GetStreamAsync` (PR-14).
- **ImportModelAsync:** Use existing `PostMultipartAsync<T>` with form field `file`, filename `model.zip`, optional query `engine` (mirror current BackendClient).
- **DeleteModelAsync:** Use `SendRequestAsync<object, object>(..., HttpMethod.Delete)` or dedicated delete helper (mirror BackupRestore / EffectChain pattern).
- **RegisterModelAsync:** Anonymous request shape → use a small DTO in Core or inline `PostAsync` with matching JSON property names (`model_name` vs C# naming — align with backend; BackendClient uses anonymous object with `model_name`).

---

## Interface Additions (IModelManagerClient)

Add signatures matching `IBackendClient`:

```csharp
Task<ModelInfo> GetModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
Task<ModelInfo> RegisterModelAsync(string engine, string modelName, string modelPath, string? version = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
```

---

## Implementation Checklist

1. Add `GetModelAsync`, `RegisterModelAsync` to `IModelManagerClient`.
2. Implement all 9 methods in `ModelManagerClient` via `_pipeline` (escape path segments).
3. Internal ctor `(BackendClientHttpPipeline pipeline)`; update AppServices registration.
4. Remove all 9 model methods from `IBackendClient` and `BackendClient`.
5. Migrate `TrainingViewModelTests.GetModelAsync_ReturnsModelInfo` to `IModelManagerClient` (or document as transport-mock with honest classification).
6. Add seam test(s) in `BackendClientTransportPolicyTests` (e.g. `GetModelsAsync_ResolvesCorrectPath`, optional second for export/import).
7. Add `ModelMethodNames` array and `IBackendClient_DoesNotExposeModelMethods` / `BackendClient_DoesNotExposeModelMethods` in `BackendClientExtractionRegressionTests`.
8. Run proof: `dotnet build`, `dotnet test` (ModelManager, Model, BackendClientExtractionRegression as appropriate), `.\scripts\verify.ps1 -Quick`; record **actual** artifact path in STATE / inventory.

---

## Out-of-Scope

- Training panel bulk model flows beyond `GetModelAsync` test migration noted above.
- Backend route or Python model_facade behavior changes.
- Renaming DTOs or OpenAPI contract changes (keep parity with current BackendClient).

---

## Proof Requirements

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → PASS  
- Targeted tests: ModelManager / Model / `BackendClientExtractionRegression` → PASS  
- `.\scripts\verify.ps1 -Quick` → PASS  
- Record path to `artifacts/verify/{timestamp}/verification_report.md` (no hand-wavy “latest” only).

---

## PR-15 Selection Note

**Ranking (post PR-14):** Models (this slice) → Video → Mixer. Mixer deferred due to size (22 methods).
