# Execution Row: GAP-057 — Mandatory Auth for Non-Localhost Deployments

**Lane ID:** GOV-VOICESTUDIO-GAP057-MANDATORY-AUTH-NON-LOCALHOST-01  
**Gap:** GAP-057 (Security Phase 6)  
**Status:** CLOSED — 2026-04-10  
**Date frozen:** 2026-04-10  
**Owner Role:** Core Platform (Role 4)  
**Validator:** Overseer (Role 0) + Skeptical Validator  
**Predecessor:** GAP-056 umbrella closed (all slices)

---

## Context

GAP-056 delivered STS trust metadata and sample-level watermark status surfaced via `GET /api/audio/{id}/marking`, artifact download, and export headers. The `/api/audio` router had **no** `require_auth_if_enabled` dependency, so when `VOICESTUDIO_REQUIRE_AUTH=true` those surfaces remained callable without credentials unlike `/api/voice/*`.

This lane closes that gap by wiring the **existing** FastAPI dependency `require_auth_if_enabled` ([backend/api/middleware/auth_middleware.py](backend/api/middleware/auth_middleware.py)) onto the trust-bearing audio routes — **no new auth framework**.

---

## Hard IN (Scope)

1. Router-level `dependencies=[Depends(require_auth_if_enabled)]` on [backend/api/routes/audio.py](backend/api/routes/audio.py) (`/api/audio/*`).
2. Same pattern on [backend/api/routes/audio_audit.py](backend/api/routes/audio_audit.py) (`/api/audio/audit/*`) — same URL namespace as marking/export trust surface.
3. Update [ROUTE_SECURITY_MATRIX.md](backend/api/routes/ROUTE_SECURITY_MATRIX.md) with `/api/audio/*` and `/api/audio/audit/*` entries; note other `contexts/audio.py` routers (waveform, effects, …) as **follow-up** if not modified this lane.
4. Python tests: 401 without credentials when `AUTH_REQUIRED` patched True; normal behavior when False; authenticated request with valid `X-API-Key` succeeds.
5. C# tests: `BackendTransport` maps 401; `Gap057Tests` extended with auth/error-handling seam checks (file-based or behavioral where feasible).
6. **Response cache:** [backend/api/response_cache.py](backend/api/response_cache.py) `_cache_key_auth_segment()` — GET JSON cache keys include `AUTH_REQUIRED` + credential presence so a cached anonymous **200** cannot be served when auth is required (would incorrectly bypass **401**).
7. Proof matrix: build, targeted pytest, targeted MSTest, full App.Tests, creep, empty-catch, `verify.ps1 -Quick`.

## Hard OUT (Not in scope)

1. New identity platform, user-management UI, OAuth wiring, or RBAC redesign (GAP-061).
2. WebSocket auth (GAP-058).
3. Global ASGI auth middleware — keep `Depends()` pattern.
4. Watermarking / STS / marking logic changes except as required for tests to pass.
5. Broad sweep of every router in `contexts/audio.py` beyond `audio.py` + `audio_audit.py` unless explicitly listed above.

---

## Authority Model

- **Single choke point:** `require_auth_if_enabled` from `auth_middleware.py`.
- **Credentials:** `X-API-Key` (`vs_*`) or `Authorization: Bearer` (JWT), resolved via [backend/api/auth.py](backend/api/auth.py) `APIKeyManager` / `JWTManager`.

---

## Acceptance Contract

- [x] `/api/audio/*` protected when auth required.
- [x] `/api/audio/audit/*` protected when auth required.
- [x] Unauthorized → 401 when `VOICESTUDIO_REQUIRE_AUTH` behavior is simulated via patched `AUTH_REQUIRED` in tests.
- [x] Default local mode (auth off): anonymous behavior preserved.
- [x] No parallel auth authority introduced.
- [x] ROUTE_SECURITY_MATRIX updated.
- [x] Targeted + full tests pass; creep + empty-catch + Quick pass.
- [x] Governance: closure report, STATE, tracker, registry.

---

## Proof Matrix (closed)

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `pytest tests/unit/backend/api/routes/test_audio_auth.py` | 11 PASS |
| Regression: `test_audio_file_endpoint.py` + STS service tests | 25 PASS |
| `dotnet test ... --filter FullyQualifiedName~Gap057` | 9 PASS |
| Full `VoiceStudio.App.Tests` | 3334 PASS / 274 skipped |
| `check_ibackendclient_creep.py` | PASS |
| `check_empty_catches.py` | PASS |
| `verify.ps1 -Quick` | PASS `artifacts/verify/20260410_194302/` |

---

## Rollback

Remove `dependencies=[Depends(require_auth_if_enabled)]` from `audio.py` and `audio_audit.py` routers.
