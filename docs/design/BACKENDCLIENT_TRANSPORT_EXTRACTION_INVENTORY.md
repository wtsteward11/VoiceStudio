# BackendClient transport extraction inventory

**Purpose:** Line-anchored map of [`BackendClient.cs`](../../src/VoiceStudio.App/Services/BackendClient.cs) for PR-1 (policy-only extraction) vs later PRs. **Not** migration-complete claims.  
**Date:** 2026-03-20  
**Related:** [`FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md`](FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md) §1, [`APPSERVICES_SPLIT_PLAN.md`](APPSERVICES_SPLIT_PLAN.md)

---

## Reconciliation: `BackendTransport` vs `BackendClient`

| Surface | Location | Pattern |
|--------|----------|---------|
| **BackendClient** | `Services/BackendClient.cs` | Throws `BackendException` subclasses; `ExecuteWithRetryAsync` + `CircuitBreaker` + connection flags |
| **BackendTransport** | `Services/Gateways/BackendTransport.cs` + `IBackendTransport` | Returns `GatewayResult<T>`; parallel `ExecuteAsync`, `CreateErrorFromResponseAsync`, `HandleResponseAsync` |

**PR-1 convergence strategy (frozen):** Extract an **`internal`** [`BackendClientHttpPipeline`](../../src/VoiceStudio.App/Services/BackendClientHttpPipeline.cs) that preserves **BackendClient’s throw-based contract** and shares **one** `HttpClient` instance. **Do not** route `BackendClient` through `IBackendTransport` in PR-1 (would force `GatewayResult` at the façade or a behavior-changing adapter).

**PR-2 (done 2026-03-20):** Shared `StandardErrorResponseParser` — `BackendClientHttpPipeline.CreateExceptionFromResponseAsync` and `BackendTransport.CreateErrorFromResponseAsync` both call it; no `GatewayResult` in BackendClient.

---

## Bucket A — Transport policy

| Item | Approx. lines | Notes |
|------|---------------|--------|
| Handler chain in ctor | 256–274 | `DegradedModeClearHandler` → `RequestMetricsHandler` → `CorrelationIdHandler` → inner |
| `HttpClient` construction | 270–274 | `BaseAddress`, `Timeout = config.RequestTimeout` |
| `_circuitBreaker` field + init | 226–227, 279–280 | Threshold 5, 30s reset |
| `MaxRetries`, `RetryDelayMs` | 227–228 | Used by retry path |
| Connection fields | 230–233 | `_isConnected`, `_lastConnectionCheck`, `ConnectionCheckIntervalSeconds` |
| `IsConnected` | 309 | |
| `CircuitState` | 319 | |
| `CheckHealthAsync` | 439–454 | Direct GET `/api/health`, mutates connection flags |
| `ExecuteWithRetryAsync<T>` | 1296–1340 | `UpdateConnectionStatusAsync` + `_circuitBreaker` + `RetryHelper.ExecuteWithExponentialBackoffAsync` |
| `UpdateConnectionStatusAsync` | 1345–1363 | Periodic GET `/api/health` |
| `TryCheckHealthAsync` (static) | 462–479 | **Smell:** `AppServices.GetService<HttpClient>()` — **PR-1 fix:** dedicated short-lived `HttpClient` for probe |

**Cross-ref `BackendTransport`:** ctor 48–65; `ExecuteAsync` wraps circuit + retry (different API shape).

---

## Bucket B — Serialization / response handling

| Item | Approx. lines | Notes |
|------|---------------|--------|
| `_jsonOptions` | 225, 277 | `JsonSerializerOptionsFactory.BackendApi` |
| `SendRequestAsync` (POST JSON) | 321–350 | Serialize body, `PostAsync`, deserialize |
| `SendRequestAsync` (method overload) | 355–428 | GET/POST/PUT/DELETE branches |
| `GetAsync<T>` | 3162–3184 | |
| `PostAsync<TRequest,TResponse>` | 3189–3219 | |
| `PostAsync<TRequest>` (void) | 3224–3240 | |
| `PutAsync<TRequest,TResponse>` | 3245–3271 | |
| `CreateExceptionFromResponseAsync` | 1365–1450 | StandardErrorResponse → `BackendException` hierarchy + `IsRetryable` |

**Cross-ref `BackendTransport`:** `HandleResponseAsync` 207–237; `CreateErrorFromResponseAsync` 239+ (`GatewayError`).

---

## Bucket C — Unsafe / global concerns

| Concern | Location | PR-1 action |
|---------|----------|-------------|
| Static service locator | `TryCheckHealthAsync` → `AppServices.GetService<HttpClient>()` | Remove; use `new HttpClient` + absolute URL + timeout |
| Shared `HttpClient.Timeout` mutation | `UploadFilesWithProgressAsync` 4353–4371 | Replace with **`CancellationTokenSource.CreateLinkedTokenSource`** + `CancelAfter(timeout)`; never mutate `_httpClient.Timeout` |
| Connection side effects inside retry path | `ExecuteWithRetryAsync` / `UpdateConnectionStatusAsync` | Moved with pipeline; behavior unchanged |

**Callers of `TryCheckHealthAsync`:** `MainWindow.Smoke.cs` (~473, 477, 790, 794, 969), `App.xaml.cs` (~1287).

---

## Bucket D — Feature surfaces (by `/api/` prefix)

Grouped by first path segment after `/api/`. Endpoint **methods** stay in `BackendClient` for PR-1; only policy/helpers move.

| Prefix / area | Examples (non-exhaustive) |
|---------------|---------------------------|
| **Health / version** | `/api/health`, `/api/version/`, `/api/version/compatibility` |
| **MCP** | `/api/mcp/{operation}` |
| **Voice** | `/api/voice/synthesize`, `analyze`, `clone`, `audio/{id}` |
| **Audio** | `/api/audio/file`, `export`, `formats`, `upload`, waveform/spectrogram/meters/radar/loudness/phase |
| **Projects + timeline** | `/api/projects/.../audio`, `tracks`, `clips`, `markers` |
| **Macros** | `/api/macros`, `automation` |
| **Workflows** | `/api/workflows` |
| **Models / engine** | `/api/models`, `/api/engine/telemetry` |
| **Effects** | `/api/effects/chains`, `presets` |
| **Batch** | `/api/batch/jobs`, `queue`, `quality` |
| **Transcribe** | `/api/transcribe/` |
| **Training** | `/api/training/...` |
| **Ensemble** | `/api/ensemble/multi-engine` |
| **Mixer** | `/api/mixer/state`, `presets`, sends/returns/subgroups/master/channels |
| **Video** | `/api/video/generate`, `upscale`, `engines`, `{id}`, `voice/convert` |
| **Quality** | `/api/quality/...` (via `PostAsync` helpers ~3559+) |
| **Script editor** | `/api/script-editor/...` |
| **Pipeline** | `/api/pipeline/...` |
| **Plugins** | `api/plugins/...` (some paths omit leading `/` — invariant risk, PR-2) |

**Regenerate hint:** `rg '/api/|api/plugins' src/VoiceStudio.App/Services/BackendClient.cs`

---

## PR scope freeze

### PR-1 — IN (no public API rename; no endpoint moves)

- New internal **`BackendClientHttpPipeline`**: `ExecuteWithRetryAsync`, `UpdateConnectionStatusAsync`, `CreateExceptionFromResponseAsync`, `SendRequestAsync` (both), `GetAsync`, `PostAsync` (both), `PutAsync`.
- **`BackendClient`** retains ctor handler chain, `_httpClient`, `_config`, `_requestCoordinator`, `WebSocketService`, all feature methods; delegates generic HTTP + retry to `_pipeline`.
- **Fix:** `TryCheckHealthAsync` (no `AppServices`).
- **Fix:** upload timeout via linked CTS, not `_httpClient.Timeout` mutation.

### PR-1 — OUT

- Re-grouping endpoint methods by domain.
- Multipart / streaming one-offs (except timeout fix in existing upload method).
- `AppServices` registration splits.
- `MainWindow` decomposition.
- Switching `BackendClient` to `IBackendTransport` / `GatewayResult`.

### PR-2 (done 2026-03-20)

- Shared `StandardErrorResponseParser` — `BackendClientHttpPipeline` + `BackendTransport` use it.
- Path normalization: `/api/plugins` (leading slash) enforced; `GetPluginHealthDashboardAsync_ResolvesCorrectPath` test.

### PR-3 — Plugin health extraction (done 2026-03-21)

**Current state (2026-03-21):** BackendClient.cs = **4110 lines**. Bucket D: ~100 endpoint references.

**Single next extraction:** `/api/plugins/` methods → dedicated `IPluginHealthClient` / `PluginHealthClient`.

| Method | Lines | Notes |
|--------|-------|-------|
| `GetPluginHealthDashboardAsync` | 4050–4066 | Uses `SendRequestAsync`; path `/api/plugins/health/dashboard` |
| `GetPluginMetricsAsync` | 4068–4085 | Uses `SendRequestAsync`; path `/api/plugins/{id}/metrics` |
| `ExportPluginMetricsAsync` | 4087–4101 | Uses `_httpClient.GetAsync` directly (not pipeline); path `/api/plugins/metrics/export` |

**PR-3 — IN**

- New `IPluginHealthClient` interface with `GetPluginHealthDashboardAsync`, `GetPluginMetricsAsync`, `ExportPluginMetricsAsync`.
- New `PluginHealthClient` implementing `IPluginHealthClient`; uses `BackendClientHttpPipeline` or shared `HttpClient`/`SendRequestAsync` pattern.
- Migrate `ExportPluginMetricsAsync` to use pipeline (replace raw `_httpClient.GetAsync`).
- `BackendClient` delegates plugin health calls to `IPluginHealthClient` (injected) or removes methods and updates callers.
- Test migration: `GetPluginHealthDashboardAsync_ResolvesCorrectPath` → `PluginHealthClient` or equivalent.

**PR-3 — OUT**

- Other domain extractions (voice, audio, training, etc.).
- BackendClient facade changes beyond plugin methods.
- Changing `IBackendClient` surface for non-plugin callers.

**PR-3 — DONE (2026-03-21):** `PluginHealthClient` owns HTTP via `BackendClientHttpPipeline`; `BackendHttpContext` shared with `BackendClient`; 3 methods removed from `BackendClient`/`IBackendClient`; `GetPluginHealthDashboardAsync_ResolvesCorrectPath` tests `PluginHealthClient`.

### PR-4 — Search extraction (done 2026-03-21)

**Single extraction:** `/api/search` → `ISearchClient` / `SearchClient`.

| Method | Notes |
|--------|-------|
| `SearchAsync` | Removed from `BackendClient`/`IBackendClient`; `SearchClient` owns HTTP via `BackendClientHttpPipeline` |

**PR-4 — IN**

- `SearchClient` uses `BackendClientHttpPipeline.GetAsync<SearchResponse>`; internal ctor for testability.
- `ISearchClient` registered with `BackendHttpContext.Pipeline` in AppServices.
- `SearchAsync` removed from `IBackendClient`, `BackendClient`, `MockBackendClient`.
- Test: `SearchAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.

**PR-4 — OUT**

- Other domain extractions (voice, audio, training, etc.).

**PR-5 — Health/Version extraction (done 2026-03-21)**

**Single extraction:** `/api/health`, `/api/version/`, `/api/version/compatibility` → `IHealthVersionClient` / `HealthVersionClient`.

| Method | Notes |
|--------|-------|
| CheckHealthAsync | Removed from BackendClient; HealthVersionClient uses pipeline.CheckHealthAsync |
| CheckApiVersionAsync | Removed from BackendClient; HealthVersionClient owns |
| GetApiVersionInfoAsync | Removed from BackendClient; HealthVersionClient owns |
| ValidateApiVersionOnStartupAsync | Removed from BackendClient; HealthVersionClient owns |

**PR-5 — IN**

- Migrated callers: DiagnosticsClient, StatusBarActivityService, DeferredServiceInitializer, MainWindow.StatusBar, DiagnosticsView (TestConnection, Reconnect).
- **Retained exception:** `BackendClient.TryCheckHealthAsync` (static) for bootstrap/self-test; callers: App.xaml.cs, MainWindow.Smoke.cs. Instance health uses IHealthVersionClient.
- **Proof/tests:** `BackendClientTransportPolicyTests.CheckHealthAsync_FailureThenRecovery_ConnectionStateCorrect`, `CheckHealthAsync_ResolvesCorrectPath`; `StatusBarActivityServiceTests`; `DiagnosticsViewModelSeamTests`; `DiagnosticsClientTests.CheckHealthAsync_DelegatesToHealthVersionClient`; `StatusBarActivityServiceTests.StartMonitoring_CallsHealthVersionClient_WhenMonitoring`; `DeferredServiceInitializerTests.BackendHealthCheck_InvokesIHealthVersionClient_WhenRegistered`. PR-5-focused filter: 32+ tests pass; verify.ps1 -Quick verified pass.
- **Sweep:** No remaining `backendClient.CheckHealthAsync` or `_backend.CheckHealthAsync`; version methods only on IHealthVersionClient/HealthVersionClient. Boundary clean.

### PR-6 — Telemetry/diagnostics extraction (done 2026-03-21)

**Single extraction:** `/api/engine/telemetry`, `/api/v1/diagnostics/traces` → `ITelemetryClient` / `TelemetryClient`.

| Method | Notes |
|--------|-------|
| GetTelemetryAsync | Removed from BackendClient; TelemetryClient owns `/api/engine/telemetry` |
| GetTracesAsync | TelemetryClient owns `/api/v1/diagnostics/traces`; DiagnosticsClient delegates to ITelemetryClient |

**PR-6 — IN**

- `ITelemetryClient` interface with `GetTelemetryAsync`, `GetTracesAsync`.
- `TelemetryClient` uses `BackendClientHttpPipeline` via `BackendHttpContext.Pipeline`.
- `DiagnosticsClient` constructor takes `(IConnectionStatusClient, IHealthVersionClient, ITelemetryClient)` (PR-8); delegates telemetry/traces to `_telemetry`.
- `MainWindow.StatusBar` uses `GetTelemetryClient()` for GPU/VRAM display.
- `GetTelemetryAsync` removed from `IBackendClient`, `BackendClient`.
- Tests: `DiagnosticsClientTests.GetTelemetryAsync_DelegatesToTelemetryClient`, `GetTracesAsync_DelegatesToTelemetryClient`.

**PR-6 — OUT**

- Other domain extractions (voice, audio, training, etc.).

### PR-7 — Script editor extraction (done 2026-03-21)

**Single extraction:** `/api/script-editor/` → `IScriptEditorClient` / `ScriptEditorClient` (already existed; migrated from IBackendClient delegation to pipeline ownership).

| Method | Notes |
|--------|-------|
| GetScriptsAsync | ScriptEditorClient owns; uses pipeline.GetAsync |
| GetScriptAsync | ScriptEditorClient owns |
| CreateScriptAsync | ScriptEditorClient owns |
| UpdateScriptAsync | ScriptEditorClient owns |
| DeleteScriptAsync | ScriptEditorClient owns |
| AddSegmentToScriptAsync | ScriptEditorClient owns |
| RemoveSegmentFromScriptAsync | ScriptEditorClient owns |

**PR-7 — IN**

- ScriptEditorClient uses `BackendClientHttpPipeline` via `BackendHttpContext.Pipeline`; no IBackendClient.
- AppServices: `services.AddSingleton<IScriptEditorClient>(sp => new ScriptEditorClient(sp.GetRequiredService<BackendHttpContext>().Pipeline))`.
- All 7 methods removed from `IBackendClient`, `BackendClient`.
- Test: `GetScriptsAsync_ResolvesCorrectPath` in `BackendClientTransportPolicyTests`.
- **Proof (closure-grade 2026-03-22):**
  - `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → PASS
  - `dotnet test ... --filter "FullyQualifiedName~ScriptEditor"` → 24 passed, 0 failed, 0 skipped
  - verify.ps1 -Quick artifact: `artifacts/verify/20260322_011101/verification_report.md` (or `artifacts/verify/latest/`)
- **Sweep (2026-03-22):** IBackendClient clean; BackendClient.cs clean; all callers route through IScriptEditorClient. No orphaned usage.
  - grep `backendClient.GetScript`, `_backend.GetScript`, `script-editor` in BackendClient.cs → no matches
  - grep `IBackendClient.*GetScript` in src → no matches
  - ScriptEditorViewModel, ScriptEditorView, tests use `_scriptEditorClient` / `IScriptEditorClient` only

### PR-8 — Connection status extraction (done 2026-03-22)

**Single extraction:** `IsConnected` / `CircuitState` → `IConnectionStatusClient` / `ConnectionStatusClient` (pipeline state, no HTTP).

| Method | Notes |
|--------|-------|
| IsConnected | Removed from IBackendClient; ConnectionStatusClient delegates to pipeline.IsConnected |
| CircuitState | Removed from BackendClient public surface; ConnectionStatusClient delegates to pipeline.CircuitState |

**PR-8 — IN**

- `IConnectionStatusClient` interface with `IsConnected`, `CircuitState` (from `VoiceStudio.App.Utilities.RetryHelper`).
- `ConnectionStatusClient` takes `BackendHttpContext`; delegates to `Pipeline.IsConnected` and `Pipeline.CircuitState`. No HTTP.
- `DiagnosticsClient` constructor takes `(IConnectionStatusClient, IHealthVersionClient, ITelemetryClient)` — no IBackendClient.
- `SystemStore` uses `IConnectionStatusClient` for `IsBackendConnected`; retains `IBackendClient` for `BaseAddress` cast only.
- `IsConnected` and `CircuitState` removed from `IBackendClient` and `BackendClient` public surface.
- Tests: `ConnectionStatusClientTests.ConnectionStatusClient_DelegatesToPipeline_IsConnectedAndCircuitState`, `IBackendClient_DoesNotExposeIsConnected`; `BackendClientTransportPolicyTests.CheckHealthAsync_FailureThenRecovery_ConnectionStateCorrect` uses `IConnectionStatusClient`.
- **Proof (2026-03-22):** `dotnet build` PASS; `dotnet test --filter "FullyQualifiedName~DiagnosticsClient|FullyQualifiedName~ConnectionStatus|FullyQualifiedName~BackendClientTransportPolicy"` 29 passed; verify.ps1 -Quick artifact: `artifacts/verify/20260322_044739/` (or latest).
- **Sweep:** No `IBackendClient.IsConnected`; no `backendClient.IsConnected` for connection status (tests use `IConnectionStatusClient`).

**Next recommended slice:** Macros (~11), Workflows (~6), Models (~9), Effects (~8).

**What remains in BackendClient (Bucket D):** MCP, Voice, Audio, Projects, Macros, Workflows, Models/engine, Effects, Batch, Transcribe, Training, Ensemble, Mixer, Video, Quality, Pipeline. Search, Health/Version, Telemetry, Script editor, and Connection status are fully extracted.

---

## Exit criteria

- [x] This inventory committed with PR-1 scope table.
- [x] Policy block moved to `BackendClientHttpPipeline.cs` (verify via diff / type location).
- [x] `BackendClientTransportPolicyTests` green (retry 5xx, 401 no retry, 422 mapping, timeout mapping).
- [x] `dotnet build` App + targeted `dotnet test`; run `verify.ps1 -Quick` before merge.
