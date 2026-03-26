# PR-13 Pipeline Extraction Scope

**Status:** Frozen (2026-03-22)  
**Related:** [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md), [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)  
**Pattern:** scope → extract → delete → sweep → prove → ledger. No improvisation.

---

## Decision

**PR-13 = Pipeline (2 methods)**

Chosen per evidence-based ranking: smallest surface, single caller, thin client exists, lowest risk.

---

## Methods Leaving IBackendClient

| Method | Endpoint | Notes |
|--------|----------|-------|
| GetPipelineProvidersAsync | GET `/api/pipeline/providers` | Returns PipelineProvidersResponse |
| ProcessPipelineAsync | POST `/api/pipeline/process` | PipelineRequest → PipelineResponse |

---

## Destination

- **Interface:** `IPipelineConversationClient` (already exists)
- **Client:** `PipelineConversationClient` (already exists; currently delegates to IBackendClient)
- **Migration:** Replace IBackendClient with BackendClientHttpPipeline; implement both methods via pipeline

---

## Callers

| Caller | Usage |
|--------|-------|
| PipelineConversationViewModel | GetPipelineProvidersAsync, ProcessPipelineAsync |
| PipelineConversationViewModelSeamTests | Mocks IPipelineConversationClient |

**Caller count:** 1 ViewModel. No other runtime callers.

---

## Implementation Checklist

1. Add `BackendClientHttpPipeline` to PipelineConversationClient ctor (internal)
2. Implement GetPipelineProvidersAsync via `_pipeline.GetAsync<PipelineProvidersResponse>`
3. Implement ProcessPipelineAsync via `_pipeline.PostAsync<PipelineRequest, PipelineResponse>`
4. Update AppServices DI: register with `BackendHttpContext().Pipeline` instead of IBackendClient
5. Remove GetPipelineProvidersAsync, ProcessPipelineAsync from IBackendClient, BackendClient
6. Sweep MockBackendClient for preset stubs
7. Add seam tests: GetPipelineProvidersAsync_ResolvesCorrectPath, ProcessPipelineAsync_ResolvesCorrectPath
8. Add anti-regression: IBackendClient_DoesNotExposePipelineMethods, BackendClient_DoesNotExposePipelineMethods
9. Run proof: build, targeted tests, verify.ps1 -Quick
10. Update extraction inventory, STATE.md

---

## Retained Exceptions

None.

---

## Out-of-Scope

- Other thin clients (Models, Mixer, Backup, Video)
- Pipeline UI redesign
- Pipeline DTO changes

---

## Proof Requirements

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → PASS
- `dotnet test --filter "FullyQualifiedName~PipelineConversation|FullyQualifiedName~BackendClientExtractionRegression"` → PASS
- `.\scripts\verify.ps1 -Quick` → PASS
- Record actual artifact path (no "latest")
