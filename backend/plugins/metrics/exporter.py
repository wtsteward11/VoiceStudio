"""
Plugin Metrics Exporter.

Export metrics in various formats for dashboards and monitoring.
"""

import json
import logging
from datetime import datetime
from enum import Enum
from io import StringIO
from typing import Any, Dict, List, Optional

from .aggregator import MetricsAggregator, get_aggregator
from .collector import MetricType, PluginMetric, get_all_collectors

logger = logging.getLogger(__name__)


class MetricsFormat(Enum):
    """Supported export formats."""

    JSON = "json"
    PROMETHEUS = "prometheus"
    CSV = "csv"
    INFLUXDB = "influxdb"


class MetricsExporter:
    """
    Export plugin metrics in various formats.

    Supports:
    - JSON: For dashboards and APIs
    - Prometheus: For Prometheus/Grafana integration
    - CSV: For spreadsheet analysis
    - InfluxDB: For InfluxDB line protocol
    """

    def __init__(
        self,
        aggregator: Optional[MetricsAggregator] = None,
    ) -> None:
        """
        Initialize exporter.

        Args:
            aggregator: MetricsAggregator instance, or uses global
        """
        self._aggregator = aggregator or get_aggregator()

    def export(
        self,
        format: MetricsFormat,
        plugin_id: Optional[str] = None,
        since: Optional[datetime] = None,
    ) -> str:
        """
        Export metrics in specified format.

        Args:
            format: Export format
            plugin_id: Filter to specific plugin
            since: Filter metrics after this timestamp

        Returns:
            Formatted metrics string
        """
        if format == MetricsFormat.JSON:
            return self.to_json(plugin_id=plugin_id, since=since)
        elif format == MetricsFormat.PROMETHEUS:
            return self.to_prometheus(plugin_id=plugin_id)
        elif format == MetricsFormat.CSV:
            return self.to_csv(plugin_id=plugin_id, since=since)
        elif format == MetricsFormat.INFLUXDB:
            return self.to_influxdb(plugin_id=plugin_id, since=since)
        else:
            raise ValueError(f"Unsupported format: {format}")

    def to_json(
        self,
        plugin_id: Optional[str] = None,
        since: Optional[datetime] = None,
        pretty: bool = True,
    ) -> str:
        """
        Export metrics as JSON.

        Args:
            plugin_id: Filter to specific plugin
            since: Filter metrics after this timestamp
            pretty: Pretty print JSON

        Returns:
            JSON string
        """
        collectors = get_all_collectors()

        if plugin_id:
            collector = collectors.get(plugin_id)
            if not collector:
                return json.dumps({"error": f"Plugin not found: {plugin_id}"})
            data = {
                "plugin_id": plugin_id,
                "stats": collector.get_stats(),
                "metrics": [m.to_dict() for m in collector.get_metrics(since=since)],
            }
        else:
            # All plugins
            plugin_data = {}
            for pid, collector in collectors.items():
                plugin_data[pid] = {
                    "stats": collector.get_stats(),
                    "metrics": [m.to_dict() for m in collector.get_metrics(since=since)],
                }

            system_health = self._aggregator.get_system_health()
            data = {
                "system": system_health.to_dict(),
                "plugins": plugin_data,
                "exported_at": datetime.now().isoformat(),
            }

        indent = 2 if pretty else None
        return json.dumps(data, indent=indent, default=str)

    def to_prometheus(
        self,
        plugin_id: Optional[str] = None,
    ) -> str:
        """
        Export metrics in Prometheus exposition format.

        Args:
            plugin_id: Filter to specific plugin

        Returns:
            Prometheus text format
        """
        lines: List[str] = []
        collectors = get_all_collectors()

        if plugin_id:
            collectors = {plugin_id: collectors.get(plugin_id)} if plugin_id in collectors else {}

        for pid, collector in collectors.items():
            if collector is None:
                continue

            stats = collector.get_stats()
            summary = stats.get("summary", {})
            counters = stats.get("counters", {})
            gauges = stats.get("gauges", {})
            execution = stats.get("execution", {})

            # Plugin-level metrics
            labels = f'plugin_id="{pid}"'

            # Total calls
            lines.append("# HELP voicestudio_plugin_calls_total Total plugin method calls")
            lines.append("# TYPE voicestudio_plugin_calls_total counter")
            lines.append(
                f'voicestudio_plugin_calls_total{{{labels}}} {summary.get("total_calls", 0)}'
            )

            # Total errors
            lines.append("# HELP voicestudio_plugin_errors_total Total plugin errors")
            lines.append("# TYPE voicestudio_plugin_errors_total counter")
            lines.append(
                f'voicestudio_plugin_errors_total{{{labels}}} {summary.get("total_errors", 0)}'
            )

            # Per-method metrics
            for method, method_stats in execution.items():
                method_labels = f'plugin_id="{pid}",method="{method}"'

                # Duration histogram
                lines.append(
                    f'voicestudio_plugin_duration_ms_sum{{{method_labels}}} {method_stats.get("total_duration_ms", 0)}'
                )
                lines.append(
                    f'voicestudio_plugin_duration_ms_count{{{method_labels}}} {method_stats.get("call_count", 0)}'
                )

                # Quantiles
                lines.append(
                    f'voicestudio_plugin_duration_ms{{quantile="0.5",{method_labels}}} {method_stats.get("p50_duration_ms", 0)}'
                )
                lines.append(
                    f'voicestudio_plugin_duration_ms{{quantile="0.95",{method_labels}}} {method_stats.get("p95_duration_ms", 0)}'
                )
                lines.append(
                    f'voicestudio_plugin_duration_ms{{quantile="0.99",{method_labels}}} {method_stats.get("p99_duration_ms", 0)}'
                )

            # IPC metrics
            lines.append(
                f'voicestudio_plugin_ipc_requests_total{{{labels}}} {counters.get("ipc_requests", 0)}'
            )
            lines.append(
                f'voicestudio_plugin_ipc_errors_total{{{labels}}} {counters.get("ipc_errors", 0)}'
            )

            # Permission metrics
            lines.append(
                f'voicestudio_plugin_permission_checks_total{{{labels}}} {counters.get("permission_checks", 0)}'
            )
            lines.append(
                f'voicestudio_plugin_permission_denials_total{{{labels}}} {counters.get("permission_denials", 0)}'
            )

            # Lifecycle metrics
            lines.append(
                f'voicestudio_plugin_starts_total{{{labels}}} {counters.get("lifecycle_starts", 0)}'
            )
            lines.append(
                f'voicestudio_plugin_crashes_total{{{labels}}} {counters.get("lifecycle_crashes", 0)}'
            )

            # Gauges
            if "memory_bytes" in gauges:
                lines.append(
                    f'voicestudio_plugin_memory_bytes{{{labels}}} {gauges["memory_bytes"]}'
                )

        return "\n".join(lines)

    def to_csv(
        self,
        plugin_id: Optional[str] = None,
        since: Optional[datetime] = None,
    ) -> str:
        """
        Export metrics as CSV.

        Args:
            plugin_id: Filter to specific plugin
            since: Filter metrics after this timestamp

        Returns:
            CSV string
        """
        output = StringIO()

        # Header
        output.write("timestamp,plugin_id,metric_type,value,labels\n")

        collectors = get_all_collectors()

        for pid, collector in collectors.items():
            if plugin_id and pid != plugin_id:
                continue

            metrics = collector.get_metrics(since=since)
            for metric in metrics:
                labels_str = ";".join(f"{k}={v}" for k, v in metric.labels.items())
                output.write(
                    f"{metric.timestamp.isoformat()},{pid},"
                    f"{metric.metric_type.value},{metric.value},"
                    f'"{labels_str}"\n'
                )

        return output.getvalue()

    def to_influxdb(
        self,
        plugin_id: Optional[str] = None,
        since: Optional[datetime] = None,
        measurement: str = "voicestudio_plugin",
    ) -> str:
        """
        Export metrics in InfluxDB line protocol.

        Args:
            plugin_id: Filter to specific plugin
            since: Filter metrics after this timestamp
            measurement: InfluxDB measurement name

        Returns:
            InfluxDB line protocol string
        """
        lines: List[str] = []
        collectors = get_all_collectors()

        for pid, collector in collectors.items():
            if plugin_id and pid != plugin_id:
                continue

            metrics = collector.get_metrics(since=since)
            for metric in metrics:
                # Tags
                tags = [f"plugin_id={self._escape_influx(pid)}"]
                tags.append(f"metric_type={self._escape_influx(metric.metric_type.value)}")
                for k, v in metric.labels.items():
                    tags.append(f"{self._escape_influx(k)}={self._escape_influx(v)}")

                tags_str = ",".join(tags)

                # Field
                field_str = f"value={metric.value}"

                # Timestamp (nanoseconds)
                timestamp_ns = int(metric.timestamp.timestamp() * 1e9)

                lines.append(f"{measurement},{tags_str} {field_str} {timestamp_ns}")

        return "\n".join(lines)

    def _escape_influx(self, value: str) -> str:
        """Escape value for InfluxDB line protocol."""
        return value.replace(" ", "\\ ").replace(",", "\\,").replace("=", "\\=")

    def get_summary_report(self) -> str:
        """
        Generate a human-readable summary report.

        Returns:
            Formatted text report
        """
        system = self._aggregator.get_system_health()
        plugins = self._aggregator.get_all_plugin_health()
        unhealthy = self._aggregator.get_unhealthy_plugins()

        lines = [
            "=" * 60,
            "VoiceStudio Plugin Metrics Summary",
            "=" * 60,
            f"Generated: {datetime.now().isoformat()}",
            "",
            "System Overview:",
            f"  Total Plugins: {system.total_plugins}",
            f"  Healthy: {system.healthy_plugins}",
            f"  Degraded: {system.degraded_plugins}",
            f"  Unhealthy: {system.unhealthy_plugins}",
            "",
            f"  Total Calls: {system.total_calls:,}",
            f"  Total Errors: {system.total_errors:,}",
            f"  Error Rate: {system.system_error_rate:.2f}%",
            f"  Avg Latency: {system.avg_latency_ms:.2f}ms",
            f"  Memory Usage: {system.total_memory_bytes / 1024 / 1024:.2f}MB",
            "",
        ]

        if unhealthy:
            lines.append("Unhealthy Plugins:")
            for p in unhealthy:
                lines.append(
                    f"  - {p.plugin_id}: {p.status} "
                    f"(error rate: {p.error_rate:.2f}%, "
                    f"crashes: {p.crash_count})"
                )
            lines.append("")

        lines.append("Per-Plugin Details:")
        for p in sorted(plugins, key=lambda x: x.total_calls, reverse=True):
            lines.append(f"  {p.plugin_id}:")
            lines.append(f"    Status: {p.status}")
            lines.append(f"    Calls: {p.total_calls:,}")
            lines.append(f"    Errors: {p.total_errors:,} ({p.error_rate:.2f}%)")
            lines.append(f"    Avg Latency: {p.avg_latency_ms:.2f}ms")
            if p.memory_bytes:
                lines.append(f"    Memory: {p.memory_bytes / 1024:.2f}KB")
            lines.append("")

        lines.append("=" * 60)
        return "\n".join(lines)
