# Execution Row: GAP-058 — WebSocket Auth Boundary

**Lane ID:** GOV-VOICESTUDIO-GAP058-WEBSOCKET-AUTH-01  
**Gap:** GAP-058 (Security — post GAP-057 HTTP/audio trust boundary)  
**Status:** CLOSED  
**Date frozen:** 2026-04-10  
**Owner Role:** Core Platform (Role 4)  
**Validator:** Overseer (Role 0) + Skeptical Validator  
**Predecessor:** GAP-057 closed (mandatory auth + auth-aware GET cache)

---

## Context

GAP-057 secured HTTP `/api/audio/*` and `/api/audio/audit/*`. App-level WebSockets `/ws/realtime` and `/ws/plugins` in `route_registry.py` had **no** handshake auth while exposing meters, training/batch status, quality metrics, and plugin command sync.

This lane wires **`require_ws_auth_if_enabled`** (same credential model as `require_auth_if_enabled`) at handshake time, closes with **4001** when `VOICESTUDIO_REQUIRE_AUTH=true` and credentials are missing, and updates C# clients to send **`X-API-Key`** / **`Authorization`** on upgrade where applicable. **`/ws/events`** remains intentionally **Public** (heartbeat counter only).

---

## Hard IN (Scope)

1. `WS_CLOSE_AUTH_REQUIRED = 4001` + `require_ws_auth_if_enabled(ws)` in `backend/api/middleware/auth_middleware.py`.
2. `route_registry.py`: `/ws/realtime` and `/ws/plugins` call auth helper before delegating; early return when auth required and denied.
3. `ROUTE_SECURITY_MATRIX.md`: document all **9** WebSocket routes (3 app-level + 6 `/api/*` router WS).
4. C#: `IWebSocketService` — `SetAuthHeaders` + `SetCredentialProvider`; `WebSocketService` / `PluginBridgeService` apply headers; env `VOICESTUDIO_API_KEY` fallback; `PluginBridgeService` reconnect stops on auth failure (no anonymous downgrade).
5. `BackendClient` optional `IUnifiedAuthService?` for credential provider when available.
6. Tests: `tests/unit/backend/api/ws/test_ws_auth.py`; `Gap058AuthSeamTests.cs`.
7. Proof matrix: build, targeted pytest/MSTest, regression (GAP-057 audio auth), full App.Tests, creep, empty-catch, `verify.ps1 -Quick`.

## Hard OUT

- No RBAC redesign, no new identity platform.
- No audit-trail expansion (GAP-059).
- No changes to the six existing `/api/*` `@router.websocket` handlers (already use `Depends(require_auth_if_enabled)`).
- No `/ws/events` protection.

---

## Authority Model

- **Same as HTTP:** `get_current_user_from_api_key` / `get_current_user_from_token`, `AUTH_REQUIRED` from `VOICESTUDIO_REQUIRE_AUTH`.
- **Handshake only** — no per-message auth in this lane.

---

## Acceptance Contract

- [x] `/ws/realtime` and `/ws/plugins` require credentials when `AUTH_REQUIRED=True`; missing credentials → close **4001**.
- [x] `/ws/events` works with auth on or off (public).
- [x] Local default (`AUTH_REQUIRED=False`) unchanged for anonymous WS.
- [x] C# clients send credentials on upgrade when API key/token/env available.
- [x] `PluginBridgeService` does not infinite-reconnect on auth failure.
- [x] No parallel auth authority.
- [x] `ROUTE_SECURITY_MATRIX.md` lists all 9 WS routes.
- [x] Targeted + full tests pass; creep + empty-catch + Quick pass.
- [x] Governance: closure report, STATE, tracker, registry.

---

## Proof Matrix (fill on close)

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `pytest tests/unit/backend/api/ws/test_ws_auth.py` | 9 PASS |
| `pytest tests/unit/backend/api/routes/test_audio_auth.py` (regression) | 11 PASS |
| `dotnet test ... --filter FullyQualifiedName~Gap058` | 4 PASS |
| Full `VoiceStudio.App.Tests` | 3338 PASS / 274 skipped |
| `check_ibackendclient_creep.py` | PASS |
| `check_empty_catches.py` | PASS |
| `verify.ps1 -Quick` | PASS `artifacts/verify/20260410_202311/` |

---

## Rollback

Remove auth calls from `ws_realtime` / `ws_plugins` in `route_registry.py`; remove `require_ws_auth_if_enabled` usage. Revert C# header injection if needed.
