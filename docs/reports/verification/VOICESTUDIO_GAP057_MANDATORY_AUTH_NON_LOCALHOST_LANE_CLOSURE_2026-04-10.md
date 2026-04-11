# Lane Closure: GAP-057 — Mandatory Auth for Non-Localhost Deployments

**Lane ID:** GOV-VOICESTUDIO-GAP057-MANDATORY-AUTH-NON-LOCALHOST-01  
**Status:** CLOSED  
**Date:** 2026-04-10  
**Execution row:** [GOV_VOICESTUDIO_GAP057_MANDATORY_AUTH_NON_LOCALHOST_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP057_MANDATORY_AUTH_NON_LOCALHOST_01_EXECUTION_ROW.md)

---

## Summary

Wired existing `require_auth_if_enabled` onto the `/api/audio` and `/api/audio/audit` routers so trust-bearing STS metadata, artifact access, export, upload, and analysis endpoints honor `VOICESTUDIO_REQUIRE_AUTH=true` the same way `/api/voice/*` already does.

Fixed **GET response cache** to include auth mode + credential presence in the cache key so anonymous 200 responses cannot bypass authentication when auth is required (`backend/api/response_cache.py` — `_cache_key_auth_segment`).

---

## Delivered

| Area | Change |
|------|--------|
| `backend/api/routes/audio.py` | Router `dependencies=[Depends(require_auth_if_enabled)]` |
| `backend/api/routes/audio_audit.py` | Same pattern for `/api/audio/audit/*` |
| `backend/api/response_cache.py` | Auth-aware cache key for GET JSON cache middleware |
| `backend/api/routes/ROUTE_SECURITY_MATRIX.md` | Documented `/api/audio/*`, audit routes, follow-up note for other `contexts/audio.py` routers |
| `tests/unit/backend/api/routes/test_audio_auth.py` | 11 tests — 401 without creds when `AUTH_REQUIRED` patched; API key success paths |
| `src/VoiceStudio.App.Tests/Views/Gap057AuthSeamTests.cs` | Client seam: `BackendTransport` 401 → `AUTHENTICATION_FAILED`; VM non-blocking marking failure |

---

## Proof matrix

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors) |
| `pytest tests/unit/backend/api/routes/test_audio_auth.py` | 11 PASS |
| Regression: `test_audio_file_endpoint.py`, `test_sts_sample_watermark.py`, `test_speech_to_speech_service.py` | 25 PASS |
| `dotnet test ... --filter FullyQualifiedName~Gap057` | 9 PASS |
| Full `VoiceStudio.App.Tests` | 3334 PASS / 274 skipped |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `python scripts/check_empty_catches.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | Overall: PASS — `artifacts/verify/20260410_194302/` |

---

## Authority

- **Single dependency:** `require_auth_if_enabled` from `backend/api/middleware/auth_middleware.py`
- **Credentials:** `X-API-Key` (`vs_*`) or `Authorization: Bearer` via `backend/api/auth.py`

---

## Hard OUT (honored)

- No new identity platform, RBAC redesign, or WebSocket auth (GAP-058)
- No watermark/STS logic changes beyond cache/auth interaction

---

## Follow-up

- Routers under `backend/api/routes/contexts/audio.py` with other prefixes (`/api/waveform`, `/api/audio-analysis`, …) remain without router-level `require_auth_if_enabled`; documented in ROUTE_SECURITY_MATRIX.
