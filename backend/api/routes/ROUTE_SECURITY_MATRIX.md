# Route Security Matrix

**GAP-CRIT-004**: Documents which routes require authentication and their current status.

## Security Tiers

| Tier | Description | Auth Required |
|------|-------------|---------------|
| **Public** | Health checks, version info, read-only discovery | No |
| **Protected** | User data, synthesis, training, file operations | Yes (when auth enabled) |
| **Admin** | Configuration, backup, system settings | Yes (always) |

## Public Routes (No Auth Required)

These routes are intentionally public:

| Route | File | Reason |
|-------|------|--------|
| `GET /api/health/*` | health.py | Service discovery, monitoring |
| `GET /api/version` | main.py | API version info |
| `GET /api/voice-browser/voices` | voice_browser.py | Voice catalog browsing |
| `GET /api/engines` | engines.py | Engine discovery |
| `GET /` | main.py | Root endpoint |
| `GET /metrics` | metrics.py | Prometheus metrics |
| `WS /ws/events` | route_registry.py → [ws/events.py](../ws/events.py) | Demo heartbeat counter only; **no user data** (GAP-058) |

## WebSocket routes (GAP-058)

| Route | Registration | Auth mechanism | Tier |
|-------|--------------|----------------|------|
| `WS /ws/events` | `route_registry.py` | None (intentionally public) | **Public** |
| `WS /ws/realtime` | `route_registry.py` | `require_ws_auth_if_enabled` (handshake); close **4001** if missing credentials when auth required | **Protected** |
| `WS /ws/plugins` | `route_registry.py` | `require_ws_auth_if_enabled` (handshake); close **4001** if missing credentials when auth required | **Protected** |
| `WS /api/voice/synthesize/stream` | `routes/voice/streaming.py` (voice router) | `Depends(require_auth_if_enabled)` on [voice/_shared.py](voice/_shared.py) router | **Protected** |
| `WS /api/rvc/convert/realtime` | `routes/rvc.py` | Router-level `require_auth_if_enabled` | **Protected** |
| `WS /api/orchestrator/events/{job_id}` | `routes/orchestrator.py` | Router-level `require_auth_if_enabled` | **Protected** |
| `WS /api/pipeline/stream` | `routes/pipeline.py` | Router-level `require_auth_if_enabled` | **Protected** |
| `WS /api/realtime-converter/{session_id}/stream` | `routes/realtime_converter.py` | Router-level `require_auth_if_enabled` | **Protected** |
| `WS /api/realtime-visualizer/{session_id}/stream` | `routes/realtime_visualizer.py` | Router-level `require_auth_if_enabled` | **Protected** |

Handshake credentials: `X-API-Key` or `Authorization: Bearer` (same as HTTP). Environment: `VOICESTUDIO_REQUIRE_AUTH=true` enables enforcement for app-level `/ws/*` routes.

## Protected Routes (Auth When Enabled)

These routes require `Depends(require_auth_if_enabled)`:

### Voice Operations
- `POST /api/voice/synthesize` - voice.py ✓
- `POST /api/voice/clone` - voice.py ✓
- `WS /api/voice/synthesize/stream` - voice/streaming.py (voice router) ✓ — see **WebSocket routes** table

### Profile Management
- `POST /api/profiles` - profiles.py ✓
- `PUT /api/profiles/{id}` - profiles.py
- `DELETE /api/profiles/{id}` - profiles.py

### Training
- `POST /api/training/*` - training.py ✓

### Project Management
- `POST /api/projects` - projects.py ✓
- `PUT /api/projects/{id}` - projects.py
- `DELETE /api/projects/{id}` - projects.py

### Timeline/Tracks
- All write operations - timeline.py ✓

### Jobs
- `POST /api/jobs` - jobs.py ✓
- `DELETE /api/jobs/{id}` - jobs.py

### Audio — core router (GAP-057: 2026-04-10)

Router-level `Depends(require_auth_if_enabled)` on [audio.py](audio.py) (`prefix=/api/audio`). When `VOICESTUDIO_REQUIRE_AUTH=true`, all listed endpoints require `X-API-Key` or `Authorization: Bearer`. **GET response cache** keys include auth mode + credential presence ([response_cache.py](../response_cache.py) `_cache_key_auth_segment`) so anonymous 200s are not served when auth is required.

- `GET /api/audio/{audio_id}/marking` — STS / watermark trust metadata ✓
- `GET /api/audio/file/{audio_id}` — artifact streaming ✓
- `POST /api/audio/export` — format export ✓
- `POST /api/audio/upload` — upload ✓
- `GET /api/audio/formats` — format catalog (same router; protected when auth enabled) ✓
- `GET /api/audio/waveform`, `/spectrogram`, `/loudness`, `/meters`, `/radar`, `/phase`, etc. — analysis ✓

### Audio — module audit (GAP-057)

- `GET /api/audio/audit/*` — [audio_audit.py](audio_audit.py) ✓

### Audio context — follow-up (not GAP-057)

Routers mounted via [contexts/audio.py](contexts/audio.py) under other prefixes (`/api/waveform`, `/api/audio-analysis`, `/api/effects`, `/api/recording`, …) do **not** yet use `require_auth_if_enabled` at router scope. Track as future security hardening if those deployments expose non-localhost listeners.

## Admin Routes (Always Protected)

These should always require auth even if auth is globally disabled:

### Backup/Restore (GAP-CRIT-004: Auth added 2026-02-11)
- `POST /api/backup` - backup.py ✓
- `POST /api/backup/{id}/restore` - backup.py ✓
- `POST /api/backup/upload` - backup.py ✓
- `DELETE /api/backup/{id}` - backup.py ✓

### Settings (GAP-CRIT-004: Auth added 2026-02-11)
- `POST /api/settings` - settings.py ✓
- `PUT /api/settings/{category}` - settings.py ✓
- `POST /api/settings/reset` - settings.py ✓

### Models (GAP-CRIT-004: Auth added 2026-02-11)
- `POST /api/models` - models.py ✓
- `POST /api/models/import` - models.py ✓
- `PUT /api/models/{engine}/{model_name}/update-checksum` - models.py ✓
- `DELETE /api/models/{engine}/{model_name}` - models.py ✓

### API Keys
- All operations - api_key_manager.py ✓

## Archived Routes (Arch Review Task 1.4)

Moved to `routes/_archived/`: todo_panel, ultimate_dashboard, mcp_dashboard, adr, docs, reward, text_highlighting, script_editor, mix_scene, deepfake_creator.

## Face Swap (Arch Review Task 1.4)

- `POST /api/face-swap/create` - face_swap.py (gated, consent required)
- `GET /api/face-swap/engines` - face_swap.py
- Alias `/api/deepfake-creator/*` for backward compatibility. Gate: `experimental.face_swap` in config/feature_flags.json.

## Implementation Notes

1. Use `Depends(require_auth_if_enabled)` for Protected tier
2. Use `Depends(require_auth_always)` for Admin tier (to be created)
3. Update routes marked "NEEDS AUTH" as part of GAP-CRIT-004
