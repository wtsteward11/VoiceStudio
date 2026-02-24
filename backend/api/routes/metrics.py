"""
Prometheus Metrics Export API — Phase 5.2.3

Provides a /metrics endpoint exposing VoiceStudio metrics in Prometheus format.
Supports both text (Prometheus exposition) and JSON formats.
All operations are local-first with no external dependencies.

Architecture: Routes -> EngineService -> Engine Layer (app.core.engines)
"""

from __future__ import annotations

import logging
import time
from datetime import datetime, timezone
from typing import Any, Optional

from fastapi import APIRouter, Depends, Query, Response
from fastapi.responses import PlainTextResponse
from pydantic import BaseModel

from backend.ml.models.engine_service import IEngineService, get_engine_service

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/metrics", tags=["metrics"])


# =============================================================================
# Metric Types
# =============================================================================


class MetricValue(BaseModel):
    """A single metric data point."""

    name: str
    value: float
    labels: dict[str, str] = {}
    timestamp: float | None = None
    help_text: str | None = None
    metric_type: str = "gauge"  # gauge, counter, histogram, summary


class MetricsResponse(BaseModel):
    """Response containing all metrics."""

    metrics: list[MetricValue]
    timestamp: str
    format: str = "json"


# =============================================================================
# Metrics Registry (in-memory, local-first)
# =============================================================================


class MetricsRegistry:
    """
    Local metrics registry for VoiceStudio.

    Stores metrics in memory for Prometheus-style scraping.
    Thread-safe and designed for local-first operation.
    """

    _instance: Optional[MetricsRegistry] = None
    _metrics: dict[str, MetricValue] = {}
    _start_time: float = time.time()

    def __new__(cls) -> MetricsRegistry:
        if cls._instance is None:
            cls._instance = super().__new__(cls)
            cls._metrics = {}
            cls._start_time = time.time()
        return cls._instance

    def set_gauge(
        self,
        name: str,
        value: float,
        labels: dict[str, str] | None = None,
        help_text: str | None = None,
    ) -> None:
        """Set a gauge metric value."""
        key = self._make_key(name, labels or {})
        self._metrics[key] = MetricValue(
            name=name,
            value=value,
            labels=labels or {},
            timestamp=time.time(),
            help_text=help_text,
            metric_type="gauge",
        )

    def set_counter(
        self,
        name: str,
        value: float,
        labels: dict[str, str] | None = None,
        help_text: str | None = None,
    ) -> None:
        """Set a counter metric to a specific value."""
        key = self._make_key(name, labels or {})
        self._metrics[key] = MetricValue(
            name=name,
            value=value,
            labels=labels or {},
            timestamp=time.time(),
            help_text=help_text,
            metric_type="counter",
        )

    def inc_counter(
        self,
        name: str,
        value: float = 1.0,
        labels: dict[str, str] | None = None,
        help_text: str | None = None,
    ) -> None:
        """Increment a counter metric."""
        key = self._make_key(name, labels or {})
        current = self._metrics.get(key)
        new_value = (current.value if current else 0) + value
        self._metrics[key] = MetricValue(
            name=name,
            value=new_value,
            labels=labels or {},
            timestamp=time.time(),
            help_text=help_text,
            metric_type="counter",
        )

    def observe_histogram(
        self,
        name: str,
        value: float,
        labels: dict[str, str] | None = None,
        help_text: str | None = None,
    ) -> None:
        """Record a histogram observation (simplified as gauge for now)."""
        # For full histogram support, use prometheus_client library
        key = self._make_key(name + "_latest", labels or {})
        self._metrics[key] = MetricValue(
            name=name + "_latest",
            value=value,
            labels=labels or {},
            timestamp=time.time(),
            help_text=help_text,
            metric_type="gauge",
        )

    def get_all_metrics(self) -> list[MetricValue]:
        """Get all registered metrics."""
        return list(self._metrics.values())

    def get_uptime_seconds(self) -> float:
        """Get server uptime in seconds."""
        return time.time() - self._start_time

    @staticmethod
    def _make_key(name: str, labels: dict[str, str]) -> str:
        """Create unique key for metric+labels combination."""
        if not labels:
            return name
        label_str = ",".join(f'{k}="{v}"' for k, v in sorted(labels.items()))
        return f"{name}{{{label_str}}}"


def get_metrics_registry() -> MetricsRegistry:
    """Get the global metrics registry instance."""
    return MetricsRegistry()


# =============================================================================
# Built-in Metrics Collection
# =============================================================================


def collect_system_metrics(registry: MetricsRegistry) -> None:
    """Collect built-in system metrics."""
    import os
    import platform

    # Process uptime
    registry.set_gauge(
        "voicestudio_uptime_seconds",
        registry.get_uptime_seconds(),
        help_text="VoiceStudio backend uptime in seconds",
    )

    # Python process info
    registry.set_gauge(
        "voicestudio_info",
        1,
        labels={
            "version": "1.0.0",
            "python_version": platform.python_version(),
            "platform": platform.system(),
        },
        help_text="VoiceStudio version and platform info",
    )

    # Process ID for identification
    registry.set_gauge(
        "voicestudio_process_info",
        float(os.getpid()),
        help_text="VoiceStudio backend process ID",
    )


def collect_slo_metrics(registry: MetricsRegistry) -> None:
    """Collect SLO-related metrics from slo_monitor service."""
    try:
        from backend.platform.monitoring.slo_monitor import get_slo_monitor

        monitor = get_slo_monitor()
        slo_statuses = monitor.get_all_slo_statuses()

        for slo in slo_statuses:
            labels = {"slo_id": slo.slo_id, "slo_name": slo.slo_name}

            registry.set_gauge(
                "voicestudio_slo_current_value",
                slo.current_value,
                labels=labels,
                help_text="Current value of SLO metric",
            )

            registry.set_gauge(
                "voicestudio_slo_target",
                slo.target,
                labels=labels,
                help_text="Target value for SLO",
            )

            registry.set_gauge(
                "voicestudio_slo_is_met",
                1.0 if slo.is_met else 0.0,
                labels=labels,
                help_text="Whether SLO target is currently met (1=yes, 0=no)",
            )

            registry.set_gauge(
                "voicestudio_slo_burn_rate",
                slo.burn_rate,
                labels=labels,
                help_text="SLO error budget burn rate",
            )

            registry.set_gauge(
                "voicestudio_slo_error_budget_remaining",
                slo.error_budget_remaining,
                labels=labels,
                help_text="Remaining error budget percentage",
            )

    except ImportError:
        logger.debug("SLO monitor not available for metrics collection")
    except AttributeError as exc:
        logger.warning("SLO monitor API changed: %s", exc)
    except ValueError as exc:
        logger.warning("Invalid SLO data: %s", exc)


def collect_engine_metrics(
    registry: MetricsRegistry,
    engine_service: IEngineService | None = None,
) -> None:
    """Collect engine-related metrics from the engine service.

    Args:
        registry: Metrics registry to populate.
        engine_service: Optional engine service. If not provided, gets global instance.
    """
    try:
        # Use injected service or get global instance
        service = engine_service or get_engine_service()
        engine_metrics = service.get_metrics()

        if engine_metrics.get("error") or not engine_metrics.get("available", True):
            logger.debug("Engine metrics not available via service")
            return

        # If get_metrics returns raw metrics dict, use it directly
        # Otherwise try to get collector-style interface
        if hasattr(engine_metrics, "get_all_metrics"):
            all_metrics = engine_metrics.get_all_metrics()
        else:
            # Assume engine_metrics is already the metrics dict
            all_metrics = engine_metrics

        # Synthesis latency histograms
        synth_latency = all_metrics.get("synthesis_latency", {})
        for engine, data in synth_latency.items():
            registry.set_gauge(
                "voicestudio_synthesis_latency_ms_p50",
                data.get("p50", 0),
                labels={"engine": engine},
                help_text="Synthesis latency 50th percentile (ms)",
            )
            registry.set_gauge(
                "voicestudio_synthesis_latency_ms_p99",
                data.get("p99", 0),
                labels={"engine": engine},
                help_text="Synthesis latency 99th percentile (ms)",
            )
            registry.set_counter(
                "voicestudio_synthesis_total",
                data.get("count", 0),
                labels={"engine": engine},
                help_text="Total synthesis operations",
            )

        # Synthesis counters
        synth_counter = all_metrics.get("synthesis_counter", {})
        for engine, data in synth_counter.items():
            registry.set_counter(
                "voicestudio_synthesis_success_total",
                data.get("success", 0),
                labels={"engine": engine},
                help_text="Successful synthesis operations",
            )
            registry.set_counter(
                "voicestudio_synthesis_failure_total",
                data.get("failure", 0),
                labels={"engine": engine},
                help_text="Failed synthesis operations",
            )

        # Transcription latency histograms
        trans_latency = all_metrics.get("transcription_latency", {})
        for engine, data in trans_latency.items():
            registry.set_gauge(
                "voicestudio_transcription_latency_ms_p50",
                data.get("p50", 0),
                labels={"engine": engine},
                help_text="Transcription latency 50th percentile (ms)",
            )
            registry.set_gauge(
                "voicestudio_transcription_latency_ms_p99",
                data.get("p99", 0),
                labels={"engine": engine},
                help_text="Transcription latency 99th percentile (ms)",
            )
            registry.set_counter(
                "voicestudio_transcription_total",
                data.get("count", 0),
                labels={"engine": engine},
                help_text="Total transcription operations",
            )

        # Transcription counters
        trans_counter = all_metrics.get("transcription_counter", {})
        for engine, data in trans_counter.items():
            registry.set_counter(
                "voicestudio_transcription_success_total",
                data.get("success", 0),
                labels={"engine": engine},
                help_text="Successful transcription operations",
            )
            registry.set_counter(
                "voicestudio_transcription_failure_total",
                data.get("failure", 0),
                labels={"engine": engine},
                help_text="Failed transcription operations",
            )

        # Audio duration metrics
        audio_dur = all_metrics.get("audio_duration", {})
        for engine, data in audio_dur.items():
            registry.set_gauge(
                "voicestudio_audio_duration_seconds_total",
                data.get("sum", 0),
                labels={"engine": engine},
                help_text="Total audio duration generated (seconds)",
            )

        # Engine availability (count of engines with any recorded metrics)
        engines_with_metrics = set(synth_latency.keys()) | set(trans_latency.keys())
        registry.set_gauge(
            "voicestudio_engines_available",
            len(engines_with_metrics) if engines_with_metrics else 1,
            help_text="Number of available synthesis/transcription engines",
        )

    except (AttributeError, ValueError, TypeError) as exc:
        logger.debug("Engine metrics collection error: %s", exc)
    except Exception as exc:
        logger.warning("Unexpected error collecting engine metrics: %s", exc)


# =============================================================================
# Prometheus Text Format Export
# =============================================================================


def format_prometheus_text(metrics: list[MetricValue]) -> str:
    """
    Format metrics in Prometheus text exposition format.

    See: https://prometheus.io/docs/instrumenting/exposition_formats/
    """
    lines: list[str] = []
    seen_help: set = set()

    for metric in metrics:
        # Add HELP and TYPE lines once per metric name
        if metric.name not in seen_help:
            if metric.help_text:
                lines.append(f"# HELP {metric.name} {metric.help_text}")
            lines.append(f"# TYPE {metric.name} {metric.metric_type}")
            seen_help.add(metric.name)

        # Format labels
        if metric.labels:
            label_str = ",".join(f'{k}="{v}"' for k, v in sorted(metric.labels.items()))
            metric_line = f"{metric.name}{{{label_str}}} {metric.value}"
        else:
            metric_line = f"{metric.name} {metric.value}"

        # Add timestamp if available
        if metric.timestamp:
            metric_line += f" {int(metric.timestamp * 1000)}"

        lines.append(metric_line)

    return "\n".join(lines) + "\n"


# =============================================================================
# API Endpoints
# =============================================================================


@router.get(
    "",
    summary="Get metrics in Prometheus format",
    description="Returns all VoiceStudio metrics in Prometheus text format.",
    response_class=PlainTextResponse,
)
async def get_metrics_prometheus(
    engine_service: IEngineService = Depends(get_engine_service),
) -> Response:
    """
    Get metrics in Prometheus text exposition format.

    This endpoint is designed for Prometheus scraping.
    Returns metrics with HELP and TYPE annotations.
    """
    registry = get_metrics_registry()

    # Collect all metric sources
    collect_system_metrics(registry)
    collect_slo_metrics(registry)
    collect_engine_metrics(registry, engine_service)

    metrics = registry.get_all_metrics()
    text_output = format_prometheus_text(metrics)

    return Response(
        content=text_output,
        media_type="text/plain; version=0.0.4; charset=utf-8",
    )


@router.get(
    "/json",
    response_model=MetricsResponse,
    summary="Get metrics in JSON format",
    description="Returns all VoiceStudio metrics in JSON format.",
)
async def get_metrics_json(
    engine_service: IEngineService = Depends(get_engine_service),
) -> MetricsResponse:
    """
    Get metrics in JSON format.

    Useful for custom dashboards and debugging.
    """
    registry = get_metrics_registry()

    # Collect all metric sources
    collect_system_metrics(registry)
    collect_slo_metrics(registry)
    collect_engine_metrics(registry, engine_service)

    metrics = registry.get_all_metrics()

    return MetricsResponse(
        metrics=metrics,
        timestamp=datetime.now(timezone.utc).isoformat(),
        format="json",
    )


@router.get(
    "/health",
    summary="Metrics endpoint health check",
    description="Verify the metrics endpoint is operational.",
)
async def metrics_health() -> dict[str, Any]:
    """Health check for metrics endpoint."""
    registry = get_metrics_registry()
    return {
        "status": "healthy",
        "uptime_seconds": registry.get_uptime_seconds(),
        "metric_count": len(registry.get_all_metrics()),
        "timestamp": datetime.now(timezone.utc).isoformat(),
    }


@router.post(
    "/record",
    summary="Record a custom metric",
    description="Record a custom metric value (for internal use).",
)
async def record_metric(
    name: str = Query(..., description="Metric name"),
    value: float = Query(..., description="Metric value"),
    metric_type: str = Query("gauge", description="Metric type"),
    labels: str | None = Query(None, description="Labels as key=value pairs"),
) -> dict[str, str]:
    """
    Record a custom metric.

    This endpoint allows internal services to push metrics.
    Labels should be provided as comma-separated key=value pairs.
    """
    registry = get_metrics_registry()

    # Parse labels
    label_dict: dict[str, str] = {}
    if labels:
        for pair in labels.split(","):
            if "=" in pair:
                k, v = pair.split("=", 1)
                label_dict[k.strip()] = v.strip()

    if metric_type == "counter":
        registry.inc_counter(name, value, label_dict)
    elif metric_type == "histogram":
        registry.observe_histogram(name, value, label_dict)
    else:
        registry.set_gauge(name, value, label_dict)

    return {"status": "recorded", "metric": name, "value": str(value)}
