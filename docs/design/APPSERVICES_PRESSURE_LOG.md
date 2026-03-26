# AppServices Pressure Log

**Purpose:** Track every new registration demanded by architecture work; which area forced it; whether it indicates a missing facade/module boundary.

**Date:** 2026-03-21  
**Related:** Architecture Wave Execution Plan, `APPSERVICES_SPLIT_PLAN.md` § Composition-Root Discipline

---

## Log format

| Date | Registration | Reason | Area |
|------|--------------|--------|------|
| 2026-03-21 | `IHealthVersionClient` | HealthVersionClient extracted; callers migrated from BackendClient | PR-5 Health/Version extraction |
| 2026-03-21 | `ISearchClient` | SearchClient extracted from BackendClient; uses `BackendClientHttpPipeline` | PR-4 Search extraction |
| 2026-03-21 | `IPluginHealthClient` | PluginHealthClient extracted from BackendClient; uses `BackendClientHttpPipeline` | PR-3 Plugin extraction |
| 2026-03-21 | `ITelemetryClient` | TelemetryClient extracted; DiagnosticsClient, MainWindow.StatusBar use for telemetry/traces | PR-6 Telemetry extraction |

---

## Notes

- **Facade-based:** SearchClient, PluginHealthClient, HealthVersionClient use `BackendHttpContext.Pipeline` — no new HttpClient factory.
- **SearchOverlayCoordinator:** Created in MainWindow (shell-level); not in AppServices. No new registration.
- **Pressure:** Four new registrations from BackendClient extractions (IHealthVersionClient, ISearchClient, IPluginHealthClient, ITelemetryClient). AppServices did not grow from MainWindow refactors.
