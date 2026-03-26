# PR-10: Workflows Extraction — Scope (Frozen)

**Status:** Scoped (2026-03-22)
**Prerequisite:** PR-9 Macros complete
**Related:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)

---

## Objective

Extract all workflow methods from `IBackendClient`/`BackendClient` into `IWorkflowAutomationClient`/`WorkflowAutomationClient`. WorkflowAutomationClient switches from thin IBackendClient delegation to pipeline ownership (BackendClientHttpPipeline). Same pattern as PR-9 (MacroClient).

---

## Exact Methods Leaving IBackendClient

| Method | Signature | Route |
|--------|-----------|-------|
| GetWorkflowsAsync | `Task<List<Workflow>> GetWorkflowsAsync(int skip = 0, int limit = 100, bool enabledOnly = false, CancellationToken ct = default)` | GET /api/workflows |
| GetWorkflowAsync | `Task<Workflow> GetWorkflowAsync(string workflowId, CancellationToken ct = default)` | GET /api/workflows/{id} |
| CreateWorkflowAsync | `Task<Workflow> CreateWorkflowAsync(WorkflowCreateRequest request, CancellationToken ct = default)` | POST /api/workflows |
| UpdateWorkflowAsync | `Task<Workflow> UpdateWorkflowAsync(string workflowId, WorkflowUpdateRequest request, CancellationToken ct = default)` | PUT /api/workflows/{id} |
| DeleteWorkflowAsync | `Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken ct = default)` | DELETE /api/workflows/{id} |
| ExecuteWorkflowAsync | `Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? inputData = null, CancellationToken ct = default)` | POST /api/workflows/{id}/execute |

---

## Destination

- **Interface:** `IWorkflowAutomationClient` — extend with GetWorkflowsAsync, GetWorkflowAsync, DeleteWorkflowAsync (currently has Create, Update, Execute only)
- **Implementation:** `WorkflowAutomationClient` — inject `BackendClientHttpPipeline` instead of IBackendClient; implement all 6 methods via pipeline
- **Pattern:** Same as MacroClient — internal ctor for tests; DI uses `BackendHttpContext.Pipeline`

---

## Call Sites

| Caller | Methods Used | Change |
|--------|--------------|--------|
| WorkflowAutomationViewModel | CreateWorkflowAsync, UpdateWorkflowAsync, ExecuteWorkflowAsync | None (uses IWorkflowAutomationClient) |
| WorkflowAutomationClient (current) | Delegates all 6 to IBackendClient | Replace with pipeline |
| GetWorkflowsAsync, GetWorkflowAsync, DeleteWorkflowAsync | No current UI callers | Add to interface for API completeness; future list/delete UI will use |

**Sweep (2026-03-22):** No other callers of workflow methods on IBackendClient. WorkflowAutomationViewModel is sole consumer via IWorkflowAutomationClient.

---

## Retained Exceptions

- None expected. All workflow traffic routes through IWorkflowAutomationClient after extraction.

---

## Proof Requirements

1. Build passes: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. Targeted tests: `dotnet test --filter "FullyQualifiedName~Workflow"`
3. verify.ps1 -Quick: PASS
4. Extraction inventory: Add PR-10 section with proof artifact path
5. Sweep: No `IBackendClient.GetWorkflow`, `_backend.GetWorkflow`, etc.
6. Anti-regression guard: Add `IBackendClient_DoesNotExposeWorkflowMethods`, `BackendClient_DoesNotExposeWorkflowMethods` to BackendClientExtractionRegressionTests

---

## Out-of-Scope

- No Workflows UI changes
- No Models or Effects bundling
- No changes to WorkflowAutomationViewModel beyond what DI requires (none; already uses IWorkflowAutomationClient)

---

## Migration Steps

1. Extend IWorkflowAutomationClient with GetWorkflowsAsync, GetWorkflowAsync, DeleteWorkflowAsync
2. WorkflowAutomationClient: change ctor from `(IBackendClient)` to `(BackendClientHttpPipeline pipeline)`; implement all 6 methods via pipeline
3. AppServices: register WorkflowAutomationClient with `sp.GetRequiredService<BackendHttpContext>().Pipeline`
4. Remove all 6 methods from IBackendClient
5. Remove all 6 methods from BackendClient
6. Update MockBackendClient: remove workflow method stubs if any
7. Add seam test: `GetWorkflowsAsync_ResolvesCorrectPath` in BackendClientTransportPolicyTests
8. Add anti-regression: workflow method names to BackendClientExtractionRegressionTests
9. Run proof; record actual artifact path in STATE.md and inventory

---

## Status

- [x] Scoped
- [ ] In Progress
- [ ] Complete
