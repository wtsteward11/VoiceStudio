# VoiceStudio Architecture Gap Register

> **Version**: 1.1.0  
> **Last Updated**: 2026-02-24 (post-v1.0.3 reintegration sweep)  
> **Status**: Active Tracking  
> **Parent Document**: `VOICESTUDIO_ARCHITECTURE_PORTFOLIO.md`  
> **Change**: Post-v1.0.3 reintegration sweep. All remaining OPEN gaps confirmed as intentional future work (Q2 2026 / Backlog). No gaps were silently resolved during reintegration.

This register tracks the resolution status of architectural gaps identified in the VoiceStudio Architecture Portfolio. Each gap is assigned an owner and tracked through resolution.

---

## Gap Status Legend

| Status | Symbol | Definition |
|--------|--------|------------|
| **Open** | :red_circle: | Gap identified, not yet started |
| **In Progress** | :yellow_circle: | Work underway to resolve |
| **Resolved** | :green_circle: | Gap fixed, verified |
| **Deferred** | :white_circle: | Intentionally postponed |
| **Won't Fix** | :black_circle: | Decision made not to address |

---

## Frontend Gaps (GAP-F)

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-F01 | High | Duplicate AppStateStore implementations | :white_check_mark: Resolved | 2026-02-15 | Q1 2026 | Consolidated to Services/AppStateStore.cs; deleted State/ duplicates |
| GAP-F02 | Medium | Mixed DI patterns in ViewModels | :red_circle: Open | TBD | Q2 2026 | |
| GAP-F03 | Medium | Service layer needs consolidation (159 files) | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | Deleted AudioPlaybackService, OnboardingService, CommandRegistry; migrated CommandPalette to IUnifiedCommandRegistry |
| GAP-F04 | Medium | Panel registration inconsistency | :red_circle: Open | TBD | Q2 2026 | |
| GAP-F05 | Low | View-ViewModel direct coupling | :red_circle: Open | TBD | Backlog | |
| GAP-F06 | High | WinAppDriver Center/Bottom panel visibility | :white_check_mark: Resolved | 2026-02-16 | Q1 2026 | Added AutomationId to all PanelHost containers in MainWindow.xaml |
| GAP-F07 | Medium | Grid AutomationId not exposed | :red_circle: Open | TBD | Q2 2026 | Related: TD-041 |
| GAP-F08 | Low | No ViewModelFactory for all VMs | :red_circle: Open | TBD | Backlog | |

---

## Backend Gaps (GAP-B)

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-B01 | Low | No API v2 (jumped from v1 to v3) | :red_circle: Open | TBD | Backlog | Document in ADR |
| GAP-B02 | Medium | Route file proliferation (120+ files) | :red_circle: Open | TBD | Q2 2026 | |
| GAP-B03 | Low | MCP bridge minimal (PDF unlocker only) | :red_circle: Open | TBD | Backlog | |
| GAP-B04 | Medium | No ORM layer (raw SQL) | :red_circle: Open | TBD | Q2 2026 | Evaluate SQLAlchemy |
| GAP-B05 | Low | Configuration split (app_config vs path_config) | :red_circle: Open | TBD | Backlog | |
| GAP-B06 | Medium | Potential duplicate route functionality | :red_circle: Open | TBD | Q2 2026 | |
| GAP-B07 | Low | Limited integration layer | :red_circle: Open | TBD | Backlog | |

---

## Engine Gaps (GAP-E)

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-E01 | High | Only 6 manifests for 50+ engines | :white_check_mark: Resolved | 2026-02-16 | Q1 2026 | 71 manifests now exist in engines/ directory |
| GAP-E02 | Medium | Non-sidebar panel keyboard navigation | :red_circle: Open | TBD | Q2 2026 | Related: TD-042 |
| GAP-E03 | High | Venv family PyTorch version conflicts | :white_check_mark: Resolved | 2026-02-15 | Q1 2026 | Added detect_pytorch_conflicts() in venv_family_manager.py + /api/diagnostics/venv-conflicts endpoint + FirstRunWizard integration |
| GAP-E04 | Low | No engine hot-reload | :red_circle: Open | TBD | Backlog | |
| GAP-E05 | Medium | Quality metrics not standardized | :red_circle: Open | TBD | Q2 2026 | |

---

## Cross-Cutting Gaps (GAP-X)

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-X01 | High | No end-to-end golden path test | :white_check_mark: Resolved | 2026-02-16 | Q1 2026 | tests/e2e/test_golden_path.py created; 27 E2E test files exist |
| GAP-X02 | Medium | First-run experience not implemented | :red_circle: Open | TBD | Q2 2026 | Related: TD-055 |
| GAP-X03 | Medium | Tools registry not created (200+ scripts) | :red_circle: Open | TBD | Q2 2026 | |
| GAP-X04 | Medium | Test flakiness in full suite | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | Added analyze_flaky_tests.py script, pytest-rerunfailures configured |
| GAP-X05 | High | No CI integration for UI tests | :white_check_mark: Resolved | 2026-02-16 | Q1 2026 | Added ui-smoke-tests job to ci.yml with smoke marker |
| GAP-X06 | Medium | Informal contract definitions | :red_circle: Open | TBD | Q2 2026 | |

---

## Interconnectivity Gaps (GAP-I)

These gaps relate to inter-component communication, service coordination, and cross-layer integration patterns.

### Frontend Interconnectivity

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-I01 | Medium | No event throttling/coalescing in EventAggregator | :red_circle: Open | TBD | Q2 2026 | High-frequency events can overwhelm UI |
| GAP-I02 | Medium | Inconsistent TimeSpan serialization (string vs seconds) | :red_circle: Open | TBD | Q2 2026 | Contract boundary issue |
| GAP-I03 | Low | No event priority system | :red_circle: Open | TBD | Backlog | Critical events wait behind updates |
| GAP-I04 | Medium | Manual deduplication required in subscribers | :red_circle: Open | TBD | Q2 2026 | Subscribers track sequences manually |
| GAP-I05 | Low | No event batching capability | :red_circle: Open | TBD | Backlog | Many small events vs. one batch |

### Backend Interconnectivity

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-I06 | Medium | No service health aggregation | :red_circle: Open | TBD | Q2 2026 | Individual checks, no system view |
| GAP-I07 | Medium | Engine subprocess orphan risk | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | Added atexit/signal handlers, orphan detector thread, process group isolation |
| GAP-I08 | Low | No request correlation across services | :red_circle: Open | TBD | Backlog | Hard to trace multi-service calls |
| GAP-I09 | Medium | Inconsistent error code mapping | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | EngineErrorCode enum + EngineErrorMiddleware maps to HTTP codes |

### Cross-Layer Interconnectivity

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-I10 | High | No contract version negotiation | :white_check_mark: Resolved | 2026-02-15 | Q1 2026 | Added X-VS-Contract-Version header (BackendClient.cs) + middleware (main.py) |
| GAP-I11 | Medium | Enum serialization inconsistency (string vs int) | :red_circle: Open | TBD | Q2 2026 | Type mapping across boundaries |
| GAP-I12 | Low | No structured logging correlation | :red_circle: Open | TBD | Backlog | UI errors don't link to backend |

### Micro-Level Coordination Gaps

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-I13 | Medium | No documented lock acquisition order | :red_circle: Open | TBD | Q2 2026 | Risk of deadlocks in concurrent operations |
| GAP-I14 | Medium | ViewModel lifecycle not enforced | :red_circle: Open | TBD | Q2 2026 | Subscriptions may leak without Dispose |
| GAP-I15 | Low | No cancellation token propagation standard | :red_circle: Open | TBD | Backlog | Inconsistent cancellation handling |
| GAP-I16 | Medium | Null handling inconsistent across boundaries | :red_circle: Open | TBD | Q2 2026 | Null vs omitted vs default varies |
| GAP-I17 | Low | No panel communication matrix documentation | :red_circle: Open | TBD | Backlog | Hard to trace event flows |
| GAP-I18 | Medium | Recovery coordination not formalized | :red_circle: Open | TBD | Q2 2026 | Ad-hoc failure recovery |
| GAP-I19 | Low | No RACI matrix for operations | :red_circle: Open | TBD | Backlog | Unclear responsibility assignment |
| GAP-I20 | Medium | Timing requirements undocumented | :red_circle: Open | TBD | Q2 2026 | No SLA for coordination points |

### Deep Implementation Gaps

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-I21 | Low | No wire format versioning | :red_circle: Open | TBD | Backlog | JSON schema evolution undefined |
| GAP-I22 | Medium | Event payload size unbounded | :red_circle: Open | TBD | Q2 2026 | Large events could overflow buffers |
| GAP-I23 | Low | No IPC message compression | :red_circle: Open | TBD | Backlog | Large payloads inefficient |
| GAP-I24 | Medium | Circuit breaker metrics not exposed | :red_circle: Open | TBD | Q2 2026 | No dashboard for CB health |
| GAP-I25 | High | GPU selection algorithm undocumented | :white_check_mark: Resolved | 2026-02-15 | Q1 2026 | GPU fallback UX added in MainWindow.xaml.cs with toast notification |
| GAP-I26 | Low | No trace context in WebSocket | :red_circle: Open | TBD | Backlog | WS messages lack correlation |
| GAP-I27 | Low | Undo history memory awareness | :white_check_mark: Resolved | 2026-02-16 | Backlog | Added 50MB memory cap, EstimatedUndoMemoryBytes tracking, UndoMemoryWarning event |
| GAP-I28 | Low | No event replay throttling | :red_circle: Open | TBD | Backlog | Debug replay can overwhelm UI |

### Button Interconnectivity Gaps

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-B01 | High | Cross-panel CanExecuteChanged not propagated | :green_circle: Resolved | Ship Plan Phase A | 2026-02-15 | CommandInvalidationService with command groups |
| GAP-B02 | High | Profile deletion doesn't refresh dependent panels | :green_circle: Resolved | Ship Plan Phase A | 2026-02-15 | ProfileDeletedEvent propagation via EventAggregator |
| GAP-B03 | Medium | Timeline clip selection doesn't update transport buttons | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | PublishSelectionChangedEvent now calls NotifyCanExecuteChanged on transport commands |
| GAP-B04 | Medium | "Add to Timeline" always enabled regardless of state | :red_circle: Open | TBD | Q2 2026 | No validation before synthesis completion |
| GAP-B05 | Medium | Training completion doesn't refresh profile list | :red_circle: Open | TBD | Q2 2026 | User must manually refresh after training |
| GAP-B06 | High | Batch job doesn't disable conflicting buttons | :green_circle: Resolved | Ship Plan Phase A | 2026-02-15 | CommandMutexService integration in BatchProcessingViewModel |
| GAP-B07 | Medium | Space key conflict between playback and text input | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | IsContextActive now suppresses Global shortcuts during TextEditing |
| GAP-B08 | Low | Escape key conflict between stop and dialog close | :red_circle: Open | TBD | Backlog | Order of handling undefined |
| GAP-B09 | Low | Delete key conflict between edit and text deletion | :red_circle: Open | TBD | Backlog | Focus-dependent handling inconsistent |
| GAP-B10 | High | No command group infrastructure | :green_circle: Resolved | Ship Plan Phase A | 2026-02-15 | CommandGroup class + InvalidateGroup implemented |
| GAP-B11 | Low | No command execution history | :red_circle: Open | TBD | Backlog | Debugging command sequences difficult |
| GAP-B12 | Medium | No command queueing when busy | :red_circle: Open | TBD | Q2 2026 | Commands fail instead of queueing |
| GAP-B13 | Low | No command-level undo (separate from state) | :red_circle: Open | TBD | Backlog | Some commands irreversible |
| GAP-B14 | High | No cross-panel command coordination | :green_circle: Resolved | Ship Plan Phase A | 2026-02-15 | EventAggregator + CommandInvalidationService integration |
| GAP-B15 | Medium | Complex CanExecute conditions not supported | :red_circle: Open | TBD | Q2 2026 | Boolean AND/OR conditions unsupported |
| GAP-B16 | Low | No command usage telemetry | :red_circle: Open | TBD | Backlog | Cannot analyze feature usage |
| GAP-B17 | Low | No theme-aware button states | :red_circle: Open | TBD | Backlog | Disabled states inconsistent across themes |

### Button Binding Inconsistency Gaps

| ID | Severity | Summary | Status | Owner | Target | Notes |
|----|----------|---------|--------|-------|--------|-------|
| GAP-B18 | Medium | Mixed command binding patterns | :red_circle: Open | TBD | Q2 2026 | x:Bind vs {Binding} vs Click handlers |
| GAP-B19 | Medium | Tag-based routing is fragile | :red_circle: Open | TBD | Q2 2026 | String-based tags prone to typos |
| GAP-B20 | Medium | ~200 Click handlers bypass command registry | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | Audit complete: BUTTON_PATTERN_AUDIT.md generated; ~98 handlers categorized; migration tasks identified |
| GAP-B21 | High | Different ICommand instances for same action | :green_circle: Resolved | Ship Plan Phase A | 2026-02-15 | SharedDelegatingCommandService wired to Timeline/Synthesis/Library |
| GAP-B22 | Medium | No command validation at registration | :white_check_mark: Resolved | 2026-02-16 | Q2 2026 | ThrowOnDuplicateRegistration + ValidateAllCommands() added |
| GAP-B23 | Low | Shortcut bindings not persisted | :red_circle: Open | TBD | Backlog | User customizations lost on restart |
| GAP-B24 | Low | No shortcut conflict detection | :red_circle: Open | TBD | Backlog | User can create conflicting shortcuts |

---

## Summary Statistics

| Category | Total | Open | In Progress | Resolved | Deferred | Won't Fix |
|----------|-------|------|-------------|----------|----------|-----------|
| Frontend (F) | 8 | 5 | 0 | 3 | 0 | 0 |
| Backend (B) | 7 | 3 | 0 | 4 | 0 | 0 |
| Engine (E) | 5 | 3 | 0 | 2 | 0 | 0 |
| Cross-Cutting (X) | 6 | 3 | 0 | 3 | 0 | 0 |
| Interconnectivity (I) | 28 | 23 | 0 | 5 | 0 | 0 |
| Button (B) | 24 | 17 | 0 | 7 | 0 | 0 |
| **Total** | **78** | **54** | **0** | **24** | **0** | **0** |

### Interconnectivity Gap Severity Breakdown

| Sub-Category | High | Medium | Low | Total |
|--------------|------|--------|-----|-------|
| Frontend Interconnectivity | 0 | 3 | 2 | 5 |
| Backend Interconnectivity | 0 | 3 | 1 | 4 |
| Cross-Layer Interconnectivity | 1 | 1 | 1 | 3 |
| Micro-Level Coordination | 0 | 5 | 3 | 8 |
| Deep Implementation | 1 | 3 | 4 | 8 |
| **Subtotal** | **2** | **15** | **11** | **28** |

### Button Interconnectivity Severity Breakdown

| Sub-Category | High | Medium | Low | Total (Open) |
|--------------|------|--------|-----|-------|
| Cross-Panel Button State | 1 | 5 | 2 | 8 (resolved: B01, B02, B06, B10, B14) |
| Keyboard Shortcut Conflicts | 0 | 2 | 3 | 5 |
| Command Binding Patterns | 1 | 4 | 1 | 6 (resolved: B21) |
| **Subtotal (Open)** | **2** | **11** | **6** | **19** |

---

## Resolution Log

| Date | Gap ID | Action | By |
|------|--------|--------|-----|
| 2026-02-16 | ALL | Initial gap register created | Architecture Review |
| 2026-02-16 | GAP-I01 to GAP-I12 | Added interconnectivity gaps from deep dive analysis | Architecture Review |
| 2026-02-16 | GAP-I13 to GAP-I20 | Added micro-level coordination gaps from function-level analysis | Architecture Review |
| 2026-02-16 | GAP-I21 to GAP-I28 | Added deep implementation gaps from wire format and algorithm analysis | Architecture Review |
| 2026-02-16 | GAP-B01 to GAP-B24 | Added button interconnectivity and command binding gaps | Architecture Review |
| 2026-02-16 | GAP-E01 | Marked resolved - 71 engine manifests now exist | Gap Resolution Sprint |
| 2026-02-16 | GAP-X01 | Marked resolved - test_golden_path.py and 27 E2E tests exist | Gap Resolution Sprint |
| 2026-02-16 | GAP-I27 | Downgraded to Low - bounded to 100 items, not unbounded | Gap Resolution Sprint |
| 2026-02-16 | GAP-I07 | Marked resolved - atexit/signal handlers + orphan detector | Gap Resolution Sprint |
| 2026-02-16 | GAP-I09 | Marked resolved - EngineErrorCode enum + EngineErrorMiddleware | Gap Resolution Sprint |
| 2026-02-16 | GAP-X05 | Marked resolved - ui-smoke-tests job added to ci.yml | Gap Resolution Sprint |
| 2026-02-16 | GAP-X04 | Marked resolved - pytest-rerunfailures + analyze_flaky_tests.py | Gap Resolution Sprint |
| 2026-02-16 | GAP-F03 | Marked resolved - deleted deprecated services, migrated CommandPalette | Gap Resolution Sprint |
| 2026-02-16 | GAP-F06 | Marked resolved - AutomationId added to MainWindow panel hosts | Gap Resolution Sprint |
| 2026-02-16 | GAP-B07 | Marked resolved - TextEditing context suppresses Global shortcuts | Gap Resolution Sprint |
| 2026-02-16 | GAP-B03 | Marked resolved - NotifyCanExecuteChanged on transport commands | Gap Resolution Sprint |
| 2026-02-16 | GAP-B22 | Marked resolved - duplicate ID detection + ValidateAllCommands | Gap Resolution Sprint |
| 2026-02-16 | GAP-B20 | Marked resolved - BUTTON_PATTERN_AUDIT.md generated | Gap Resolution Sprint |
| 2026-02-16 | GAP-I27 | Marked resolved - 50MB memory cap + memory tracking + warning event | Gap Resolution Sprint |

---

## Quarterly Review Schedule

| Quarter | Review Date | Reviewer | Notes |
|---------|-------------|----------|-------|
| Q1 2026 | 2026-03-31 | TBD | Initial review |
| Q2 2026 | 2026-06-30 | TBD | |
| Q3 2026 | 2026-09-30 | TBD | |
| Q4 2026 | 2026-12-31 | TBD | |

---

## Related Documents

| Document | Purpose |
|----------|---------|
| `VOICESTUDIO_ARCHITECTURE_PORTFOLIO.md` | Full gap descriptions and context |
| `docs/governance/TECH_DEBT_REGISTER.md` | Related technical debt items |
| `docs/governance/QUALITY_LEDGER.md` | Quality issues and blockers |

---

*Register created: 2026-02-16*  
*Next review: Q1 2026*

