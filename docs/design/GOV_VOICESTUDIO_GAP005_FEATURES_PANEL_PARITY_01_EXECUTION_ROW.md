# GOV-VOICESTUDIO-GAP005-FEATURES-PANEL-PARITY-01

**Status:** Closed
**GAP:** GAP-005 — Orphan / duplicate `Features/*` ViewModels vs canonical panels
**Phase:** 0 (Broken)
**Role:** UI Engineer
**Created:** 2026-04-08

---

## Problem Statement

`src/VoiceStudio.App/Features/` contains 16 files (5 ViewModels, 11 services) that compile but are never used by the running application. All have canonical counterparts in `Views/Panels/`, `ViewModels/`, `Views/Shell/`, or `Services/`. The `Features/` types use the Gateway pattern (`IProfileGateway`, `IVoiceGateway`, `IEngineGateway`, `ITimelineGateway`) instead of the production client pipeline (`IBackendClient`, `IProfilesClient`, `IEnginesClient`), creating a namespace collision hazard and false sense of coverage.

### Evidence of isolation

- **XAML**: 0 files reference `VoiceStudio.App.Features`
- **DI / AppServices.cs**: 0 registrations from `Features/`
- **Panel registry**: 0 panels use `Features/` ViewModels (all 73 panels resolve from `Views/Panels/` or `ViewModels/`)
- **Production C#**: Only internal cross-references within `Features/` itself
- **Tests**: 1 file — `SelectionAuthorityTests.cs` — imports `Features.Synthesis` and `Features.VoiceProfile` (covered by canonical `WorkflowCoherenceAdvancedTests.ProfileSelectedEvent_UpdatesVoiceSynthesisSelectedProfile_WhenProfileInList`)
- **JSON / state persistence**: 0 references
- **Navigation / commands**: 0 references

## Bounded Slice

Remove the **entire `Features/` folder** (16 files) and its sole external test consumer.

### Allowlist

| Action | Target |
|--------|--------|
| Delete | All 16 files under `src/VoiceStudio.App/Features/` |
| Delete | `src/VoiceStudio.App.Tests/ViewModels/SelectionAuthorityTests.cs` (orphaned test — canonical coverage exists in `WorkflowCoherenceAdvancedTests`) |
| Edit | Governance files (this row, tracker, registry, STATE, closure report) |

### Hard OUT

- No changes to `Views/Panels/`, `ViewModels/`, `Services/`, or `Views/Shell/`
- No changes to panel registration services
- No changes to backend, engine, or Python code
- No synthesis, emotion, or prosody route changes (GAP-004/050/023 closed)
- No new ViewModels or abstractions introduced

## Acceptance Contract

- [x] `src/VoiceStudio.App/Features/` directory deleted
- [x] `SelectionAuthorityTests.cs` deleted (canonical equivalent exists)
- [x] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` passes (0 errors, 650 warnings)
- [x] `dotnet build VoiceStudio.sln -c Release -p:Platform=x64` passes (0 errors, 647 warnings)
- [x] Full App.Tests pass (3194 passed, 0 failed, 274 skipped)
- [x] `pytest tests/ci` passes (217 passed)
- [x] `verify.ps1 -Quick` GREEN — `artifacts/verify/20260408_065831/`
- [x] `run_verification.py` completion_guard PASS — `20260408-070338`
- [x] No new skip/ignore introduced

## Rollback

`git revert` the GAP-005 commit restores all 16 files and the test. No other production files are affected.

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SelectionAuthorityTests tests unique behavior not covered elsewhere | Low | Medium | Verified: `WorkflowCoherenceAdvancedTests.ProfileSelectedEvent_UpdatesVoiceSynthesisSelectedProfile_WhenProfileInList` covers the same canonical `ProfileSelectedEvent` → synthesis VM path |
| Features/ service loaded via reflection or MEF | Very Low | High | Grep shows 0 `typeof` references outside `Features/` and 0 DI registrations; reflection would require string references not found |
| Build break from removing co-located helper types | Low | Low | Grep shows 0 production references to `VoiceProfileData`, `SynthesisParameters`, `EngineInfo` (Features namespace), or other Features-local types |

## Changelog

| Date | Entry |
|------|-------|
| 2026-04-08 | Row created; inventory complete; bounded slice frozen |
| 2026-04-08 | **Closed** — 16 files + 1 test deleted; Debug/Release build 0 errors; App.Tests 3194/0/274; pytest CI 217; Quick `20260408_065831`; rolling `20260408-070338` (completion_guard PASS); [closure report](../reports/verification/VOICESTUDIO_GAP005_FEATURES_PANEL_PARITY_LANE_CLOSURE_2026-04-08.md) |
