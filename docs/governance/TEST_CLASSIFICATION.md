# Test Classification for Seam Migration Claims

**Date:** 2026-03-12  
**Purpose:** Classify tests by whether they validate seam migrations. Architectural completion claims require seam-aware tests or stronger runtime proof.  
**Related:** [SEAM_MATURITY_AUDIT.md](../design/SEAM_MATURITY_AUDIT.md), [closure-protocol.mdc](../../.cursor/rules/workflows/closure-protocol.mdc)

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
