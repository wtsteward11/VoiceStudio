"""
Diagnostics Service — Phase 5.3

Provides comprehensive diagnostic capabilities for troubleshooting
and debugging VoiceStudio issues.

Features:
- System environment diagnostics
- Dependency health checks
- Configuration validation
- Log aggregation
- Diagnostic report generation

Local-first: All data stored locally, no external dependencies.
"""

from __future__ import annotations

import json
import logging
import os
import platform
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from datetime import datetime
from pathlib import Path

logger = logging.getLogger(__name__)

# Default diagnostics output directory
DEFAULT_DIAGNOSTICS_DIR = Path(".buildlogs/diagnostics")


# =============================================================================
# Diagnostic Models
# =============================================================================


@dataclass
class DiagnosticCheck:
    """Result of a single diagnostic check."""

    name: str
    category: str
    status: str  # "pass", "warn", "fail", "skip"
    message: str
    details: dict = field(default_factory=dict)
    duration_ms: float = 0.0


@dataclass
class DiagnosticReport:
    """Complete diagnostic report."""

    generated_at: str
    hostname: str
    platform: str
    python_version: str
    overall_status: str
    checks: list[DiagnosticCheck] = field(default_factory=list)
    environment: dict = field(default_factory=dict)
    recommendations: list[str] = field(default_factory=list)


# =============================================================================
# Diagnostics Service
# =============================================================================


class DiagnosticsService:
    """
    Provides comprehensive system diagnostics.

    Usage:
        service = DiagnosticsService()
        report = service.run_diagnostics()
        service.save_report(report)
    """

    def __init__(self, output_dir: Path | None = None):
        """
        Initialize diagnostics service.

        Args:
            output_dir: Directory for diagnostic reports
        """
        self.output_dir = output_dir or DEFAULT_DIAGNOSTICS_DIR
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self._checks: list[DiagnosticCheck] = []

    def run_diagnostics(self, include_sensitive: bool = False) -> DiagnosticReport:
        """
        Run all diagnostic checks.

        Args:
            include_sensitive: Include sensitive information in report

        Returns:
            Complete diagnostic report
        """
        self._checks = []

        # Run all check categories
        self._check_python_environment()
        self._check_system_resources()
        self._check_dependencies()
        self._check_paths()
        self._check_backend_services()
        self._check_network()
        self._check_model_drift()

        # Calculate overall status
        statuses = [c.status for c in self._checks]
        if "fail" in statuses:
            overall = "unhealthy"
        elif "warn" in statuses:
            overall = "degraded"
        else:
            overall = "healthy"

        # Collect environment info
        env_info = self._collect_environment(include_sensitive)

        # Generate recommendations
        recommendations = self._generate_recommendations()

        return DiagnosticReport(
            generated_at=datetime.utcnow().isoformat() + "Z",
            hostname=platform.node(),
            platform=platform.platform(),
            python_version=sys.version,
            overall_status=overall,
            checks=self._checks,
            environment=env_info,
            recommendations=recommendations,
        )

    def _add_check(
        self,
        name: str,
        category: str,
        status: str,
        message: str,
        details: dict | None = None,
        duration_ms: float = 0.0,
    ) -> None:
        """Add a diagnostic check result."""
        self._checks.append(
            DiagnosticCheck(
                name=name,
                category=category,
                status=status,
                message=message,
                details=details or {},
                duration_ms=duration_ms,
            )
        )

    def _check_python_environment(self) -> None:
        """Check Python environment."""
        import time

        start = time.perf_counter()

        # Python version
        major, minor = sys.version_info[:2]
        if major >= 3 and minor >= 9:
            self._add_check(
                "python_version",
                "environment",
                "pass",
                f"Python {major}.{minor} meets requirements (3.9+)",
                {"version": sys.version},
            )
        else:
            self._add_check(
                "python_version",
                "environment",
                "fail",
                f"Python {major}.{minor} below required 3.9",
                {"version": sys.version},
            )

        # Virtual environment
        in_venv = hasattr(sys, "real_prefix") or (
            hasattr(sys, "base_prefix") and sys.base_prefix != sys.prefix
        )
        if in_venv:
            self._add_check(
                "virtual_env",
                "environment",
                "pass",
                "Running in virtual environment",
                {"prefix": sys.prefix},
            )
        else:
            self._add_check(
                "virtual_env",
                "environment",
                "warn",
                "Not running in virtual environment",
                {"prefix": sys.prefix},
            )

        elapsed = (time.perf_counter() - start) * 1000
        logger.debug(f"Python environment checks: {elapsed:.2f}ms")

    def _check_system_resources(self) -> None:
        """Check system resources."""
        import time

        start = time.perf_counter()

        try:
            import psutil

            # Memory
            memory = psutil.virtual_memory()
            if memory.available / (1024**3) >= 4:
                self._add_check(
                    "memory",
                    "resources",
                    "pass",
                    f"Sufficient memory available ({memory.available / (1024**3):.1f} GB)",
                    {
                        "available_gb": memory.available / (1024**3),
                        "total_gb": memory.total / (1024**3),
                        "percent_used": memory.percent,
                    },
                )
            else:
                self._add_check(
                    "memory",
                    "resources",
                    "warn",
                    f"Low memory ({memory.available / (1024**3):.1f} GB available)",
                    {"available_gb": memory.available / (1024**3)},
                )

            # Disk
            disk = psutil.disk_usage("/")
            if disk.free / (1024**3) >= 10:
                self._add_check(
                    "disk",
                    "resources",
                    "pass",
                    f"Sufficient disk space ({disk.free / (1024**3):.1f} GB free)",
                    {"free_gb": disk.free / (1024**3), "percent_used": disk.percent},
                )
            else:
                self._add_check(
                    "disk",
                    "resources",
                    "warn",
                    f"Low disk space ({disk.free / (1024**3):.1f} GB free)",
                    {"free_gb": disk.free / (1024**3)},
                )

            # CPU
            cpu_count = psutil.cpu_count()
            self._add_check(
                "cpu",
                "resources",
                "pass",
                f"CPU available ({cpu_count} cores)",
                {"cores": cpu_count, "percent": psutil.cpu_percent(interval=0.1)},
            )

        except ImportError:
            self._add_check(
                "psutil",
                "resources",
                "warn",
                "psutil not installed - cannot check system resources",
                {},
            )

        elapsed = (time.perf_counter() - start) * 1000
        logger.debug(f"Resource checks: {elapsed:.2f}ms")

    def _check_dependencies(self) -> None:
        """Check required dependencies."""
        import time

        start = time.perf_counter()

        required_packages = [
            ("fastapi", "Backend framework"),
            ("pydantic", "Data validation"),
            ("uvicorn", "ASGI server"),
            ("aiohttp", "HTTP client"),
        ]

        optional_packages = [
            ("torch", "ML framework"),
            ("numpy", "Numerical computing"),
            ("scipy", "Scientific computing"),
            ("soundfile", "Audio I/O"),
        ]

        for package, description in required_packages:
            try:
                __import__(package)
                self._add_check(
                    f"dep_{package}",
                    "dependencies",
                    "pass",
                    f"{description} ({package}) installed",
                    {},
                )
            except ImportError:
                self._add_check(
                    f"dep_{package}",
                    "dependencies",
                    "fail",
                    f"{description} ({package}) not installed",
                    {},
                )

        for package, description in optional_packages:
            try:
                __import__(package)
                self._add_check(
                    f"dep_{package}",
                    "dependencies",
                    "pass",
                    f"{description} ({package}) installed",
                    {},
                )
            except ImportError:
                self._add_check(
                    f"dep_{package}",
                    "dependencies",
                    "warn",
                    f"{description} ({package}) not installed (optional)",
                    {},
                )

        elapsed = (time.perf_counter() - start) * 1000
        logger.debug(f"Dependency checks: {elapsed:.2f}ms")

    def _check_paths(self) -> None:
        """Check required paths and directories."""
        import time

        start = time.perf_counter()

        paths_to_check = [
            ("VOICESTUDIO_MODELS_PATH", "Model storage path"),
            ("VOICESTUDIO_PROJECTS_DIR", "Projects directory"),
            ("VOICESTUDIO_CACHE_DIR", "Cache directory"),
        ]

        for env_var, description in paths_to_check:
            path = os.environ.get(env_var)
            if path:
                if os.path.exists(path):
                    if os.access(path, os.W_OK):
                        self._add_check(
                            f"path_{env_var.lower()}",
                            "paths",
                            "pass",
                            f"{description} exists and writable",
                            {"path": path},
                        )
                    else:
                        self._add_check(
                            f"path_{env_var.lower()}",
                            "paths",
                            "fail",
                            f"{description} not writable",
                            {"path": path},
                        )
                else:
                    self._add_check(
                        f"path_{env_var.lower()}",
                        "paths",
                        "warn",
                        f"{description} does not exist",
                        {"path": path},
                    )
            else:
                self._add_check(
                    f"path_{env_var.lower()}",
                    "paths",
                    "warn",
                    f"{description} not configured ({env_var})",
                    {},
                )

        # Check ffmpeg
        ffmpeg_path = os.environ.get("VOICESTUDIO_FFMPEG_PATH")
        if ffmpeg_path and os.path.exists(ffmpeg_path):
            self._add_check(
                "ffmpeg", "paths", "pass", "ffmpeg configured and found", {"path": ffmpeg_path}
            )
        else:
            # Try to find ffmpeg in PATH
            try:
                result = subprocess.run(["ffmpeg", "-version"], capture_output=True, timeout=5)
                if result.returncode == 0:
                    self._add_check("ffmpeg", "paths", "pass", "ffmpeg found in PATH", {})
                else:
                    self._add_check("ffmpeg", "paths", "warn", "ffmpeg not found", {})
            except (subprocess.TimeoutExpired, FileNotFoundError):
                self._add_check("ffmpeg", "paths", "warn", "ffmpeg not found", {})

        elapsed = (time.perf_counter() - start) * 1000
        logger.debug(f"Path checks: {elapsed:.2f}ms")

    def _check_backend_services(self) -> None:
        """Check backend services."""
        import time

        start = time.perf_counter()

        # Telemetry service
        try:
            from backend.services.telemetry import get_telemetry_service

            get_telemetry_service()
            self._add_check("telemetry", "services", "pass", "Telemetry service available", {})
        except Exception as e:
            self._add_check(
                "telemetry", "services", "warn", f"Telemetry service unavailable: {e}", {}
            )

        # SLO monitor
        try:
            from backend.services.slo_monitor import get_slo_monitor

            get_slo_monitor()
            self._add_check("slo_monitor", "services", "pass", "SLO monitor available", {})
        except Exception as e:
            self._add_check("slo_monitor", "services", "warn", f"SLO monitor unavailable: {e}", {})

        # Trace exporter
        try:
            from backend.services.trace_export import get_trace_exporter

            get_trace_exporter()
            self._add_check("trace_exporter", "services", "pass", "Trace exporter available", {})
        except Exception as e:
            self._add_check(
                "trace_exporter", "services", "warn", f"Trace exporter unavailable: {e}", {}
            )

        elapsed = (time.perf_counter() - start) * 1000
        logger.debug(f"Service checks: {elapsed:.2f}ms")

    def _check_network(self) -> None:
        """Check network connectivity."""
        import time

        start = time.perf_counter()

        # Check localhost binding
        import socket

        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(2)
            result = sock.connect_ex(("127.0.0.1", 8000))
            sock.close()

            if result == 0:
                self._add_check(
                    "port_8000",
                    "network",
                    "pass",
                    "Port 8000 is in use (backend may be running)",
                    {},
                )
            else:
                self._add_check("port_8000", "network", "pass", "Port 8000 is available", {})
        except Exception as e:
            self._add_check("port_8000", "network", "warn", f"Could not check port 8000: {e}", {})

        elapsed = (time.perf_counter() - start) * 1000
        logger.debug(f"Network checks: {elapsed:.2f}ms")

    def _check_model_drift(self) -> None:
        """Check model data drift status (Phase 9 Sprint 2)."""
        import time

        start = time.perf_counter()

        try:
            from backend.services.model_drift_detector import get_model_drift_detector

            detector = get_model_drift_detector()
            statuses = detector.get_status()

            if not statuses:
                self._add_check(
                    "model_drift",
                    "services",
                    "pass",
                    "Model drift detection: no baselines set (no drift to report)",
                    {"engines_with_baselines": 0},
                )
                return

            drifted = [s for s in statuses if s.any_drifted]
            total_metrics = sum(len(s.metrics) for s in statuses)

            if drifted:
                engines = ", ".join(s.engine_id for s in drifted)
                self._add_check(
                    "model_drift",
                    "services",
                    "warn",
                    f"Model drift detected for: {engines}",
                    {
                        "drifted_engines": [s.engine_id for s in drifted],
                        "total_engines": len(statuses),
                        "total_metrics": total_metrics,
                    },
                )
            else:
                self._add_check(
                    "model_drift",
                    "services",
                    "pass",
                    f"Model drift: no significant drift ({len(statuses)} engines monitored)",
                    {"engines_monitored": len(statuses), "total_metrics": total_metrics},
                )
        except Exception as e:
            logger.debug("Model drift check failed: %s", e)
            self._add_check(
                "model_drift",
                "services",
                "warn",
                f"Model drift check unavailable: {e}",
                {},
            )

        elapsed = (time.perf_counter() - start) * 1000
        logger.debug(f"Model drift check: {elapsed:.2f}ms")

    def _collect_environment(self, include_sensitive: bool) -> dict:
        """Collect environment information."""
        env_vars = [
            "VOICESTUDIO_MODELS_PATH",
            "VOICESTUDIO_PROJECTS_DIR",
            "VOICESTUDIO_CACHE_DIR",
            "VOICESTUDIO_FFMPEG_PATH",
            "HF_HOME",
            "TORCH_HOME",
            "CUDA_VISIBLE_DEVICES",
            "PYTHONPATH",
        ]

        env_info = {}
        for var in env_vars:
            value = os.environ.get(var)
            if value:
                env_info[var] = value

        # Add system info
        env_info["_system"] = {
            "os": platform.system(),
            "os_version": platform.version(),
            "machine": platform.machine(),
            "processor": platform.processor(),
        }

        return env_info

    def _generate_recommendations(self) -> list[str]:
        """Generate recommendations based on check results."""
        recommendations = []

        for check in self._checks:
            if check.status == "fail":
                if "python_version" in check.name:
                    recommendations.append("Upgrade Python to 3.9 or later")
                elif "memory" in check.name:
                    recommendations.append("Consider adding more RAM or closing other applications")
                elif "disk" in check.name:
                    recommendations.append("Free up disk space or expand storage")
                elif "dep_" in check.name:
                    pkg = check.name.replace("dep_", "")
                    recommendations.append(f"Install missing package: pip install {pkg}")
            elif check.status == "warn":
                if "virtual_env" in check.name:
                    recommendations.append("Consider using a virtual environment for isolation")
                elif "path_" in check.name:
                    recommendations.append(
                        f"Configure {check.name.replace('path_', '').upper()} "
                        "environment variable"
                    )

        return list(set(recommendations))  # Remove duplicates

    def save_report(
        self,
        report: DiagnosticReport,
        filename: str | None = None,
    ) -> Path:
        """Save diagnostic report to file."""
        if filename is None:
            timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            filename = f"diagnostic_report_{timestamp}.json"

        filepath = self.output_dir / filename

        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(asdict(report), f, indent=2, default=str)

        logger.info(f"Diagnostic report saved to {filepath}")
        return filepath

    def get_quick_status(self) -> dict:
        """Get a quick status summary without running all checks."""
        return {
            "timestamp": datetime.utcnow().isoformat() + "Z",
            "hostname": platform.node(),
            "platform": platform.system(),
            "python_version": f"{sys.version_info.major}.{sys.version_info.minor}",
            "diagnostics_available": True,
        }


# =============================================================================
# Global Instance
# =============================================================================

_diagnostics_service: DiagnosticsService | None = None


def get_diagnostics_service() -> DiagnosticsService:
    """Get the global diagnostics service."""
    global _diagnostics_service
    if _diagnostics_service is None:
        _diagnostics_service = DiagnosticsService()
    return _diagnostics_service


def run_diagnostics(include_sensitive: bool = False) -> DiagnosticReport:
    """Convenience function to run diagnostics."""
    return get_diagnostics_service().run_diagnostics(include_sensitive)
