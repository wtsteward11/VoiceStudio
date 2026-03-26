# Full-Scope Architecture Next Wave

**Status:** **Active** — execution started 2026-03-20 after release-trust GREEN  
**Date:** 2026-03-18 (plan); **Unblocked:** 2026-03-20  
**Prerequisite (met):** Two consecutive full `.\scripts\verify.ps1` green runs with inspectable artifacts — see `docs/reports/release_trust_closure_20260320.md` (`artifacts/verify/20260320_061427/`, `20260320_063157/`).

---

## Assumptions

- All planned panels remain
- All planned routes remain
- No startup/MVP scope cuts
- Goal: professional maintainability for a large, growing codebase

---

## Ranked Next Architecture Work

### 1. BackendClient strangling / shared transport extraction

**Problem:** BackendClient (and generated extensions) is a god-file; HTTP policy, retries, serialization, and error mapping are entangled with feature logic.

**Target:**
- Reduce god-file growth
- Move HTTP policy, retries, serialization, error mapping into a shared transport layer
- Keep features; reduce entropy
- Clients consume transport; transport owns connection lifecycle

**References:** `src/VoiceStudio.App/Services/BackendClient*.cs`, `BackendClientHttpPipeline.cs`, `BackendClient.g.cs`, `Gateways/BackendTransport.cs`  
**Inventory / PR-1 proof:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)

---

### 2. MainWindow decomposition continuation

**Problem:** MainWindow.xaml.cs remains ~2400+ lines; shell/business orchestration still embedded.

**Target:**
- Continue removing shell/business orchestration from MainWindow
- No fake coordinators that just move the blob sideways
- **Next seam (scoped 2026-03-20):** **Open project workflow** only — save/search deferred (per MAINWINDOW_DECOMPOSITION_PLAN.md)

**References:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

---

### 3. AppServices / App.xaml.cs composition cleanup

**Problem:** Registration sprawl; DI composition root is hard to reason about.

**Target:**
- Registration modularization (by domain or feature)
- Less DI sprawl
- Safer composition root
- Clear ownership of service lifetimes

**References:** `src/VoiceStudio.App/Services/AppServices.cs`, `App.xaml.cs`

---

### 4. Feature-preserving panel contract enforcement

**Problem:** Panel lifecycle, navigation, backend seam, and event ownership are implicit.

**Target:**
- Registry descriptor (capabilities, dependencies)
- Lifecycle ownership (who creates, who disposes)
- Navigation ownership (who opens panels)
- Backend seam ownership (which client per panel)
- Event ownership (who subscribes, who unsubscribes)
- Test bucket (seam vs integration vs workflow proof)

**References:** `IPanelView`, `PanelRegistry`, seam migration patterns

---

### 5. Backend bounded-context organization

**Problem:** Flat service sprawl in `backend/services/`; forwarding theater and stub services.

**Target:**
- Reduce flat service sprawl
- Organize by domain (synthesis, training, analysis, project, etc.)
- Kill forwarding theater and stub services where appropriate
- Align with ADR-022 bounded contexts

**References:** `backend/domain/`, `backend/services/`, ADR-022

---

### 6. Test-honesty improvements

**Problem:** Seam tests vs real integration/workflow proof are conflated; "migration complete" claims without proof.

**Target:**
- Distinguish seam tests from real integration/workflow proof
- Require one real proof path per major domain
- Per TEST_CLASSIFICATION.md: categorize honestly

**References:** [TEST_CLASSIFICATION.md](../governance/TEST_CLASSIFICATION.md)

---

## Execution Order

Execute in rank order (1 → 6). **PR-1 (2026-03-20):** `BackendClientHttpPipeline` + inventory doc + transport tests; MainWindow **open project** seam scoped in `MAINWINDOW_DECOMPOSITION_PLAN.md`; AppServices comments-only pass.

---

## Architecture-wave honesty (do not confuse with “migration done”)

| Milestone | Status (2026-03-20) |
|-----------|---------------------|
| Kickoff (release-trust + traceability) | Done |
| Extraction inventory published | Done — `BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md` |
| PR-1 scoped (in/out frozen) | Done — same doc |
| PR-1 landed (policy in `BackendClientHttpPipeline`) | Done |
| Transport policy tests (`BackendClientTransportPolicyTests`) | Done |
| MainWindow seam identified | Done — **Open project workflow** (`MAINWINDOW_DECOMPOSITION_PLAN.md`) |
| **MainWindow open-project extracted** | Done — `ProjectWorkflowBootstrap`; MainWindow delegates only; `ProjectWorkflowCoordinatorTests` (5) |
| **MainWindow save-project extracted** | Done — `ProjectWorkflowCoordinator.SaveProjectAsync`; MainWindow delegates only; SaveProjectAsync tests (4) |
| AppServices readability pass (comments only) | Done |
| **PR-2 error parsing dedupe** (shared `StandardErrorResponseParser` with `BackendTransport`) | Done — `StandardErrorResponseParser.cs`; `BackendClientHttpPipeline` + `BackendTransport` use it |
| **PR-3 plugin health extraction** | Done — `PluginHealthClient` owns HTTP; `BackendHttpContext` shared; 3 methods removed from BackendClient |

## Changelog

| Date       | Change |
|------------|--------|
| 2026-03-21 | PR-3 plugin health extraction: `PluginHealthClient` owns HTTP via `BackendClientHttpPipeline`; `BackendHttpContext` shared; 3 methods removed from BackendClient/IBackendClient |
| 2026-03-21 | MainWindow save-project extracted: `ProjectWorkflowCoordinator.SaveProjectAsync`; MainWindow delegates only; SaveProjectAsync tests (4) |
| 2026-03-20 | PR-2 error parsing dedupe: `StandardErrorResponseParser` shared by `BackendClientHttpPipeline` and `BackendTransport`; transport tests expanded (8) |
| 2026-03-20 | MainWindow open-project extracted: `ProjectWorkflowBootstrap`; `ProjectWorkflowCoordinatorTests` (5); MAINWINDOW-OPEN-PROJECT-SEAM-LANDED |
| 2026-03-20 | PR-1: `BackendClientHttpPipeline`, static health probe fix, upload timeout via linked CTS; transport tests; inventory + honesty table |
| 2026-03-20 | Prerequisite satisfied; status → Active; kickoff notes; link release_trust_closure proof |
| 2026-03-18 | Initial plan; ranked 6 architecture work items |
