# VOICESTUDIO_SELECTION_AUTHORITY_LANE_CLOSURE_2026-03-28

**Lane:** `GOV-VOICESTUDIO-SELECTION-AUTHORITY-01`  
**Execution row:** `docs/design/GOV_VOICESTUDIO_SELECTION_AUTHORITY_01_EXECUTION_ROW.md`

## 1 Scope delivered

- Canonical **`ProfileSelectedEvent`** for all profile selection publishes previously using `VoiceProfileSelectedEvent` (Library fallback, workflow coordinator). Features **`SynthesisViewModel`** subscribes to `ProfileSelectedEvent`.
- **`VoiceProfileSelectedEvent`** marked `[Obsolete]`; no production publishers remain.
- **`IContextManager`**: `ActiveTimelinePrimaryClipId`, `ActiveTimelinePrimaryTrackId`, `SetActiveTimelineSelection`; **`TimelineViewModel`** syncs selection into context on clip and track changes.
- **GAP-011** closed in `PROFESSIONAL_GAP_TRACKER.md` with reference to this lane (read-model + event authority; optional facade deferred).

## 2 Proof commands (run locally)

| Step | Command | Expected |
|------|---------|----------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | 0 errors |
| C# tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | Pass (2793 passed / 274 skipped) |
| CI pytest | `python -m pytest tests/ci/ -q` | Pass |
| Quick verify | `.\scripts\verify.ps1 -Quick` | Exit 0 |
| Validator | `python scripts/run_verification.py` | PASS, **completion_guard** PASS after commit |

## 3 Tests added or updated

- `SelectionAuthorityTests.ProfileSelectedEvent_UpdatesFeaturesSynthesisViewModel_SelectedVoice`
- `ContextManagerTests` — initial nulls for timeline fields; `SetActiveTimelineSelection_UpdatesClipAndTrackIds`
- `WorkflowCoordinatorServiceTests.StartSynthesizeWithVoiceAsync_PublishesProfileSelectedEvent`

## 4 Honest limits

- **Transport bar** — **GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01** closed 2026-03-28 (GAP-009); see [VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md](VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md). *(This selection-closure doc §4 was written before transport lane close; updated for registry truth.)*
- **Persistence** for timeline state remains **GAP-017** / persistence lane.
- Optional **`ISelectionService`** thin facade was not introduced; `IContextManager` + `ProfileSelectedEvent` satisfy the lane’s authority goal.
