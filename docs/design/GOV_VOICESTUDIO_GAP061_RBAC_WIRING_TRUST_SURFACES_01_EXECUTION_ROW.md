# Execution Row: GAP-061 — RBAC Wiring + Trust Surface Role Identity

**Lane ID:** GOV-VOICESTUDIO-GAP061-RBAC-WIRING-TRUST-SURFACES-01  
**Gap:** GAP-061 (Security — RBAC on identity-sensitive operations; rate-limit TestClient root fix)  
**Status:** CLOSED  
**Date frozen:** 2026-04-11  
**Date closed:** 2026-04-11  
**Owner Role:** Core Platform (Role 4)  
**Validator:** Overseer (Role 0) + Skeptical Validator  
**Predecessor:** GAP-059 (trust audit), GAP-060 (model provenance), GAP-057 (identity)

---

## Context

GAP-057 proves identity exists; GAP-059/060 record what happened and which model. None answer **whether the principal was authorized at role level** for STS conversion or export of transformed audio. Existing `require_role_middleware` and `UserRole` are wired to trust surfaces with `user_role` on `TrustAuditEvent`.

---

## Hard IN (Scope)

1. `backend/api/rate_limiting_enhanced.py` — add `"testclient"` to loopback-exempt hosts (Starlette `TestClient`).
2. `tests/unit/backend/api/routes/test_audio_trust_audit.py` — remove `RateLimitMiddleware` monkey-patch; rely on loopback exempt.
3. `backend/services/trust_audit_service.py` — `user_role` on `TrustAuditEvent`; `record_sts_conversion` / `record_audio_export` parameters.
4. `backend/services/speech_to_speech_service.py` — pass `user_role` into trust audit.
5. `backend/api/routes/voice/speech_to_speech.py` — `Depends(require_user_role_for_trust_surfaces)`; thread role into `SpeechToSpeechService.convert`.
6. `backend/api/routes/audio.py` — same dependency on `POST /api/audio/export`; pass `user_role` to `record_audio_export`.
7. `backend/api/middleware/auth_middleware.py` — `require_user_role_for_trust_surfaces` (minimum `UserRole.USER`; local anonymous → synthetic local principal).
8. Tests: `tests/unit/backend/services/test_gap061_rbac_wiring.py` (8+); regression cohort green.
9. Proof matrix: build, targeted pytest, regression cohort, App.Tests, creep, empty-catch, `verify.ps1 -Quick`, `run_verification.py`.
10. Governance: closure report, tracker, CANONICAL_REGISTRY, STATE, `openmemory.md`.

## Hard OUT

- No new `RBACService` / enum changes / session store.
- No UI role display; no audit backfill.
- No `IBackendClient` creep; no broad synthesis-path RBAC beyond this row.

---

## Authority Model

- **Role gate:** `require_user_role_for_trust_surfaces` — delegates to `require_role_middleware(..., UserRole.USER)` when `AUTH_REQUIRED`; local mode uses synthetic principal when anonymous, else enforces hierarchy on authenticated principal.
- **Trust enrichment:** callers pass `user_role` (string) into `TrustAuditService` record methods.

---

## Acceptance Contract

- [x] `POST /api/voice/sts/convert` returns 403 for `UserRole.GUEST` when auth is required; USER/ADMIN succeed past role gate (subject to consent/engine).
- [x] `POST /api/audio/export` same role behavior for transformed-export path.
- [x] `TrustAuditEvent` / emitted metadata includes `user_role` for STS and export records when provided.
- [x] Local mode (`AUTH_REQUIRED=False`): anonymous requests use synthetic local principal; role gate does not block desktop single-user flow.
- [x] `user_role` joinable with `artifact_id` / `correlation_id` in audit metadata.
- [x] Targeted + regression + verification gates pass; governance surfaces updated.

---

## Proof Matrix (fill on close)

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `pytest tests/unit/backend/services/test_gap061_rbac_wiring.py` | 8 PASS |
| Regression cohort (trust + STS + audio + provenance + `test_auth_middleware`) | 66 PASS |
| Full `VoiceStudio.App.Tests` | 3338 PASS / 274 skipped |
| `verify.ps1 -Quick` | PASS `artifacts/verify/20260410_222838/` |
| `run_verification.py` | PASS (**completion_guard** post-commit) |

---

## Rollback

Revert middleware dependency on STS/export routes; remove `user_role` from trust audit API; remove `testclient` loopback line and restore test monkey-patch if needed.
