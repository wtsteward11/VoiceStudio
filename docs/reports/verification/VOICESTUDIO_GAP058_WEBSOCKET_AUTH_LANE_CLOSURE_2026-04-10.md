# Lane Closure: GAP-058 — WebSocket Auth Boundary

**Lane ID:** GOV-VOICESTUDIO-GAP058-WEBSOCKET-AUTH-01  
**Status:** CLOSED  
**Date:** 2026-04-10  
**Execution row:** [GOV_VOICESTUDIO_GAP058_WEBSOCKET_AUTH_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP058_WEBSOCKET_AUTH_01_EXECUTION_ROW.md)  
**Commit:** `aa3099bd5272d8438360fe6253014b9f928df681` (branch `main`)

---

## Summary

Handshake-time authentication for app-level WebSockets **`/ws/realtime`** and **`/ws/plugins`** (registered in `backend/api/route_registry.py`), aligned with HTTP auth: same `X-API-Key` / `Authorization: Bearer` resolution and `AUTH_REQUIRED` flag. Unauthenticated clients receive WebSocket close code **4001** (`WS_CLOSE_AUTH_REQUIRED`). **`/ws/events`** remains intentionally public (heartbeat only). C# **`WebSocketService`** and **`PluginBridgeService`** apply credentials on upgrade; **`PluginBridgeService`** reconnect loop stops on auth-class failures (no anonymous downgrade). **`InternalsVisibleTo`** for tests is emitted via `src/VoiceStudio.App/Properties/InternalsVisibility.cs` because `GenerateAssemblyInfo=false` ignores csproj `InternalsVisibleTo` items.

---

## Delivered

| Area | Change |
|------|--------|
| `backend/api/middleware/auth_middleware.py` | `WS_CLOSE_AUTH_REQUIRED = 4001`, `require_ws_auth_if_enabled(ws)` |
| `backend/api/route_registry.py` | Auth gate on `/ws/realtime`, `/ws/plugins`; `/ws/events` documented public |
| `backend/api/routes/ROUTE_SECURITY_MATRIX.md` | All **9** WebSocket routes documented |
| `src/VoiceStudio.App/Services/WebSocketService.cs` | `SetAuthHeaders` / `SetCredentialProvider`; close **4001** handling; `NotifyAuthenticationRequiredCloseIfNeeded` |
| `src/VoiceStudio.App/Services/PluginBridgeService.cs` | Handshake headers; `ConnectHandshakeAsyncOverrideForSeamTests`; reconnect auth stop |
| `src/VoiceStudio.App/Services/BackendClient.cs` | Optional `IUnifiedAuthService` for WS credential provider |
| `src/VoiceStudio.App/Properties/InternalsVisibility.cs` | `[assembly: InternalsVisibleTo("VoiceStudio.App.Tests")]` |
| `tests/unit/backend/api/ws/test_ws_auth.py` | **9** tests |
| `src/VoiceStudio.App.Tests/Views/Gap058AuthSeamTests.cs` | **4** seam tests |
| `BackendClientTransportPolicyTests.cs` / `ConnectionStatusClientTests.cs` | Reflection ctor updated for `internal BackendClient(..., IUnifiedAuthService?)` |

---

## Proof matrix

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `pytest tests/unit/backend/api/ws/test_ws_auth.py` | 9 PASS |
| `pytest tests/unit/backend/api/routes/test_audio_auth.py` (GAP-057 regression) | 11 PASS |
| `dotnet test ... --filter FullyQualifiedName~Gap058` | 4 PASS |
| Full `VoiceStudio.App.Tests` | 3338 PASS / 274 skipped |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `python scripts/check_empty_catches.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260410_202311/` |

---

## Authority

- **Single model:** `get_current_user_from_api_key` / `get_current_user_from_token`, `AUTH_REQUIRED` / `VOICESTUDIO_REQUIRE_AUTH` — same as GAP-057 HTTP surface.
- **Handshake only** — no per-message auth in this lane.

---

## Hard OUT (honored)

- No RBAC redesign; no `/ws/events` protection; no changes to the six existing `/api/*` `@router.websocket` handlers.

---

## Follow-up

- **GAP-059** audit trail / expanded logging (out of scope here).
