# PR-16 Video Extraction Scope

**Status:** Frozen (2026-03-23)  
**Related:** [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md), [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)  
**Pattern:** scope → extract → delete → sweep → prove → ledger. No improvisation.

---

## Decision

**PR-16 = Video (5 methods, 2 thin clients)**

Chosen as next slice after PR-15 Models: smaller blast radius than Mixer (22 methods); two thin clients (IVideoGenClient, IVideoEditClient) already exist; coherent `/api/video/*` family.

---

## Methods Leaving IBackendClient

| Method                | Client           | Endpoint                             | HTTP |
| --------------------- | ---------------- | ------------------------------------ | ---- |
| ListVideoEnginesAsync | IVideoGenClient  | `/api/video/engines/list`             | GET  |
| GenerateVideoAsync    | IVideoGenClient  | `/api/video/generate`                | POST |
| UpscaleVideoAsync     | IVideoGenClient  | `/api/video/upscale`                 | POST |
| GetVideoInfoAsync     | IVideoEditClient | `/api/video/edit/info?path={path}`   | GET  |
| EditVideoAsync        | IVideoEditClient | `/api/video/edit`                    | POST |

*(Endpoints verified from [BackendClient.cs](../../src/VoiceStudio.App/Services/BackendClient.cs) 2026-03-23.)*

---

## Destination

- **Interfaces:** `IVideoGenClient` (3 methods), `IVideoEditClient` (2 methods) — both already declared.
- **Clients:** `VideoGenClient`, `VideoEditClient` — replace `IBackendClient _backend` with `BackendClientHttpPipeline`; internal ctor pattern (same as ModelManagerClient / BackupRestoreClient).
- **DI:** [AppServices.cs](../../src/VoiceStudio.App/Services/AppServices.cs) — register `IVideoGenClient` and `IVideoEditClient` with `BackendHttpContext().Pipeline`.

---

## Callers

| Caller              | Usage                                                                 |
| ------------------- | --------------------------------------------------------------------- |
| VideoGenViewModel   | ListVideoEnginesAsync, GenerateVideoAsync, UpscaleVideoAsync via IVideoGenClient |
| VideoEditViewModel  | GetVideoInfoAsync, EditVideoAsync via IVideoEditClient                 |

**No other production callers** of these five methods on `IBackendClient` expected; confirm with sweep during implementation.

---

## Pipeline / Retained Exceptions

- All five methods use `PostAsJsonAsync` / `GetAsync` patterns. Migrate to `BackendClientHttpPipeline.PostAsync<TRequest, TResponse>` and `BackendClientHttpPipeline.GetAsync<T>`.
- GetVideoInfoAsync: query param `path` — use `$"/api/video/edit/info?path={Uri.EscapeDataString(videoPath)}"`.
- DTOs unchanged: `VideoGenerateRequest`, `VideoGenerateResponse`, `VideoUpscaleRequest`, `VideoUpscaleResponse`, `VideoInfo`, `VideoEditRequest`, `VideoEditResponse`.

---

## Implementation Checklist

1. Add internal ctor `(BackendClientHttpPipeline pipeline)` to VideoGenClient and VideoEditClient.
2. Implement all 5 methods in clients via `_pipeline` (escape path/query segments).
3. Update AppServices registration for both clients.
4. Remove all 5 video methods from `IBackendClient` and `BackendClient`.
5. Add seam tests in `BackendClientTransportPolicyTests` (minimum two: one per client).
6. Add `VideoMethodNames` array and `IBackendClient_DoesNotExposeVideoMethods` / `BackendClient_DoesNotExposeVideoMethods` in `BackendClientExtractionRegressionTests`.
7. Run proof: `dotnet build`, targeted Video + BackendClientExtractionRegression tests, `.\scripts\verify.ps1 -Quick`; record **actual** artifact path in STATE and inventory.

---

## Out-of-Scope

- GetVideoAsync, ConvertVoiceAsync (different endpoint families; may be future slices).
- Backend route or Python video behavior changes.
- Consolidating IVideoGenClient and IVideoEditClient into one interface (keep split unless scope doc approves).
- DTO redesign or OpenAPI contract changes.

---

## Proof Requirements

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → PASS  
- Targeted tests: VideoGen / VideoEdit / `BackendClientExtractionRegression` → PASS  
- `.\scripts\verify.ps1 -Quick` → PASS  
- Record path to `artifacts/verify/{timestamp}/verification_report.md` (no hand-wavy "latest" only).

---

## PR-16 Selection Note

**Ranking (post PR-15):** Video (this slice) → Mixer. Mixer deferred due to size (22 methods).
