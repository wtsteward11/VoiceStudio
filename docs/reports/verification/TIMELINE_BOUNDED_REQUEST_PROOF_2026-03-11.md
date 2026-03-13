# Timeline Panel Bounded Request Proof

**Date:** 2026-03-11  
**Purpose:** Document proof that Timeline panel (Views/Panels/TimelineViewModel) keeps request counts bounded for activation, refresh, and mutation flows.  
**Related:** [PROFILES_REQUEST_STORM_PROOF_2026-03-10](./PROFILES_REQUEST_STORM_PROOF_2026-03-10.md), [REQUEST_COORDINATION_AUDIT_2026-03-11](./REQUEST_COORDINATION_AUDIT_2026-03-11.md)

---

## Context

The Timeline panel loads projects (via IProjectsClient), profiles (via IProfilesClient), and tracks (via ITimelineTrackService) when the user refreshes or selects a project. Clip CRUD goes through ITimelineClipService. Without coordination, concurrent or redundant calls could cause request storms.

---

## Panel Activation

| Event | Backend Calls | Coordinated? |
|-------|---------------|--------------|
| OnActivatedAsync | None | N/A |
| RefreshAsync | LoadProjectsAsync → IProjectsClient.GetProjectsAsync | Yes (single-flight + TTL) |
| LoadProfilesCommand | LoadProfilesAsync → _profilesClient.GetProfilesAsync | Yes (RequestCoordinator) |

**Note:** Timeline uses IProfilesClient (not IBackendClient) for profile loading; RequestCoordinator applies. OnActivatedAsync returns `Task.CompletedTask`; no automatic load on activation. Projects and profiles load only when the user triggers Refresh or LoadProfiles.

---

## Idle

No polling or background refresh. Idle state = no backend calls.

---

## Mutation + Reload

| Action | Backend Call | Count |
|--------|--------------|-------|
| Select project | GetTracksAsync(projectId) | 1 per selection |
| Create track | CreateTrackAsync | 1 per action |
| Delete clip | DeleteClipAsync (via ITimelineClipService) | 1 per action |
| Add clip | CreateClipAsync (via ITimelineClipService) | 1 per action |

Project-specific endpoints (`/api/projects/{id}/tracks`, etc.) are not coalesced (one request per user action is expected).

---

## Expected Bounded Counts

| Scenario | Endpoint | Expected |
|----------|----------|----------|
| Refresh (LoadProjectsAsync) | /api/projects | ≤ 2 |
| LoadProfiles (concurrent 3×) | /api/profiles | ≤ 2 |
| Refresh + LoadProfiles | /api/projects, /api/profiles | Each ≤ 2 |

---

## Verification Result

- [x] Document created
- [ ] Manual run (optional): Open Timeline → Refresh → Load Profiles → verify snapshot
- [x] Scenario test: `TimelinePanelScenario_RefreshLoadProfiles_BoundedRequestCounts` (RequestCoordinatorIntegrationTests)
- [x] Scenario test: `TimelinePanelScenario_LoadProjectsSelectProjectLoadTracksCreateClip_BoundedRequestCounts` — open Timeline, load projects, select project, load tracks, create clip; asserts bounded counts for /api/projects and tracks
- [x] Scenario test: `TimelinePanelScenario_LoadProjectsSelectProjectLoadTracksDeleteClip_BoundedRequestCounts` — load projects, select project, load tracks, delete clip; asserts bounded counts and exactly 1 delete request (2026-03-11)

---

## Automation

**CI-capable tests:**
- `TimelinePanelScenario_RefreshLoadProfiles_BoundedRequestCounts` — simulates Timeline refresh flow: GetProjectsAsync + GetProfilesAsync. Asserts /api/projects ≤ 2, /api/profiles ≤ 2.
- `TimelinePanelScenario_LoadProjectsSelectProjectLoadTracksCreateClip_BoundedRequestCounts` — simulates full flow: load projects, select project, load tracks, create clip. Asserts bounded counts for projects and tracks.
- `TimelinePanelScenario_LoadProjectsSelectProjectLoadTracksDeleteClip_BoundedRequestCounts` — simulates flow with clip delete. Asserts bounded counts and exactly 1 DeleteClipAsync request.

```powershell
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TimelinePanelScenario"
```

---

**Last verified against code:** 2026-03-12
