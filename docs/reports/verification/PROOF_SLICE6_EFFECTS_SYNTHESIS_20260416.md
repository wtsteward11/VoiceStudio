# Slice 6 — Second Hero Workflow Recovery (Effects + Synthesis Stub)

**Date:** 2026-04-16 (runtime proof: 2026-04-17)
**Status:** PASS — Runtime Proven
**Workflows Proven:** Effect Chain CRUD, Synthesis Stub

---

## Backend Truth (Live Session)

Backend started with `VOICESTUDIO_TEST_MODE=stub` via `.venv\Scripts\python.exe` (Python 3.11.9).

```
GET /api/health → 200
{
  "status": "ok",
  "version": "1.1.0",
  "version_string": "v1.1.0 (653612c8)",
  "python_executable": "E:\\VoiceStudio\\.venv\\Scripts\\python.exe",
  "python_version": "3.11.9",
  "engines_ready": true
}
```

---

## Python Route Tests (TestClient/ASGITransport)

### test_effects_crud.py — 8 tests, 8 PASSED

| Test | Status | Proves |
|------|--------|--------|
| test_list_empty | PASS | GET /api/effects/chains?project_id=… returns [] for fresh project |
| test_create_chain | PASS | POST /{project_id} creates chain with server-assigned id |
| test_get_chain | PASS | GET /{project_id}/{chain_id} returns the created chain |
| test_update_chain | PASS | PUT /{project_id}/{chain_id} updates name/description |
| test_delete_chain | PASS | DELETE /{project_id}/{chain_id} removes chain, GET returns 404 |
| test_full_crud_lifecycle | PASS | Full create → get → update → delete lifecycle |
| test_create_empty_name_returns_400 | PASS | Empty name POST returns 400 |
| test_get_nonexistent_chain_returns_404 | PASS | Nonexistent chain_id GET returns 404 |

### test_synthesis_stub.py — 4 tests, 4 PASSED

| Test | Status | Proves |
|------|--------|--------|
| test_health_check | PASS | /api/health returns 200 with healthy status |
| test_synthesize_stub_returns_audio | PASS | profile create → synthesize → fetch audio → RIFF WAV header |
| test_synthesize_missing_profile_returns_error | PASS | Nonexistent profile returns 4xx |
| test_synthesize_empty_text_returns_422 | PASS | Empty text returns 422 validation error |

---

## C# Live-Backend Tests (Runtime Proven)

### EffectChainClientLiveBackendTests — 2 tests, 2 PASSED

| Test | Runtime | Duration | Proves |
|------|---------|----------|--------|
| EffectChainCrud_LiveBackend_CreateGetUpdateDelete | **PASS** | 306 ms | Full CRUD through EffectChainClient → BackendClientHttpPipeline → live FastAPI |
| GetEffectChains_LiveBackend_ReturnsListForProject | **PASS** | 291 ms | List operation returns non-null list via query-param endpoint |

**Command:** `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~EffectChainClientLiveBackendTests"`
**Result:** `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 597 ms`

### SynthesisStubLiveBackendTests — 1 test, 1 PASSED

| Test | Runtime | Duration | Proves |
|------|---------|----------|--------|
| Synthesize_LiveBackend_ReturnsAudioId_FetchableAsWav | **PASS** | 338 ms | profile create → synthesize → fetch WAV through real HTTP, RIFF header verified |

**Command:** `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SynthesisStubLiveBackendTests"`
**Result:** `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 338 ms`

---

## Seam Defect Found and Fixed

### Route Conflict: `EffectChainClient.GetEffectChainsAsync`

**Discovery:** First runtime execution of `GetEffectChains_LiveBackend_ReturnsListForProject` failed with `BackendValidationException: Request validation failed` (422).

**Root Cause:** `EffectChainClient.GetEffectChainsAsync` used path-style `GET /api/effects/chains/{projectId}`. In `route_registry.py`, the main effects router registers `GET /api/effects/chains/{chain_id}` (single chain by ID, expects query `project_id`) **before** the project effects router registers `GET /api/effects/chains/{project_id}` (list by project). FastAPI matched the main router first, interpreting the project ID as a chain ID and returning 422 for missing `project_id` query parameter.

**Fix:** Changed `EffectChainClient.GetEffectChainsAsync` (line 30) from:
```csharp
$"/api/effects/chains/{Uri.EscapeDataString(projectId)}"
```
to:
```csharp
$"/api/effects/chains?project_id={Uri.EscapeDataString(projectId)}"
```

**File:** `src/VoiceStudio.App/Services/EffectChainClient.cs`

**Verification:** After fix, both tests pass. The query-param endpoint (`GET /api/effects/chains?project_id=...`) is served by the main router's `list_effect_chains` handler, which returns `list[EffectChain]` — same response shape, no deserialization change.

---

## Regression Summary

| Suite | Result |
|-------|--------|
| Slice 3-5 Python tests (search, profiles, sts, transcribe, golden loop) | 63 passed |
| Slice 6 Python tests (effects CRUD, synthesis stub) | 12 passed |
| C# live-backend: EffectChainClientLiveBackendTests | 2 passed |
| C# live-backend: SynthesisStubLiveBackendTests | 1 passed |
| C# live-backend: ProfilesRuntimeLiveBackendTests | 1 passed (regression) |
| C# live-backend: LibraryRuntimeLiveBackendTests | 1 passed (regression) |
| C# solution build (Debug x64) | 0 errors |

---

## Artifacts

| File | Purpose |
|------|---------|
| tests/unit/backend/api/routes/test_effects_crud.py | Effect chain CRUD route truth |
| tests/unit/backend/api/routes/test_synthesis_stub.py | Synthesis stub route truth |
| src/VoiceStudio.App.Tests/ViewModels/EffectChainClientLiveBackendTests.cs | C# effect chain client live-backend truth (runtime proven) |
| src/VoiceStudio.App.Tests/ViewModels/SynthesisStubLiveBackendTests.cs | C# synthesis stub live-backend truth (runtime proven) |
| src/VoiceStudio.App/Services/EffectChainClient.cs | Route conflict fix (path-style → query-param for list) |

## Known Issues

- `@cache_response` decorator causes stale GET results within single test functions; mitigated by verifying mutations from response bodies and testing delete-then-GET in isolated test functions.
- Route conflict between `GET /api/effects/chains/{chain_id}` (query-param) and `GET /api/effects/chains/{project_id}` (path-style) for single-segment URLs; C# client fixed to use query-param endpoint; Python tests use query-param for list operations. Backend route conflict remains (tracked but non-blocking since all clients use the correct endpoint).
