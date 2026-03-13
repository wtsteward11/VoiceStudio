# VoiceStudio Architecture Foundations Guide

> **Version**: 1.0
> **Last Updated**: 2026-02-14
> **Classification**: Developer Documentation
> **Phase**: Deterministic Sentinel - Phase 5

---

## Overview

This guide documents the architectural foundation patterns implemented in VoiceStudio, covering dependency injection, API versioning, caching, message queuing, and database migrations.

**Truth hierarchy**: When docs disagree, precedence is code → ADRs → CI → STATE.md → CLAUDE.md. See [AGENTS.md](../../AGENTS.md) § Truth Hierarchy and [CLAUDE.md](../../CLAUDE.md).

---

## 1. Dependency Injection System

### 1.1 Architecture

VoiceStudio uses `Microsoft.Extensions.DependencyInjection` for the C# frontend:

| Component | Purpose |
|-----------|---------|
| `AppServices` | Static DI facade for service resolution |
| `ViewModelFactory` | ViewModel instantiation with DI |
| `ViewModelLocator` | XAML design-time and runtime binding |
| `ServiceProvider` | Root container initialization |

### 1.2 Key Components

#### AppServices (`src/VoiceStudio.App/Services/AppServices.cs`)

```csharp
// Initialize DI container at startup
AppServices.Initialize(serviceProvider);

// Resolve services anywhere
var backend = AppServices.Get<IBackendClient>();
var settings = AppServices.Get<ISettingsService>();
```

#### Service Registration Pattern

```csharp
services.AddSingleton<IBackendClient, BackendClient>();
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<INavigationService, NavigationService>();
```

### 1.3 Best Practices

- Use constructor injection for ViewModels
- Register interfaces, not concrete types
- Use `AddSingleton` for stateful services
- Use `AddTransient` for stateless services
- Lazy resolution for UI-dependent services (e.g., DialogService)

---

## 2. API Versioning System

### 2.1 Architecture

VoiceStudio supports multiple API versions simultaneously:

| Version | Status | Path Prefix |
|---------|--------|-------------|
| v1 | Stable | `/api/v1/` |
| v2 | Stable | `/api/v2/` |
| v3 | Current | `/api/v3/` |

### 2.2 Key Components

#### VersionedAPIRouter (`backend/api/versioning/router.py`)

```python
from backend.api.versioning import VersionedAPIRouter

router = VersionedAPIRouter(prefix="/voices", tags=["voices"])

@router.get("/{voice_id}", versions=["v1", "v2", "v3"])
async def get_voice(voice_id: str):
    ...
```

#### ApiVersionNegotiator (`backend/api/versioning/negotiation.py`)

```python
from backend.api.versioning import ApiVersionNegotiator

negotiator = ApiVersionNegotiator()
version = negotiator.negotiate(request)
```

#### DeprecationManager (`backend/api/versioning/deprecation.py`)

```python
from backend.api.versioning import DeprecationManager

@deprecated(sunset_date="2026-06-01", replacement="/api/v3/voices")
async def get_voice_v1(...):
    ...
```

### 2.3 Version Headers

| Header | Description |
|--------|-------------|
| `X-API-Version` | Requested API version |
| `X-API-Min-Version` | Minimum supported version |
| `Deprecation` | Deprecation notice |
| `Sunset` | End-of-life date |

### 2.4 Migration Guide

When creating new endpoints:

1. Add to v3 router first
2. If backward-compatible, add to v2/v1 routers
3. Use `@deprecated` decorator for sunset versions
4. Document breaking changes in CHANGELOG

---

## 3. Caching Layer

### 3.1 Architecture

VoiceStudio implements a multi-tier caching system:

| Tier | Implementation | Use Case |
|------|----------------|----------|
| L1 | In-memory (CacheAdapter) | Hot data, API responses |
| L2 | Response cache | HTTP response caching |
| L3 | Content-addressed | Audio artifacts |

### 3.2 Key Components

#### CacheAdapter (`backend/infrastructure/adapters/cache.py`)

```python
from backend.infrastructure.adapters import CacheAdapter

cache = CacheAdapter(default_ttl=300, max_size=1000)

# Store value
await cache.set("key", value, ttl=60)

# Get value
value = await cache.get("key")

# Get with fallback
value = await cache.get_or_set("key", factory_fn, ttl=300)
```

#### ContentAddressedAudioCache

```python
from backend.services import ContentAddressedAudioCache

cache = ContentAddressedAudioCache()
hash_key = await cache.store(audio_bytes)
audio = await cache.retrieve(hash_key)
```

### 3.3 Cache Statistics

```python
stats = await cache.health_check()
# Returns: {"entries": 150, "hit_rate": 0.85, "hits": 1200, "misses": 212}
```

### 3.4 Best Practices

- Set appropriate TTL for data type
- Use content addressing for immutable data (audio)
- Monitor cache hit rates via health endpoints
- Clear cache on data mutations

---

## 4. Message Queue System

### 4.1 Architecture

VoiceStudio uses an in-memory priority queue for request management:

| Priority | Value | Use Case |
|----------|-------|----------|
| CRITICAL | 0 | System operations |
| HIGH | 1 | Real-time user requests |
| NORMAL | 2 | Standard requests |
| LOW | 3 | Background tasks |
| BATCH | 4 | Batch processing |

### 4.2 Key Components

#### RequestQueue (`backend/services/request_queue.py`)

```python
from backend.services.request_queue import RequestQueue, RequestPriority

queue = RequestQueue(max_size=1000, max_concurrent=10)

# Enqueue request
request_id = await queue.enqueue(
    payload=synthesis_request,
    priority=RequestPriority.HIGH,
    engine_type="xtts",
    timeout_seconds=60
)

# Process with handler
await queue.start(process_handler)
```

#### QueuedRequest

```python
@dataclass
class QueuedRequest:
    id: str
    priority: RequestPriority
    payload: Any
    created_at: datetime
    engine_type: Optional[str]
    timeout_seconds: float = 300.0
```

### 4.3 Features

- **Priority ordering**: Higher priority requests processed first
- **Concurrency control**: Limit simultaneous engine operations
- **Timeout handling**: Automatic timeout for stalled requests
- **Backpressure**: Reject new requests when queue is full
- **Statistics**: Track queue depth, wait times, failure rates

### 4.4 Monitoring

```python
stats = queue.get_stats()
# Returns: QueueStats(total_enqueued=500, total_processed=480, current_size=20, ...)
```

---

## 5. Database Migration System

### 5.1 Architecture

VoiceStudio uses a versioned migration system:

| Component | Purpose |
|-----------|---------|
| `MigrationRunner` | Orchestrates migrations |
| `Migration` | Base class for migrations |
| `v001_*`, `v002_*` | Individual migration files |

### 5.2 Key Components

#### MigrationRunner (`backend/data/migrations/migration_runner.py`)

```python
from backend.data.migrations import MigrationRunner

runner = MigrationRunner(database_path)

# Run pending migrations
await runner.migrate()

# Rollback last migration
await runner.rollback()

# Check status
status = await runner.status()
```

#### Creating Migrations

```python
from backend.data.migrations import Migration

class V003_NewFeature(Migration):
    version = 3
    description = "Add new feature table"
    
    async def up(self, db):
        await db.execute("""
            CREATE TABLE new_feature (
                id TEXT PRIMARY KEY,
                ...
            )
        """)
    
    async def down(self, db):
        await db.execute("DROP TABLE IF EXISTS new_feature")
```

### 5.3 Current Migrations

| Version | Description |
|---------|-------------|
| v001 | Core persistence tables (jobs, profiles, settings) |
| v002 | Performance indexes |

### 5.4 Best Practices

- Always implement `up` and `down` methods
- Test rollback before deploying
- Use transactions for data migrations
- Document breaking changes
- Never modify existing migrations

---

## Integration Points

### Startup Sequence

1. **DI Container**: Initialize `AppServices` with service registrations
2. **Database**: Run `MigrationRunner.migrate()` to apply pending migrations
3. **Cache**: Initialize `CacheAdapter` with configuration
4. **Queue**: Start `RequestQueue` with concurrency limits
5. **API**: Mount versioned routers with middleware

### Health Checks

All foundation components expose health endpoints:

```
GET /api/health/detailed
{
  "database": {"status": "healthy", "migrations_applied": 2},
  "cache": {"status": "healthy", "hit_rate": 0.85},
  "queue": {"status": "healthy", "current_size": 5},
  "api_version": "v3"
}
```

---

## Troubleshooting

### DI Resolution Failures

```
InvalidOperationException: Unable to resolve service for type 'IService'
```

**Solution**: Verify service is registered in `AppServices.Initialize()`.

### Migration Failures

```
MigrationError: Migration v002 failed
```

**Solution**: Check migration logs, fix issue, use `rollback()` if needed.

### Queue Backpressure

```
QueueFullError: Request queue at capacity
```

**Solution**: Increase `max_size`, add more workers, or implement request shedding.

---

## References

- [ADR-030: ViewModel DI Migration](../architecture/decisions/ADR-030-viewmodel-di-migration.md)
- [ADR-031: API Versioning Strategy](../architecture/decisions/ADR-031-api-versioning-strategy.md)
- [Architecture Overview](ARCHITECTURE.md)
