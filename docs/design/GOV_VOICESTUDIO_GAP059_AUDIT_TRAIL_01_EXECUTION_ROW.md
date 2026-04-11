# Execution Row: GAP-059 — Audit Trail (Trust Evidence Lane)

**Lane ID:** GOV-VOICESTUDIO-GAP059-AUDIT-TRAIL-01  
**Gap:** GAP-059 (Security / trust evidence — post GAP-055–058 stack)  
**Status:** CLOSED  
**Date frozen:** 2026-04-11  
**Date closed:** 2026-04-11  
**Owner Role:** Core Platform (Role 4)  
**Validator:** Overseer (Role 0) + Skeptical Validator  
**Predecessor:** GAP-058 closed (WebSocket auth boundary)

---

## Context

Trust-sensitive surfaces (STS conversion, transformed audio export/download, durable marking reads) need a **single canonical** structured audit lane answering the six trust questions. Generic `AuditLogger` entries and ad-hoc `logger.info` calls are not queryable or schema-stable.

This lane introduces **`TrustAuditService`** + **`TrustAuditEvent`**, best-effort writes via existing JSONL `AuditLogger` (`metadata` carries the trust payload), and wires four HTTP surfaces only.

---

## Hard IN (Scope)

1. `backend/services/trust_audit_service.py` — schema + `get_trust_audit_service()` + `record_sts_conversion`, `record_audio_export`, `record_audio_download`, `record_marking_read`.
2. `speech_to_speech_service.py` — replace consent `logger.info` block; denial-path trust events before `ServiceError` raises where specified.
3. `backend/api/routes/audio.py` — export (transformed only), file GET (transformed only), marking GET (always).
4. `backend/api/routes/voice/speech_to_speech.py` — pass `auth_subject` + `correlation_id` into STS service.
5. Tests: `test_trust_audit_service.py` (8+), `test_audio_trust_audit.py` (4+).
6. Proof matrix: build, targeted pytest + GAP-057/058 regression, full App.Tests, creep, empty-catch, `verify.ps1 -Quick`.
7. Governance: closure report, tracker, CANONICAL_REGISTRY, STATE, `openmemory.md`.

## Hard OUT

- No SIEM / external log shipper; no trust-event query API.
- No RBAC redesign; no C# `IBackendClient` changes.
- No edits to `audit_logger.py` core behavior (reuse only); no unification of unrelated audit subsystems.
- No WebSocket per-message audit.

---

## Authority Model

- **Single authority:** `TrustAuditService` for trust-lane events; persistence via `get_audit_logger()` with structured `metadata`.
- **Best-effort:** write failures → `logger.warning`; **never** fail the user request.
- **`auth_subject`:** route-layer `User.user_id` when present; **never** full API key strings.
- **`correlation_id`:** `request.state.correlation_id` / `correlation_id_var` where available.

---

## Acceptance Contract

- [x] STS conversion success emits canonical `TrustAuditEvent` (six trust questions answerable).
- [x] STS consent denial emits `result="denied"`, `reason_code="CONSENT_REQUIRED"`.
- [x] Audio export of **transformed** artifact emits audit; non-transformed does not.
- [x] Audio file GET for **transformed** artifact emits audit; non-transformed does not.
- [x] Marking GET always emits audit (on successful handler completion).
- [x] Best-effort: audit write failure does not fail request.
- [x] `auth_subject` never contains a full API key (route passes `user_id` only).
- [x] `correlation_id` populated from `request.state` when available.
- [x] No parallel trust audit authority.
- [x] Targeted + regression + full tests pass; creep + empty-catch + Quick pass.
- [x] Governance surfaces closed.

---

## Proof Matrix (fill on close)

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `pytest tests/unit/backend/services/test_trust_audit_service.py` | 8 PASS |
| `pytest tests/unit/backend/api/routes/test_audio_trust_audit.py` | 4 PASS |
| `pytest tests/unit/backend/api/routes/test_audio_auth.py` | 11 PASS |
| `pytest tests/unit/backend/api/ws/test_ws_auth.py` | 9 PASS |
| `pytest tests/unit/backend/services/test_speech_to_speech_service.py` | 10 PASS |
| Full `VoiceStudio.App.Tests` | 3338 PASS / 274 skipped |
| `check_ibackendclient_creep.py` | PASS |
| `check_empty_catches.py` | PASS |
| `verify.ps1 -Quick` | PASS `artifacts/verify/20260410_212117/` |

---

## Rollback

Remove `trust_audit_service.py`; revert STS + `audio.py` + `speech_to_speech.py` wiring; delete new tests and governance addenda.
