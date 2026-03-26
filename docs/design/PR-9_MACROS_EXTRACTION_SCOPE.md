# PR-9: Macros Extraction — Scope (Frozen)

**Status:** Scoped (2026-03-22)
**Prerequisite:** PR-8 Option A complete
**Related:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)

---

## Objective

Extract all macro and automation curve methods from `IBackendClient`/`BackendClient` into `IMacroClient`/`MacroClient`. MacroClient switches from thin IBackendClient delegation to pipeline ownership (BackendClientHttpPipeline). Same pattern as PR-7 (ScriptEditorClient).

---

## Exact Methods Leaving IBackendClient

| Method | Signature | Route |
|--------|-----------|-------|
| GetMacrosAsync | `Task<List<Macro>> GetMacrosAsync(string? projectId = null, CancellationToken ct = default)` | GET /api/macros |
| GetMacroAsync | `Task<Macro> GetMacroAsync(string macroId, CancellationToken ct = default)` | GET /api/macros/{id} |
| CreateMacroAsync | `Task<Macro> CreateMacroAsync(Macro macro, CancellationToken ct = default)` | POST /api/macros |
| UpdateMacroAsync | `Task<Macro> UpdateMacroAsync(string macroId, Macro macro, CancellationToken ct = default)` | PUT /api/macros/{id} |
| DeleteMacroAsync | `Task<bool> DeleteMacroAsync(string macroId, CancellationToken ct = default)` | DELETE /api/macros/{id} |
| ExecuteMacroAsync | `Task<bool> ExecuteMacroAsync(string macroId, CancellationToken ct = default)` | POST /api/macros/{id}/execute |
| GetMacroExecutionStatusAsync | `Task<MacroExecutionStatus> GetMacroExecutionStatusAsync(string macroId, CancellationToken ct = default)` | GET /api/macros/{id}/execution-status |
| GetAutomationCurvesAsync | `Task<List<AutomationCurve>> GetAutomationCurvesAsync(string trackId, CancellationToken ct = default)` | GET /api/macros/automation/{trackId} |
| CreateAutomationCurveAsync | `Task<AutomationCurve> CreateAutomationCurveAsync(AutomationCurve curve, CancellationToken ct = default)` | POST /api/macros/automation |
| UpdateAutomationCurveAsync | `Task<AutomationCurve> UpdateAutomationCurveAsync(string curveId, AutomationCurve curve, CancellationToken ct = default)` | PUT /api/macros/automation/{id} |
| DeleteAutomationCurveAsync | `Task<bool> DeleteAutomationCurveAsync(string curveId, CancellationToken ct = default)` | DELETE /api/macros/automation/{id} |

---

## Destination

- **Interface:** `IMacroClient` (extend with GetMacroAsync, UpdateMacroAsync, UpdateAutomationCurveAsync — currently not on interface; no callers; add for API completeness)
- **Implementation:** `MacroClient` — inject `BackendClientHttpPipeline` (or `BackendHttpContext`), implement all 11 methods via `pipeline.GetAsync`, `pipeline.PostAsync`, `pipeline.PutAsync`, `pipeline.SendRequestAsync`
- **Pattern:** Same as [ScriptEditorClient](e:\VoiceStudio\src\VoiceStudio.App\Services\ScriptEditorClient.cs) — internal ctor for tests; DI uses `BackendHttpContext.Pipeline`

---

## Call Sites

| Caller | Methods Used | Change |
|--------|--------------|--------|
| MacroViewModel | GetMacrosAsync, CreateMacroAsync, DeleteMacroAsync, ExecuteMacroAsync, GetMacroExecutionStatusAsync, GetAutomationCurvesAsync, CreateAutomationCurveAsync, DeleteAutomationCurveAsync | None (uses IMacroClient) |
| MacroActions (CreateMacroAction, DeleteMacroAction, CreateAutomationCurveMacroAction, DeleteAutomationCurveMacroAction) | Inject IMacroClient; Undo/Redo modify local collections only | None |
| MacroView | Resolves IMacroClient via AppServices | None |
| MacroClient (current) | Delegates to IBackendClient | Replace with pipeline |

---

## Retained Exceptions

- None expected. All macro/automation traffic routes through IMacroClient.

---

## Proof Requirements

1. Build passes: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. Targeted tests: `dotnet test --filter "FullyQualifiedName~Macro"`
3. verify.ps1 -Quick: PASS
4. Extraction inventory: Add PR-9 section with proof artifact path
5. Sweep: No `IBackendClient.GetMacro`, `_backend.GetMacro`, etc.

---

## Out-of-Scope

- No Macros UI changes
- No Workflows or Models bundling
- No changes to MacroViewModel, MacroActions beyond what DI requires

---

## Migration Steps

1. Extend IMacroClient with GetMacroAsync, UpdateMacroAsync, UpdateAutomationCurveAsync (if not present)
2. MacroClient: change ctor from `(IBackendClient)` to `(BackendClientHttpPipeline pipeline)`; implement all 11 methods via pipeline
3. AppServices: register MacroClient with `sp.GetRequiredService<BackendHttpContext>().Pipeline`
4. Remove all 11 methods from IBackendClient
5. Remove all 11 methods from BackendClient
6. Update MockBackendClient: remove macro method stubs
7. Add seam test: `MacroClient_GetMacrosAsync_ResolvesCorrectPath` (or equivalent)
8. Run proof; record actual artifact path in STATE.md and inventory

---

## Status

- [x] Scoped
- [ ] In Progress
- [ ] Complete
