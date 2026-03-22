# PR-8 Scope: Telemetry/Diagnostics-Adjacent Continuation

**Date:** 2026-03-22  
**Status:** Scoped — ready for execution  
**Prerequisite:** Tasks 1-6 of Repair State Oracle plan fulfilled

---

## Context

- PR-5: Health/Version extracted to `IHealthVersionClient`
- PR-6: Telemetry extracted to `ITelemetryClient` (GetTelemetryAsync, GetTracesAsync)
- PR-7: Script editor extracted to `IScriptEditorClient`

Next slice: **telemetry/diagnostics-adjacent cleanup** to continue shrinking BackendClient.

---

## PR-8 Options

### Option A — DiagnosticsClient ownership consolidation

If `DiagnosticsClient` still delegates any calls to `IBackendClient` (beyond health/telemetry), extract those to a dedicated client or expand `ITelemetryClient`/`IHealthVersionClient` as appropriate.

**Action:** Audit `DiagnosticsClient` constructor and methods; identify any remaining `_backend.*` calls; move to appropriate client.

### Option B — Remaining diagnostics/engine endpoints

Scan BackendClient for `/api/engine/`, `/api/v1/diagnostics/`, or related paths. If any remain beyond what PR-6 extracted, add to `ITelemetryClient` or create a small diagnostics-specific client.

**Action:** `rg '/api/engine|/api.*diagnostic' src/VoiceStudio.App/Services/BackendClient.cs`

### Option C — Macros/Workflows/Models/Effects (inventory recommendation)

If diagnostics surface is clean, follow extraction inventory: **Macros (~11)**, **Workflows (~6)**, **Models (~9)**, **Effects (~8)** as next slice.

---

## Recommended Order

1. Run Option A + B audits.
2. If diagnostics surface is clean → proceed with Option C (Macros or Workflows first).
3. If diagnostics has remaining methods → extract those as PR-8, then Option C as PR-9.

---

## Exit Criteria

- [ ] DiagnosticsClient and BackendClient audited for diagnostics/telemetry/engine endpoints
- [ ] PR-8 task brief created with chosen slice
- [ ] ACTIVE WINDOW updated with PR-8 as active task
