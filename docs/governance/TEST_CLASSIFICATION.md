# Test Classification for Seam Migration Claims

**Date:** 2026-03-12 (proof grades 2026-04-08)  
**Purpose:** Classify tests by whether they validate seam migrations. Architectural completion claims require seam-aware tests or stronger runtime proof.  
**Related:** [SEAM_MATURITY_AUDIT.md](../design/SEAM_MATURITY_AUDIT.md), [closure-protocol.mdc](../../.cursor/rules/workflows/closure-protocol.mdc), [EXECUTION_ROW_DISCIPLINE.md](EXECUTION_ROW_DISCIPLINE.md)

---

## Proof grades (GAP-015) — seam vs integration vs runtime

These grades classify **what class of truth** a test or harness proves. They complement the seam-migration table below (which classifies **ViewModel migration** claims).

| Grade | Name | Definition | Examples |
|-------|------|------------|----------|
| **S** | Seam | Unit / isolated tests; mocks; no live process; no network. | MSTest ViewModel tests with mocked `ITrainingClient`; Python unit tests. |
| **I** | Integration | In-process ASGI or `TestClient`; real app wiring; no separate uvicorn/desktop process. | `tests/ci/test_golden_loop_smoke.py` (stub mode); `tests/ci/test_runtime_proof_training_export.py`. |
| **R** | Runtime | Live backend on a port and/or desktop subprocess; health and feature paths through real I/O. | `verify.ps1` UI stages (icon-launch, failure smokes); `tests/ci/test_golden_loop_smoke_real.py` (real engine + consent); optional `PROOF_GOLDEN_PATH_REAL_*.json`. |

**Operational doctrine:** Green **Grade S** alone does not prove the product path works against a live backend. **Grade I** proves in-process authority. **Grade R** proves operability consistent with production startup and/or engine-backed paths.

### Execution-row proof requirement matrix

Used with [EXECUTION_ROW_DISCIPLINE.md](EXECUTION_ROW_DISCIPLINE.md) **Runtime proof requirement** section.

| Lane type | Grade S | Grade I | Grade R |
|-----------|---------|---------|---------|
| **runtime-affecting** (product code: synthesis, training, startup, export, health) | Required | Required (at least stub golden loop in default CI where applicable) | **Fresh** Grade R when the lane changes those paths; otherwise **inherited** Grade R within policy window (see execution row) |
| **runtime-affecting** (governance / CI-only) | If tests change | As needed | Optional per row |
| **proof-hardening** | Only if test/docs change | No | No |

---

## Classification Definitions

| Classification | Definition | Supports "Migration Complete" Claim? |
|----------------|------------|--------------------------------------|
| **Seam-aware** | Instantiates target ViewModel with real or mocked migrated seam interfaces (ITrainingClient, IProfilesClient, IProjectsClient, etc.). Exercises the migrated path. | Yes |
| **Transport-mock** | Mocks IBackendClient/HTTP; tests contract or transport behavior. Does not instantiate the migrated ViewModel. Useful for contract checks. | No |
| **Legacy** | Bypasses seam; tests old transport patterns or DTOs only. Valuable for regression but cannot support migration claims. | No |

---

## Classified Tests (2026-03-12)

### Seam-aware

| Test File | Target | Seams Used | Notes |
|-----------|--------|------------|-------|
| TimelineViewModelTests.cs | TimelineViewModel | IProfilesClient, IProjectsClient, ITimelineClipService, ITimelineTrackService, etc. | Instantiates ViewModel with mocked seam interfaces. |
| TrainingViewModelSeamTests.cs | TrainingViewModel | ITrainingClient | Instantiates ViewModel with mocked ITrainingClient; verifies InitializeAsync calls ListDatasetsAsync, ListTrainingJobsAsync. |
| TranscribeViewModelSeamTests.cs | TranscribeViewModel | ITranscriptionClient | Instantiates ViewModel with mocked ITranscriptionClient; verifies InitializeAsync calls GetTranscriptionEnginesAsync, GetSupportedLanguagesAsync. |
| ProfileComparisonViewModelSeamTests.cs | ProfileComparisonViewModel | IVoiceSynthesisService, IProfilesClient | Instantiates ViewModel with mocked seam clients; verifies InitializeAsync calls GetProfilesAsync. |
| ImageSearchViewModelSeamTests.cs | ImageSearchViewModel | IImageSearchClient | Instantiates ViewModel with mocked IImageSearchClient; verifies constructor, null checks, IPanelLifecycle, OnActivatedAsync. |
| RequestCoordinatorIntegrationTests (TimelinePanelScenario_*) | Timeline flow | IProjectsClient, IProfilesClient, ITimelineTrackService, ITimelineClipService | Scenario tests; bounded request counts. |

### Transport-mock / Legacy

| Test File | Target | Classification | Notes |
|-----------|--------|----------------|-------|
| TrainingViewModelTests.cs | IBackendClient (not ViewModel) | Transport-mock | Tests BackendClient.StartTrainingAsync etc. directly. Does not instantiate TrainingViewModel. |
| TranscribeViewModelTests.cs | IBackendClient (not ViewModel) | Transport-mock | Tests BackendClient.TranscribeAudioAsync etc. directly. Does not instantiate TranscribeViewModel. |

### Model / DTO (not ViewModel)

| Test File | Target | Notes |
|-----------|--------|-------|
| ProfileComparisonModelTests.cs | ProfileComparisonData | Tests model/DTO; not ProfileComparisonViewModel. |

### Smoke / Integration

| Test File | Notes |
|-----------|-------|
| CriticalPathSmokeTests.cs | Creates TimelineViewModel with real services; smoke/integration. |
| PanelNavigationSmokeTests.cs | Navigation smoke; ProfileComparison panel. |

---

## Rule: Architectural Completion Claims

**Closure protocol addition:** Architectural completion claims (e.g., "TrainingViewModel migrated to ITrainingClient") require seam-aware tests or stronger runtime proof. Tests that mock IBackendClient and never instantiate the ViewModel cannot support such claims.

---

## Gaps

- **TrainingViewModel:** Seam-aware tests added (TrainingViewModelSeamTests.cs). TrainingViewModelTests remains transport-mock.
- **TranscribeViewModel:** Seam-aware tests added (TranscribeViewModelSeamTests.cs). TranscribeViewModelTests remains transport-mock.
- **ProfileComparisonViewModel:** Seam-aware tests added (ProfileComparisonViewModelSeamTests.cs). ProfileComparisonModelTests remains model-only.

---

## Recommended Metadata

For transport-mock or legacy tests that might be mistaken for seam proof, add `[TestCategory("TransportMock")]` or `[TestCategory("Legacy")]` so completion claims cannot accidentally cite them.
