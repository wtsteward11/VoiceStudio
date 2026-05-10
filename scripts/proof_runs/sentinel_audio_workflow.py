#!/usr/bin/env python3
"""
Deterministic Sentinel Audio Workflow Runner

This script exercises the complete VoiceStudio pipeline from audio import
through synthesis to evaluation, generating reproducible proof artifacts.

Phase 1 Implementation - Sentinel Infrastructure
Based on: docs/design/DETERMINISTIC_SENTINEL_IMPLEMENTATION_PLAN.md

Usage:
    python scripts/proof_runs/sentinel_audio_workflow.py [--api-base URL]

    # Run with pytest
    pytest tests/sentinel/test_sentinel_audio_workflow.py -v
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import logging
import random
import sys
import time
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any

import httpx
import jsonschema

# Configure structured logging with correlation ID support
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: [%(correlation_id)s] %(message)s",
    datefmt="%Y-%m-%dT%H:%M:%S",
)

# Custom filter to ensure correlation_id is always present
class CorrelationIdFilter(logging.Filter):
    """Add correlation_id to all log records."""

    def __init__(self, default_id: str = "no-cid"):
        super().__init__()
        self.default_id = default_id
        self._correlation_id: str | None = None

    def set_correlation_id(self, correlation_id: str) -> None:
        """Set the correlation ID for subsequent log messages."""
        self._correlation_id = correlation_id

    def filter(self, record: logging.LogRecord) -> bool:
        """Add correlation_id to log record if not present."""
        if not hasattr(record, "correlation_id") or record.correlation_id is None:
            record.correlation_id = self._correlation_id or self.default_id
        return True


# Create logger with correlation filter
logger = logging.getLogger("sentinel_runner")
correlation_filter = CorrelationIdFilter()
logger.addFilter(correlation_filter)
# Root handlers use %(correlation_id)s in format; ensure third-party loggers get the field.
for _handler in logging.root.handlers:
    _handler.addFilter(correlation_filter)


def structured_log(
    level: str,
    event: str,
    correlation_id: str | None = None,
    **data: Any,
) -> None:
    """
    Create a structured log entry with correlation ID and extra data.

    Args:
        level: Log level (debug, info, warning, error, critical).
        event: Event name/type for structured logging.
        correlation_id: Optional correlation ID override.
        **data: Additional structured data to include.
    """
    log_data = {
        "event": event,
        "correlation_id": correlation_id or correlation_filter._correlation_id or "no-cid",
        **data,
    }

    # Format message with key data inline
    data_str = " ".join(f"{k}={v}" for k, v in data.items() if v is not None)
    message = f"{event}" + (f" | {data_str}" if data_str else "")

    log_method = getattr(logger, level.lower(), logger.info)
    log_method(message, extra=log_data)


# -----------------------------------------------------------------------------
# Constants and Configuration
# -----------------------------------------------------------------------------

DEFAULT_API_BASE = "http://127.0.0.1:8000"
SENTINEL_FIXTURE_PATH = Path("fixtures/audio/sentinel_16k_mono.wav")
ARTIFACTS_DIR = Path("artifacts/sentinel_runs")
CONTRACTS_DIR = Path("tests/sentinel/contracts")

# Timeout configuration per step (seconds)
STEP_TIMEOUTS = {
    "health": 5,
    "upload": 30,
    "sync_synth": 120,
    "async_synth": 30,
    "poll_job": 180,
    "ab_test": 180,
    "eval": 60,
}

# Retry configuration
RETRY_CONFIG = {
    "max_retries": 3,
    "initial_delay": 1.0,
    "max_delay": 10.0,
    "jitter_factor": 0.2,
}

# Circuit breaker configuration
CIRCUIT_BREAKER_CONFIG = {
    "failure_threshold": 5,      # Number of failures before opening
    "success_threshold": 2,      # Successes needed to close from half-open
    "recovery_timeout": 30.0,    # Seconds before attempting recovery
}

# HTTP status codes that are retryable
RETRYABLE_STATUS_CODES = {429, 500, 502, 503, 504}

# Exception types that are retryable
RETRYABLE_EXCEPTIONS = (
    httpx.TimeoutException,
    httpx.ConnectError,
    httpx.ConnectTimeout,
    httpx.NetworkError,
)


class StepStatus(str, Enum):
    """Status of a workflow step."""
    PENDING = "pending"
    RUNNING = "running"
    PASSED = "passed"
    FAILED = "failed"
    SKIPPED = "skipped"


@dataclass
class StepResult:
    """Result of a single workflow step."""
    step_name: str
    step_index: int
    status: StepStatus
    started_at: str
    completed_at: str | None = None
    duration_ms: float | None = None
    request: dict[str, Any] | None = None
    response: dict[str, Any] | None = None
    response_hash: str | None = None
    error: str | None = None
    validation_errors: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        # Compute response_hash from response if not already set
        response_hash = self.response_hash
        if response_hash is None and self.response is not None:
            response_hash = self._hash_dict(self.response)

        return {
            "step_name": self.step_name,
            "step_index": self.step_index,
            "status": self.status.value,
            "started_at": self.started_at,
            "completed_at": self.completed_at,
            "duration_ms": self.duration_ms,
            "request_hash": self._hash_dict(self.request) if self.request else None,
            "response_hash": response_hash,
            "error": self.error,
            "validation_errors": self.validation_errors,
        }

    @staticmethod
    def _hash_dict(d: dict[str, Any]) -> str:
        """Compute SHA256 hash of a dictionary."""
        return hashlib.sha256(
            json.dumps(d, sort_keys=True, default=str).encode()
        ).hexdigest()[:16]


@dataclass
class ReproPacket:
    """Reproducible proof packet from a sentinel run."""
    run_id: str
    started_at: str
    completed_at: str
    duration_ms: float
    api_base: str
    fixture_hash: str
    overall_status: str
    passed_steps: int
    failed_steps: int
    skipped_steps: int
    total_steps: int
    steps: list[StepResult]
    invariants: dict[str, bool]
    artifacts: list[str]

    def to_summary(self) -> dict[str, Any]:
        """Generate summary.json content."""
        return {
            "run_id": self.run_id,
            "started_at": self.started_at,
            "completed_at": self.completed_at,
            "duration_ms": self.duration_ms,
            "api_base": self.api_base,
            "fixture_hash": self.fixture_hash,
            "overall_status": self.overall_status,
            "step_counts": {
                "passed": self.passed_steps,
                "failed": self.failed_steps,
                "skipped": self.skipped_steps,
                "total": self.total_steps,
            },
            "invariants": self.invariants,
            "artifacts": self.artifacts,
        }


# -----------------------------------------------------------------------------
# Schema Validation
# -----------------------------------------------------------------------------

class SchemaValidator:
    """Validates API responses against JSON schemas."""

    def __init__(self, contracts_dir: Path = CONTRACTS_DIR):
        self.contracts_dir = contracts_dir
        self._schemas: dict[str, dict] = {}
        self._load_schemas()

    def _load_schemas(self) -> None:
        """Load all JSON schemas from contracts directory."""
        if not self.contracts_dir.exists():
            logger.warning(f"Contracts directory not found: {self.contracts_dir}")
            return

        for schema_file in self.contracts_dir.glob("*.schema.json"):
            try:
                with open(schema_file) as f:
                    schema = json.load(f)
                    schema_name = schema_file.stem.replace(".schema", "")
                    self._schemas[schema_name] = schema
                    logger.debug(f"Loaded schema: {schema_name}")
            except Exception as e:
                logger.error(f"Failed to load schema {schema_file}: {e}")

    def validate(
        self,
        schema_name: str,
        data: dict[str, Any]
    ) -> tuple[bool, list[str]]:
        """
        Validate data against a named schema.

        Returns:
            Tuple of (is_valid, list_of_errors)
        """
        if schema_name not in self._schemas:
            return False, [f"Schema not found: {schema_name}"]

        errors = []
        try:
            jsonschema.validate(data, self._schemas[schema_name])
            return True, []
        except jsonschema.ValidationError as e:
            # Build detailed field path
            path = ".".join(str(p) for p in e.absolute_path) if e.absolute_path else "root"

            # Build context about the error
            error_context = []
            if e.validator:
                error_context.append(f"constraint='{e.validator}'")
            if e.validator_value is not None and e.validator != "required":
                error_context.append(f"expected={e.validator_value!r}")
            if e.instance is not None and len(str(e.instance)) < 50:
                error_context.append(f"got={e.instance!r}")

            context_str = f" ({', '.join(error_context)})" if error_context else ""
            errors.append(f"Field '{path}': {e.message}{context_str}")

        except jsonschema.SchemaError as e:
            errors.append(f"Schema error in '{schema_name}': {e.message}")

        return False, errors


# -----------------------------------------------------------------------------
# Sentinel Runner
# -----------------------------------------------------------------------------

class SimpleCircuitBreaker:
    """
    Lightweight circuit breaker for sentinel workflow.

    Provides failure isolation without requiring the full backend circuit breaker.
    States: CLOSED (normal) -> OPEN (failing fast) -> HALF_OPEN (testing)
    """

    def __init__(
        self,
        name: str = "sentinel",
        failure_threshold: int = 5,
        success_threshold: int = 2,
        recovery_timeout: float = 30.0,
    ):
        self.name = name
        self.failure_threshold = failure_threshold
        self.success_threshold = success_threshold
        self.recovery_timeout = recovery_timeout

        self._state = "closed"
        self._failure_count = 0
        self._success_count = 0
        self._last_failure_time: float | None = None
        self._total_blocked = 0

    def allow_request(self) -> bool:
        """Check if a request should be allowed through."""
        if self._state == "closed":
            return True
        elif self._state == "open":
            # Check if recovery timeout has elapsed
            if self._last_failure_time is not None:
                elapsed = time.perf_counter() - self._last_failure_time
                if elapsed >= self.recovery_timeout:
                    self._state = "half_open"
                    self._success_count = 0
                    structured_log(
                        "info",
                        "sentinel.circuit_breaker.half_open",
                        breaker=self.name,
                        elapsed=round(elapsed, 2),
                    )
                    return True
            self._total_blocked += 1
            return False
        else:  # half_open
            return True

    def record_success(self) -> None:
        """Record a successful request."""
        if self._state == "half_open":
            self._success_count += 1
            if self._success_count >= self.success_threshold:
                self._state = "closed"
                self._failure_count = 0
                structured_log(
                    "info",
                    "sentinel.circuit_breaker.closed",
                    breaker=self.name,
                    reason="recovery_complete",
                )
        elif self._state == "closed":
            # Reset failure count on success
            self._failure_count = max(0, self._failure_count - 1)

    def record_failure(self) -> None:
        """Record a failed request."""
        self._failure_count += 1
        self._last_failure_time = time.perf_counter()

        if self._state == "half_open":
            # Immediately reopen on failure during half-open
            self._state = "open"
            structured_log(
                "warning",
                "sentinel.circuit_breaker.reopened",
                breaker=self.name,
                reason="half_open_failure",
            )
        elif self._state == "closed" and self._failure_count >= self.failure_threshold:
            self._state = "open"
            structured_log(
                "warning",
                "sentinel.circuit_breaker.opened",
                breaker=self.name,
                failure_count=self._failure_count,
                threshold=self.failure_threshold,
            )

    @property
    def state(self) -> str:
        """Get current circuit breaker state."""
        return self._state

    @property
    def stats(self) -> dict[str, Any]:
        """Get circuit breaker statistics."""
        return {
            "name": self.name,
            "state": self._state,
            "failure_count": self._failure_count,
            "success_count": self._success_count,
            "total_blocked": self._total_blocked,
        }


class SentinelRunner:
    """
    Deterministic sentinel audio workflow runner.

    Executes a predefined sequence of API calls against the VoiceStudio
    backend and generates reproducible proof artifacts.

    Usage:
        async with SentinelRunner() as runner:
            packet = await runner.run()
    """

    def __init__(
        self,
        api_base: str = DEFAULT_API_BASE,
        fixture_path: Path = SENTINEL_FIXTURE_PATH,
        artifacts_dir: Path = ARTIFACTS_DIR,
        enable_circuit_breaker: bool = True,
    ):
        self.api_base = api_base.rstrip("/")
        self.fixture_path = Path(fixture_path)
        self.artifacts_dir = Path(artifacts_dir)
        self.run_id = self._generate_run_id()
        self.run_dir = self.artifacts_dir / self.run_id
        self.steps: list[StepResult] = []
        self.validator = SchemaValidator()
        self._correlation_id = str(uuid.uuid4())

        # HTTP client for connection pooling
        self._client: httpx.AsyncClient | None = None

        # Circuit breaker for failure isolation
        self._circuit_breaker: SimpleCircuitBreaker | None = None
        if enable_circuit_breaker:
            self._circuit_breaker = SimpleCircuitBreaker(
                name="sentinel_api",
                **CIRCUIT_BREAKER_CONFIG,
            )

        # State from previous steps
        self._upload_id: str | None = None
        self._upload_path: str | None = None
        self._sync_audio_id: str | None = None
        self._async_job_id: str | None = None
        self._async_audio_id: str | None = None
        self._ab_test_id: str | None = None

    async def __aenter__(self) -> SentinelRunner:
        """Enter async context - create HTTP client with connection pooling."""
        # Set correlation ID for structured logging
        correlation_filter.set_correlation_id(self._correlation_id)

        structured_log(
            "info",
            "sentinel.runner.init",
            correlation_id=self._correlation_id,
            run_id=self.run_id,
            api_base=self.api_base,
        )

        self._client = httpx.AsyncClient(
            timeout=httpx.Timeout(30.0),
            limits=httpx.Limits(
                max_connections=10,
                max_keepalive_connections=5,
                keepalive_expiry=30.0,
            ),
            follow_redirects=True,
        )
        return self

    async def __aexit__(
        self,
        exc_type: type | None,
        exc_val: BaseException | None,
        exc_tb: Any | None,
    ) -> None:
        """Exit async context - close HTTP client."""
        if self._client:
            await self._client.aclose()
            self._client = None

    @staticmethod
    def _generate_run_id() -> str:
        """Generate a deterministic run ID based on UTC timestamp."""
        now = datetime.now(timezone.utc)
        timestamp = now.strftime("%Y%m%d-%H%M%S")
        hash_suffix = hashlib.sha256(
            now.isoformat().encode()
        ).hexdigest()[:8]
        return f"{timestamp}-{hash_suffix}"

    def _compute_file_hash(self, path: Path) -> str:
        """Compute SHA256 hash of a file."""
        if not path.exists():
            return "file_not_found"

        sha256 = hashlib.sha256()
        with open(path, "rb") as f:
            for chunk in iter(lambda: f.read(8192), b""):
                sha256.update(chunk)
        return sha256.hexdigest()

    def _now_iso(self) -> str:
        """Get current UTC time in ISO format."""
        return datetime.now(timezone.utc).isoformat()

    async def _make_request(
        self,
        method: str,
        endpoint: str,
        timeout: float,
        retry: bool = True,
        **kwargs,
    ) -> tuple[int, dict[str, Any] | None, str | None]:
        """
        Make an HTTP request with timeout, error handling, and retry logic.

        Args:
            method: HTTP method
            endpoint: API endpoint
            timeout: Request timeout in seconds
            retry: Whether to retry on transient failures
            **kwargs: Additional request arguments

        Returns:
            Tuple of (status_code, response_json, error_message)
        """
        # Check circuit breaker before making request
        if self._circuit_breaker and not self._circuit_breaker.allow_request():
            error_msg = f"Circuit breaker open for {endpoint}"
            structured_log(
                "warning",
                "sentinel.http.circuit_breaker_blocked",
                endpoint=endpoint,
                breaker_state=self._circuit_breaker.state,
            )
            return 0, None, error_msg

        url = f"{self.api_base}{endpoint}"
        headers = kwargs.pop("headers", {})
        headers["X-Correlation-ID"] = self._correlation_id

        max_retries = RETRY_CONFIG["max_retries"] if retry else 1
        delay = RETRY_CONFIG["initial_delay"]
        max_delay = RETRY_CONFIG["max_delay"]
        jitter_factor = RETRY_CONFIG["jitter_factor"]

        last_error: str | None = None
        last_status_code: int = 0

        for attempt in range(max_retries):
            try:
                # Use pooled client if available, otherwise create temporary one
                if self._client:
                    response = await self._client.request(
                        method, url, headers=headers, timeout=timeout, **kwargs
                    )
                else:
                    # Fallback for when not using context manager
                    async with httpx.AsyncClient(timeout=timeout) as temp_client:
                        response = await temp_client.request(
                            method, url, headers=headers, **kwargs
                        )

                # Parse response
                try:
                    data = response.json()
                except (json.JSONDecodeError, ValueError):
                    data = {"raw_text": response.text[:1000]}

                # Check for retryable status codes
                if response.status_code in RETRYABLE_STATUS_CODES and attempt < max_retries - 1:
                    last_status_code = response.status_code
                    last_error = f"Retryable status {response.status_code}"
                    structured_log(
                        "warning",
                        "sentinel.http.retry",
                        endpoint=endpoint,
                        status_code=response.status_code,
                        attempt=attempt + 1,
                        max_retries=max_retries,
                        reason="retryable_status",
                    )
                    jitter = random.uniform(0, delay * jitter_factor)
                    await asyncio.sleep(delay + jitter)
                    delay = min(delay * 2, max_delay)
                    continue

                # Record success with circuit breaker
                if self._circuit_breaker:
                    self._circuit_breaker.record_success()

                return response.status_code, data, None

            except httpx.TimeoutException as e:
                last_error = f"Request timed out after {timeout}s: {e!s}"
                last_status_code = 0
                if attempt < max_retries - 1:
                    structured_log(
                        "warning",
                        "sentinel.http.retry",
                        endpoint=endpoint,
                        attempt=attempt + 1,
                        max_retries=max_retries,
                        reason="timeout",
                        timeout=timeout,
                    )
                    jitter = random.uniform(0, delay * jitter_factor)
                    await asyncio.sleep(delay + jitter)
                    delay = min(delay * 2, max_delay)
                    continue

            except httpx.ConnectError as e:
                last_error = f"Connection error: {e!s}"
                last_status_code = 0
                if attempt < max_retries - 1:
                    structured_log(
                        "warning",
                        "sentinel.http.retry",
                        endpoint=endpoint,
                        attempt=attempt + 1,
                        max_retries=max_retries,
                        reason="connect_error",
                    )
                    jitter = random.uniform(0, delay * jitter_factor)
                    await asyncio.sleep(delay + jitter)
                    delay = min(delay * 2, max_delay)
                    continue

            except httpx.ConnectTimeout as e:
                last_error = f"Connection timeout: {e!s}"
                last_status_code = 0
                if attempt < max_retries - 1:
                    structured_log(
                        "warning",
                        "sentinel.http.retry",
                        endpoint=endpoint,
                        attempt=attempt + 1,
                        max_retries=max_retries,
                        reason="connect_timeout",
                    )
                    jitter = random.uniform(0, delay * jitter_factor)
                    await asyncio.sleep(delay + jitter)
                    delay = min(delay * 2, max_delay)
                    continue

            except httpx.NetworkError as e:
                last_error = f"Network error: {e!s}"
                last_status_code = 0
                if attempt < max_retries - 1:
                    structured_log(
                        "warning",
                        "sentinel.http.retry",
                        endpoint=endpoint,
                        attempt=attempt + 1,
                        max_retries=max_retries,
                        reason="network_error",
                    )
                    jitter = random.uniform(0, delay * jitter_factor)
                    await asyncio.sleep(delay + jitter)
                    delay = min(delay * 2, max_delay)
                    continue

            except httpx.HTTPStatusError as e:
                # Non-retryable HTTP errors (4xx client errors except 429)
                last_error = f"HTTP error {e.response.status_code}: {e!s}"
                last_status_code = e.response.status_code
                try:
                    data = e.response.json()
                except (json.JSONDecodeError, ValueError):
                    data = {"raw_text": e.response.text[:1000]}
                return last_status_code, data, last_error

            except httpx.ProxyError as e:
                last_error = f"Proxy error: {e!s}"
                last_status_code = 0
                # Proxy errors are typically not retryable
                break

            except httpx.DecodingError as e:
                last_error = f"Response decoding error: {e!s}"
                last_status_code = 0
                # Decoding errors are not retryable
                break

            except Exception as e:
                # Catch-all for unexpected errors
                error_type = type(e).__name__
                last_error = f"Unexpected {error_type}: {e!s}"
                last_status_code = 0
                structured_log(
                    "error",
                    "sentinel.http.unexpected_error",
                    endpoint=endpoint,
                    error_type=error_type,
                    error=str(e)[:500],
                )
                break

        # All retries exhausted - record failure with circuit breaker
        if self._circuit_breaker:
            self._circuit_breaker.record_failure()

        structured_log(
            "error",
            "sentinel.http.failed",
            endpoint=endpoint,
            attempts=max_retries,
            last_error=last_error[:500] if last_error else None,
            circuit_breaker_state=self._circuit_breaker.state if self._circuit_breaker else None,
        )
        return last_status_code, None, last_error

    def _hash_response(self, response: dict[str, Any] | None) -> str | None:
        """Compute deterministic hash of response for reproducibility."""
        if response is None:
            return None
        return hashlib.sha256(
            json.dumps(response, sort_keys=True, default=str).encode()
        ).hexdigest()

    async def _execute_step(
        self,
        step_name: str,
        step_index: int,
        method: str,
        endpoint: str,
        schema_name: str | None = None,
        expected_status: int = 200,
        **request_kwargs,
    ) -> StepResult:
        """Execute a single workflow step with validation."""
        timeout = STEP_TIMEOUTS.get(step_name, 30)
        started_at = self._now_iso()
        start_time = time.perf_counter()

        result = StepResult(
            step_name=step_name,
            step_index=step_index,
            status=StepStatus.RUNNING,
            started_at=started_at,
            request={
                "method": method,
                "endpoint": endpoint,
                "timeout": timeout,
                **{k: str(v)[:200] for k, v in request_kwargs.items()
                   if k not in ("files", "content")},
            },
        )

        structured_log(
            "info",
            "sentinel.step.start",
            step_index=step_index,
            step_name=step_name,
            method=method,
            endpoint=endpoint,
            timeout=timeout,
        )

        # Execute request
        status_code, response, error = await self._make_request(
            method, endpoint, timeout, **request_kwargs
        )

        end_time = time.perf_counter()
        result.completed_at = self._now_iso()
        result.duration_ms = (end_time - start_time) * 1000
        result.response = response
        result.response_hash = self._hash_response(response)

        # Check for errors
        if error:
            result.status = StepStatus.FAILED
            result.error = error
            structured_log(
                "error",
                "sentinel.step.failed",
                step_name=step_name,
                step_index=step_index,
                error=error,
                duration_ms=result.duration_ms,
            )
            return result

        # Check status code
        if status_code != expected_status:
            result.status = StepStatus.FAILED
            result.error = f"Expected status {expected_status}, got {status_code}"
            structured_log(
                "error",
                "sentinel.step.status_mismatch",
                step_name=step_name,
                step_index=step_index,
                expected_status=expected_status,
                actual_status=status_code,
                duration_ms=result.duration_ms,
            )
            return result

        # Validate schema
        if schema_name and response:
            is_valid, validation_errors = self.validator.validate(
                schema_name, response
            )
            result.validation_errors = validation_errors
            if not is_valid:
                result.status = StepStatus.FAILED
                result.error = f"Schema validation failed: {validation_errors}"
                structured_log(
                    "error",
                    "sentinel.step.schema_validation_failed",
                    step_name=step_name,
                    step_index=step_index,
                    schema_name=schema_name,
                    validation_errors=str(validation_errors)[:500],
                )
                return result

        result.status = StepStatus.PASSED
        structured_log(
            "info",
            "sentinel.step.passed",
            step_name=step_name,
            step_index=step_index,
            duration_ms=round(result.duration_ms, 2),
        )
        return result

    # -------------------------------------------------------------------------
    # Workflow Steps
    # -------------------------------------------------------------------------

    async def _step_health(self) -> StepResult:
        """Step 1: Health check / preflight."""
        return await self._execute_step(
            step_name="health",
            step_index=1,
            method="GET",
            endpoint="/api/monitoring/health",
            schema_name="health_response",
            expected_status=200,
        )

    async def _step_upload(self) -> StepResult:
        """Step 2: Upload sentinel audio file."""
        if not self.fixture_path.exists():
            # Create a minimal placeholder result
            result = StepResult(
                step_name="upload",
                step_index=2,
                status=StepStatus.SKIPPED,
                started_at=self._now_iso(),
                error=f"Fixture not found: {self.fixture_path}",
            )
            return result

        with open(self.fixture_path, "rb") as f:
            file_content = f.read()

        result = await self._execute_step(
            step_name="upload",
            step_index=2,
            method="POST",
            endpoint="/api/audio/upload",
            schema_name="upload_response",
            expected_status=201,
            files={"file": ("sentinel_16k_mono.wav", file_content, "audio/wav")},
        )

        # Extract state for subsequent steps
        if result.status == StepStatus.PASSED and result.response:
            self._upload_id = result.response.get("id")
            self._upload_path = result.response.get("path")

        return result

    async def _step_sync_synth(self) -> StepResult:
        """Step 3: Synchronous synthesis."""
        if not self._upload_id:
            return StepResult(
                step_name="sync_synth",
                step_index=3,
                status=StepStatus.SKIPPED,
                started_at=self._now_iso(),
                error="No upload ID from previous step",
            )

        request_body = {
            "profile_id": "sentinel_default",
            "text": "This is a sentinel workflow test.",
            "language": "en",
            "enhance_quality": False,
        }

        result = await self._execute_step(
            step_name="sync_synth",
            step_index=3,
            method="POST",
            endpoint="/api/voice/synthesize",
            schema_name="tts_response",
            expected_status=200,
            json=request_body,
        )

        if result.status == StepStatus.PASSED and result.response:
            self._sync_audio_id = result.response.get("audio_id")

        return result

    async def _step_async_synth(self) -> StepResult:
        """Step 4: Async synthesis job creation."""
        # Use batch endpoint for async job
        request_body = {
            "name": f"sentinel_batch_{self.run_id}",
            "items": [
                {
                    "id": "sentinel_item_1",
                    "text": "Async sentinel test one.",
                    "profile_id": "sentinel_default",
                },
            ],
        }

        result = await self._execute_step(
            step_name="async_synth",
            step_index=4,
            method="POST",
            endpoint="/api/batch/jobs",
            expected_status=201,
            json=request_body,
        )

        if result.status == StepStatus.PASSED and result.response:
            self._async_job_id = result.response.get("id")

        return result

    async def _step_poll_job(self) -> StepResult:
        """Step 5: Poll async job until completion."""
        if not self._async_job_id:
            return StepResult(
                step_name="poll_job",
                step_index=5,
                status=StepStatus.SKIPPED,
                started_at=self._now_iso(),
                error="No async job ID from previous step",
            )

        started_at = self._now_iso()
        start_time = time.perf_counter()
        timeout = STEP_TIMEOUTS["poll_job"]
        poll_interval = 2.0
        consecutive_errors = 0
        max_consecutive_errors = 5

        while True:
            elapsed = time.perf_counter() - start_time
            if elapsed > timeout:
                return StepResult(
                    step_name="poll_job",
                    step_index=5,
                    status=StepStatus.FAILED,
                    started_at=started_at,
                    completed_at=self._now_iso(),
                    duration_ms=elapsed * 1000,
                    error=f"Job did not complete within {timeout}s",
                )

            status_code, response, error = await self._make_request(
                "GET",
                f"/api/batch/jobs/{self._async_job_id}",
                timeout=10,
                retry=False,  # Handle retries manually for polling
            )

            # Handle request errors
            if error:
                consecutive_errors += 1
                structured_log(
                    "warning",
                    "sentinel.poll.error",
                    job_id=self._async_job_id,
                    consecutive_errors=consecutive_errors,
                    max_consecutive_errors=max_consecutive_errors,
                    error=error[:200] if error else None,
                )
                if consecutive_errors >= max_consecutive_errors:
                    return StepResult(
                        step_name="poll_job",
                        step_index=5,
                        status=StepStatus.FAILED,
                        started_at=started_at,
                        completed_at=self._now_iso(),
                        duration_ms=elapsed * 1000,
                        error=f"Too many consecutive errors: {error}",
                    )
                await asyncio.sleep(poll_interval)
                continue

            # Validate status code before accessing response
            if status_code != 200:
                if status_code == 404:
                    return StepResult(
                        step_name="poll_job",
                        step_index=5,
                        status=StepStatus.FAILED,
                        started_at=started_at,
                        completed_at=self._now_iso(),
                        duration_ms=elapsed * 1000,
                        response=response,
                        error=f"Job not found: {self._async_job_id}",
                    )
                elif status_code in RETRYABLE_STATUS_CODES:
                    consecutive_errors += 1
                    structured_log(
                        "warning",
                        "sentinel.poll.retry",
                        job_id=self._async_job_id,
                        status_code=status_code,
                        consecutive_errors=consecutive_errors,
                        max_consecutive_errors=max_consecutive_errors,
                    )
                    if consecutive_errors >= max_consecutive_errors:
                        return StepResult(
                            step_name="poll_job",
                            step_index=5,
                            status=StepStatus.FAILED,
                            started_at=started_at,
                            completed_at=self._now_iso(),
                            duration_ms=elapsed * 1000,
                            response=response,
                            error=f"Too many retryable errors: last status {status_code}",
                        )
                    await asyncio.sleep(poll_interval)
                    continue
                else:
                    return StepResult(
                        step_name="poll_job",
                        step_index=5,
                        status=StepStatus.FAILED,
                        started_at=started_at,
                        completed_at=self._now_iso(),
                        duration_ms=elapsed * 1000,
                        response=response,
                        error=f"Unexpected status code: {status_code}",
                    )

            # Reset error counter on success
            consecutive_errors = 0

            if response:
                status = response.get("status", "").lower()
                if status in ("completed", "complete"):
                    self._async_audio_id = response.get("result_id")
                    return StepResult(
                        step_name="poll_job",
                        step_index=5,
                        status=StepStatus.PASSED,
                        started_at=started_at,
                        completed_at=self._now_iso(),
                        duration_ms=elapsed * 1000,
                        response=response,
                        response_hash=self._hash_response(response),
                    )
                elif status in ("failed", "error", "cancelled"):
                    return StepResult(
                        step_name="poll_job",
                        step_index=5,
                        status=StepStatus.FAILED,
                        started_at=started_at,
                        completed_at=self._now_iso(),
                        duration_ms=elapsed * 1000,
                        response=response,
                        error=f"Job ended with status: {status}",
                    )

            await asyncio.sleep(poll_interval)

    async def _step_ab_test(self) -> StepResult:
        """Step 6: A/B synthesis comparison."""
        request_body = {
            "profile_id": "sentinel_default",
            "text": "A/B comparison test.",
            "language": "en",
            "engine_a": "piper",
            "engine_b": "xtts_v2",
            "enhance_quality_a": False,
            "enhance_quality_b": True,
        }

        result = await self._execute_step(
            step_name="ab_test",
            step_index=6,
            method="POST",
            endpoint="/api/voice/synthesize/ab",
            schema_name="ab_summary_response",
            expected_status=200,
            json=request_body,
        )

        if result.status == StepStatus.PASSED and result.response:
            self._ab_test_id = result.response.get("test_id")

        return result

    async def _step_eval(self) -> StepResult:
        """Step 7: Evaluation / quality metrics."""
        if not self._sync_audio_id:
            return StepResult(
                step_name="eval",
                step_index=7,
                status=StepStatus.SKIPPED,
                started_at=self._now_iso(),
                error="No audio ID for evaluation",
            )

        result = await self._execute_step(
            step_name="eval",
            step_index=7,
            method="GET",
            endpoint=f"/api/audio/{self._sync_audio_id}/analysis",
            expected_status=200,
        )

        return result

    # -------------------------------------------------------------------------
    # Main Run
    # -------------------------------------------------------------------------

    async def run(self) -> ReproPacket:
        """Execute the complete sentinel workflow."""
        structured_log(
            "info",
            "sentinel.workflow.start",
            run_id=self.run_id,
            api_base=self.api_base,
            fixture=str(self.fixture_path),
        )

        started_at = self._now_iso()
        start_time = time.perf_counter()

        # Compute fixture hash for reproducibility
        fixture_hash = self._compute_file_hash(self.fixture_path)

        # Execute steps
        self.steps.append(await self._step_health())
        self.steps.append(await self._step_upload())
        self.steps.append(await self._step_sync_synth())
        self.steps.append(await self._step_async_synth())
        self.steps.append(await self._step_poll_job())
        self.steps.append(await self._step_ab_test())
        self.steps.append(await self._step_eval())

        end_time = time.perf_counter()
        completed_at = self._now_iso()
        duration_ms = (end_time - start_time) * 1000

        # Compute statistics
        passed = sum(1 for s in self.steps if s.status == StepStatus.PASSED)
        failed = sum(1 for s in self.steps if s.status == StepStatus.FAILED)
        skipped = sum(1 for s in self.steps if s.status == StepStatus.SKIPPED)

        # Determine overall status
        if failed > 0:
            overall_status = "failed"
        elif skipped == len(self.steps):
            overall_status = "skipped"
        elif passed == len(self.steps):
            overall_status = "passed"
        else:
            overall_status = "partial"

        # Compute invariants (key assertions)
        invariants = {
            "health_check_passed": self.steps[0].status == StepStatus.PASSED,
            "upload_succeeded": self.steps[1].status == StepStatus.PASSED,
            "sync_synth_succeeded": self.steps[2].status == StepStatus.PASSED,
            "all_schemas_valid": all(
                len(s.validation_errors) == 0 for s in self.steps
            ),
            "no_timeouts": all(
                "timed out" not in (s.error or "") for s in self.steps
            ),
        }

        # Create repro packet
        packet = ReproPacket(
            run_id=self.run_id,
            started_at=started_at,
            completed_at=completed_at,
            duration_ms=duration_ms,
            api_base=self.api_base,
            fixture_hash=fixture_hash,
            overall_status=overall_status,
            passed_steps=passed,
            failed_steps=failed,
            skipped_steps=skipped,
            total_steps=len(self.steps),
            steps=self.steps,
            invariants=invariants,
            artifacts=[],
        )

        # Save artifacts
        await self._save_artifacts(packet)

        structured_log(
            "info",
            "sentinel.workflow.complete",
            run_id=self.run_id,
            overall_status=overall_status,
            duration_ms=round(duration_ms, 2),
            passed_steps=passed,
            failed_steps=failed,
            skipped_steps=skipped,
            total_steps=len(self.steps),
        )

        return packet

    async def _save_artifacts(self, packet: ReproPacket) -> None:
        """Save all artifacts to the run directory."""
        self.run_dir.mkdir(parents=True, exist_ok=True)

        # Create subdirectories
        requests_dir = self.run_dir / "requests"
        responses_dir = self.run_dir / "responses"
        outputs_dir = self.run_dir / "outputs"

        requests_dir.mkdir(exist_ok=True)
        responses_dir.mkdir(exist_ok=True)
        outputs_dir.mkdir(exist_ok=True)

        # Save summary.json
        summary_path = self.run_dir / "summary.json"
        with open(summary_path, "w") as f:
            json.dump(packet.to_summary(), f, indent=2, default=str)
        packet.artifacts.append(str(summary_path))

        # Save steps.jsonl
        steps_path = self.run_dir / "steps.jsonl"
        with open(steps_path, "w") as f:
            for step in self.steps:
                f.write(json.dumps(step.to_dict(), default=str) + "\n")
        packet.artifacts.append(str(steps_path))

        # Save individual requests and responses
        for step in self.steps:
            if step.request:
                req_path = requests_dir / f"{step.step_name}.json"
                with open(req_path, "w") as f:
                    json.dump(step.request, f, indent=2, default=str)
                packet.artifacts.append(str(req_path))

            if step.response:
                resp_path = responses_dir / f"{step.step_name}.json"
                with open(resp_path, "w") as f:
                    json.dump(step.response, f, indent=2, default=str)
                packet.artifacts.append(str(resp_path))

        logger.info(f"Artifacts saved to: {self.run_dir}")


# -----------------------------------------------------------------------------
# CLI Entry Point
# -----------------------------------------------------------------------------

async def main():
    """CLI entry point."""
    parser = argparse.ArgumentParser(
        description="Deterministic Sentinel Audio Workflow Runner"
    )
    parser.add_argument(
        "--api-base",
        default=DEFAULT_API_BASE,
        help=f"API base URL (default: {DEFAULT_API_BASE})",
    )
    parser.add_argument(
        "--fixture",
        default=str(SENTINEL_FIXTURE_PATH),
        help=f"Path to sentinel audio fixture (default: {SENTINEL_FIXTURE_PATH})",
    )
    parser.add_argument(
        "--output-dir",
        default=str(ARTIFACTS_DIR),
        help=f"Output directory for artifacts (default: {ARTIFACTS_DIR})",
    )
    parser.add_argument(
        "-v", "--verbose",
        action="store_true",
        help="Enable verbose logging",
    )

    args = parser.parse_args()

    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)

    async with SentinelRunner(
        api_base=args.api_base,
        fixture_path=Path(args.fixture),
        artifacts_dir=Path(args.output_dir),
    ) as runner:
        packet = await runner.run()

        # Print summary
        print(f"\n{'='*60}")
        print(f"Sentinel Run: {packet.run_id}")
        print(f"Status: {packet.overall_status.upper()}")
        print(f"Duration: {packet.duration_ms:.0f}ms")
        print(f"Steps: {packet.passed_steps}/{packet.total_steps} passed")
        print(f"Artifacts: {runner.run_dir}")
        print(f"{'='*60}\n")

        # Exit with appropriate code
        if packet.overall_status == "passed":
            sys.exit(0)
        else:
            sys.exit(1)


if __name__ == "__main__":
    asyncio.run(main())
