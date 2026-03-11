# Sentinel Testing Guide

This guide covers how to use, run, and extend the VoiceStudio Sentinel Testing infrastructure.

## Overview

The Sentinel Workflow is a deterministic, reproducible test harness that validates the complete VoiceStudio audio processing pipeline. It produces "repro packets" - comprehensive artifacts that enable debugging and regression detection.

### Key Features

- **Deterministic**: Same inputs produce comparable outputs
- **Reproducible**: Any failure can be reproduced with the repro packet
- **CI-Integrated**: Runs automatically on every push/PR
- **Contract-First**: JSON schemas validate API responses

## Quick Start

### Prerequisites

```bash
# Install dependencies
pip install httpx jsonschema pytest pytest-asyncio

# Ensure the sentinel audio fixture exists
ls fixtures/audio/sentinel_16k_mono.wav
```

### Running Locally

```bash
# Run the sentinel workflow directly (requires backend)
python scripts/proof_runs/sentinel_audio_workflow.py --api-base http://localhost:8000

# Run smoke tests (no backend required)
python -m pytest tests/sentinel/ -v -m "smoke and not backend_required"

# Run full integration tests (requires backend)
python -m pytest tests/sentinel/ -v -m backend_required
```

### Running in CI

The sentinel workflow runs automatically via `.github/workflows/sentinel_backend_smoke.yml` on:
- Push to `main`, `develop`, `release/*`
- Pull requests to `main`, `develop`

**Backend cold-start in CI:** On Ubuntu with `requirements.txt` only, backend cold-start can take 2–3 minutes (first import of FastAPI, routes, engines). The workflow waits up to 90 attempts × 2s = 3 min for `/api/health/simple` before failing. If startup exceeds this, increase the loop count in the "Start backend server" step or investigate dependency/import bottlenecks. See DEFERRED_V1_2.md.

## Architecture

### 7-Step Workflow

| Step | Endpoint | Purpose |
|------|----------|---------|
| 1. Health | `GET /api/health/simple` | Preflight check |
| 2. Upload | `POST /api/audio/upload` | Audio file import |
| 3. Sync Synth | `POST /api/voice/synthesize` | Synchronous TTS |
| 4. Async Synth | `POST /api/batch/jobs` | Batch job creation |
| 5. Poll Job | `GET /api/batch/jobs/{id}` | Completion polling |
| 6. A/B Test | `POST /api/voice/synthesize/ab` | Engine comparison |
| 7. Eval | `GET /api/audio/{id}/analysis` | Quality metrics |

### Repro Packet Structure

```
artifacts/sentinel_runs/{run_id}/
├── summary.json         # Run status, invariants, timing
├── steps.jsonl          # Step-by-step execution log
├── requests/            # Request payloads
│   ├── health.json
│   ├── upload.json
│   └── ...
├── responses/           # Response payloads
│   ├── health.json
│   ├── upload.json
│   └── ...
└── outputs/             # Generated audio files
```

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `SENTINEL_API_BASE` | `http://127.0.0.1:8000` | API base URL |
| `SENTINEL_HEALTH_TIMEOUT` | `5.0` | Health check timeout (seconds) |
| `SENTINEL_UPLOAD_TIMEOUT` | `30.0` | Upload timeout (seconds) |
| `SENTINEL_SYNTH_TIMEOUT` | `120.0` | Synthesis timeout (seconds) |
| `SENTINEL_POLL_TIMEOUT` | `180.0` | Job polling timeout (seconds) |
| `SENTINEL_POLL_INTERVAL` | `2.0` | Polling interval (seconds) |
| `SENTINEL_RETRY_COUNT` | `3` | Maximum retry attempts |
| `SENTINEL_CIRCUIT_BREAKER_ENABLED` | `true` | Enable circuit breaker |
| `SENTINEL_CB_FAILURE_THRESHOLD` | `5` | Failures before opening |
| `VOICESTUDIO_KEEP_ARTIFACTS` | `false` | Keep test artifacts |

### Configuration Module

```python
from scripts.proof_runs.config import get_sentinel_config

config = get_sentinel_config()
print(config.api_base)
print(config.timeouts.health)
```

## Writing Sentinel Tests

### Test Structure

```python
import pytest
from scripts.proof_runs.sentinel_audio_workflow import SentinelRunner, StepStatus

@pytest.mark.smoke
class TestMyFeature:
    @pytest.mark.asyncio
    async def test_my_step(self, tmp_path):
        runner = SentinelRunner(
            artifacts_dir=tmp_path / "artifacts",
            enable_circuit_breaker=False,  # Disable for unit tests
        )
        async with runner:
            result = await runner._step_health()
        
        assert result.status == StepStatus.PASSED
```

### Test Markers

| Marker | Description |
|--------|-------------|
| `@pytest.mark.sentinel` | All sentinel tests |
| `@pytest.mark.smoke` | Quick validation tests |
| `@pytest.mark.backend_required` | Requires running backend |
| `@pytest.mark.slow` | Long-running tests |

### Mocking HTTP Responses

```python
from unittest.mock import AsyncMock, patch, MagicMock

@pytest.mark.asyncio
async def test_with_mock(tmp_path):
    mock_response = MagicMock()
    mock_response.status_code = 200
    mock_response.json.return_value = {"status": "healthy", ...}
    
    with patch.object(httpx.AsyncClient, 'request', new_callable=AsyncMock) as mock_request:
        mock_request.return_value = mock_response
        
        runner = SentinelRunner(...)
        async with runner:
            result = await runner._step_health()
        
        assert result.status == StepStatus.PASSED
```

## JSON Schema Contracts

### Location

Schemas are stored in `tests/sentinel/contracts/`:

```
tests/sentinel/contracts/
├── health_response.schema.json
├── upload_response.schema.json
├── tts_request.schema.json
├── tts_response.schema.json
├── job_response.schema.json
└── ab_summary_response.schema.json
```

### Schema Format

Schemas follow JSON Schema Draft-07:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "$id": "health_response.schema.json",
  "title": "HealthResponse",
  "type": "object",
  "required": ["status", "timestamp", "uptime_seconds", "version"],
  "properties": {
    "status": {
      "type": "string",
      "enum": ["healthy", "degraded", "unhealthy"]
    }
  }
}
```

### Adding a New Schema

1. Create the schema file in `tests/sentinel/contracts/`
2. Name it `{name}.schema.json`
3. Update the test to validate against the schema
4. Update this guide and `SENTINEL_CONTRACT_SCHEMAS.md`

### Validating Manually

```python
from scripts.proof_runs.sentinel_audio_workflow import SchemaValidator

validator = SchemaValidator()
is_valid, errors = validator.validate("health_response", {"status": "healthy", ...})
print(f"Valid: {is_valid}, Errors: {errors}")
```

## Debugging with Repro Packets

### Finding Artifacts

```bash
# List recent runs
ls -lt artifacts/sentinel_runs/

# Read summary
cat artifacts/sentinel_runs/20260212-143022-abc12345/summary.json | python -m json.tool
```

### Analyzing Failures

1. Open `summary.json` to see overall status and invariants
2. Check `steps.jsonl` for the failing step
3. Compare `requests/{step}.json` and `responses/{step}.json`
4. Use correlation ID to trace in backend logs

### Reproducing Issues

```bash
# Re-run with same fixture
python scripts/proof_runs/sentinel_audio_workflow.py \
    --api-base http://localhost:8000 \
    --fixture fixtures/audio/sentinel_16k_mono.wav \
    -v
```

## Resilience Features

### Retry Logic

The runner automatically retries transient failures:

- **Retryable Status Codes**: 429, 500, 502, 503, 504
- **Retryable Exceptions**: Timeout, ConnectError, NetworkError
- **Strategy**: Exponential backoff with jitter (1s → 2s → 4s)

### Circuit Breaker

Prevents cascading failures:

- **Threshold**: Opens after 5 consecutive failures
- **Recovery**: Tests recovery after 30s timeout
- **States**: CLOSED → OPEN → HALF_OPEN → CLOSED

### Correlation IDs

Every request includes `X-Correlation-ID` header for tracing:

```python
# Access correlation ID
runner = SentinelRunner()
print(runner._correlation_id)  # UUID for this run
```

## Extending the Sentinel

### Adding a New Step

1. Add the step method in `sentinel_audio_workflow.py`:

```python
async def _step_new_feature(self) -> StepResult:
    return await self._execute_step(
        step_name="new_feature",
        step_index=8,
        method="POST",
        endpoint="/api/new/endpoint",
        schema_name="new_feature_response",
        expected_status=200,
        json={"param": "value"},
    )
```

2. Add to the `run()` method:

```python
self.steps.append(await self._step_new_feature())
```

3. Create the JSON schema in `tests/sentinel/contracts/`
4. Add tests in `test_sentinel_audio_workflow.py`
5. Update documentation

### Adding New Schemas

1. Create schema file based on Pydantic model
2. Add to `STEP_TIMEOUTS` if needed
3. Reference in the step's `schema_name` parameter
4. Update `SENTINEL_CONTRACT_SCHEMAS.md`

## Troubleshooting

### Backend Not Available

```
pytest.skip: Backend server not available
```

**Solution**: Start the backend server:

```bash
cd backend && python -m uvicorn api.main:app --port 8000
```

### Schema Validation Failure

```
StepStatus.FAILED: Schema validation failed
```

**Solution**: 
1. Check the response in `responses/{step}.json`
2. Compare against `tests/sentinel/contracts/{step}.schema.json`
3. Update schema if API contract changed

### Timeout Errors

```
Request timed out after 30s
```

**Solution**:
1. Increase timeout via environment variable
2. Check backend performance
3. Verify network connectivity

### Circuit Breaker Open

```
Circuit breaker open for /api/health/simple
```

**Solution**:
1. Wait for recovery timeout (30s default)
2. Fix underlying backend issue
3. Disable circuit breaker for testing: `enable_circuit_breaker=False`

## Related Documentation

- [ADR-035: Sentinel Deterministic Workflow](../architecture/decisions/ADR-035-sentinel-deterministic-workflow.md)
- [SENTINEL_CONTRACT_SCHEMAS.md](../REFERENCE/SENTINEL_CONTRACT_SCHEMAS.md)
- [DETERMINISTIC_SENTINEL_IMPLEMENTATION_PLAN.md](../design/DETERMINISTIC_SENTINEL_IMPLEMENTATION_PLAN.md)
