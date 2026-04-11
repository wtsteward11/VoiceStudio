# Lane Closure: GOV-VOICESTUDIO-GAP061-RBAC-WIRING-TRUST-SURFACES-01

**Gap:** GAP-061 — RBAC wiring on STS + transformed audio export; TestClient rate-limit root fix  
**Status:** CLOSED  
**Date:** 2026-04-11  
**Predecessor:** GAP-057 (identity), GAP-059 (trust audit), GAP-060 (model provenance)

---

## Summary

- **Rate limit (root cause):** Starlette `TestClient` uses `client.host == "testclient"`. Added `"testclient"` to `RateLimitMiddleware._LOOPBACK_HOSTS` in `backend/api/rate_limiting_enhanced.py`. Removed class-level `RateLimitMiddleware.dispatch` monkey-patch from `test_audio_trust_audit.py` `client` fixture.
- **RBAC:** `require_user_role_for_trust_surfaces` in `backend/api/middleware/auth_middleware.py` — minimum `UserRole.USER` when `AUTH_REQUIRED`; local desktop (`AUTH_REQUIRED=False`) uses synthetic local principal when anonymous, else enforces hierarchy on authenticated principals.
- **Trust enrichment:** `TrustAuditEvent.user_role` (optional string); `record_sts_conversion` / `record_audio_export` accept `user_role`; STS route and export route pass `user.role.value`; `SpeechToSpeechService.convert(..., user_role=...)`.

**Out of scope (honored):** No new RBAC enums/services; no UI; no audit backfill; no synthesis-path changes beyond STS/export.

---

## Proof matrix (recorded)

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `pytest tests/unit/backend/services/test_gap061_rbac_wiring.py` | 8 PASS |
| Regression cohort (trust + STS + audio routes + provenance + auth middleware) | 66 PASS |
| Full `VoiceStudio.App.Tests` | 3338 PASS / 274 skipped |
| `check_ibackendclient_creep.py` | PASS |
| `check_empty_catches.py` | PASS |
| `verify.ps1 -Quick` | PASS `artifacts/verify/20260410_222838/` |
| `run_verification.py` | PASS after governance commit (**completion_guard**) |

---

## Key files

| Path | Role |
|------|------|
| `backend/api/rate_limiting_enhanced.py` | `testclient` in `_LOOPBACK_HOSTS` |
| `backend/api/middleware/auth_middleware.py` | `ROLE_HIERARCHY_LEVEL`, `_ensure_user_meets_minimum_role`, `require_user_role_for_trust_surfaces` |
| `backend/services/trust_audit_service.py` | `user_role` on events + record methods |
| `backend/services/speech_to_speech_service.py` | `user_role` passed to trust audit |
| `backend/api/routes/voice/speech_to_speech.py` | `Depends(require_user_role_for_trust_surfaces)` |
| `backend/api/routes/audio.py` | Export route role dependency + `user_role` on export audit |
| `tests/unit/backend/services/test_gap061_rbac_wiring.py` | 8 tests |
| `tests/unit/backend/api/routes/test_audio_trust_audit.py` | Simplified `TestClient` fixture |
| `docs/design/GOV_VOICESTUDIO_GAP061_RBAC_WIRING_TRUST_SURFACES_01_EXECUTION_ROW.md` | Execution row (CLOSED) |

---

## Rollback

Revert role dependencies on STS/export; remove `user_role` from trust audit API; remove `testclient` from `_LOOPBACK_HOSTS` and restore test monkey-patch if required.
