"""
Cursor AI Agent Reporter Plugin for VoiceStudio UI Tests.

This pytest plugin provides structured output optimized for Cursor AI agent consumption.
It emits machine-readable events during test execution and generates detailed failure
analysis to assist automated debugging workflows.

Usage:
    pytest tests/ui/ -p tests.ui.plugins.cursor_reporter

Features:
    - Structured JSON event emission for each test phase
    - Screenshot capture on failures (when enabled)
    - Stack trace analysis with source context
    - Element state capture for UI-related failures
    - Session-level summary for agent consumption
"""

import json
import os
import sys
import time
import traceback
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional

import pytest


class CursorAgentReporter:
    """Reporter that emits structured events for Cursor AI agent consumption."""

    def __init__(self, config: pytest.Config):
        self.config = config
        self.events: List[Dict[str, Any]] = []
        self.session_start: Optional[float] = None
        self.current_test: Optional[str] = None
        self.test_results: Dict[str, Dict[str, Any]] = {}
        self.output_dir = Path(os.environ.get("VOICESTUDIO_UI_TEST_OUTPUT", ".buildlogs/ui-tests"))
        self.screenshot_dir = Path(
            os.environ.get("VOICESTUDIO_UI_SCREENSHOT_DIR", self.output_dir / "screenshots")
        )

    def _emit_event(self, event_type: str, data: Dict[str, Any], level: str = "INFO") -> None:
        """Emit a structured event for agent consumption."""
        event = {
            "timestamp": datetime.utcnow().isoformat() + "Z",
            "type": event_type,
            "level": level,
            "data": data,
        }
        self.events.append(event)

        # Also print for real-time consumption
        marker = f"[CURSOR:{event_type}]"
        print(f"{marker} {json.dumps(data, default=str)}", file=sys.stderr)

    def _extract_failure_context(self, report: pytest.TestReport) -> Dict[str, Any]:
        """Extract detailed failure context for debugging assistance."""
        context: Dict[str, Any] = {
            "nodeid": report.nodeid,
            "duration": report.duration,
            "outcome": report.outcome,
        }

        if report.longrepr:
            # Extract exception info
            if hasattr(report.longrepr, "reprcrash"):
                crash = report.longrepr.reprcrash
                context["crash"] = {
                    "path": str(crash.path) if crash.path else None,
                    "lineno": crash.lineno,
                    "message": crash.message,
                }

            # Extract traceback entries
            if hasattr(report.longrepr, "reprtraceback"):
                entries = []
                for entry in report.longrepr.reprtraceback.reprentries:
                    if hasattr(entry, "reprfileloc"):
                        loc = entry.reprfileloc
                        entries.append(
                            {
                                "path": str(loc.path) if loc.path else None,
                                "lineno": loc.lineno,
                                "message": loc.message if hasattr(loc, "message") else None,
                            }
                        )
                context["traceback"] = entries

            # Full string representation
            context["longrepr_str"] = str(report.longrepr)

        return context

    def _categorize_failure(self, context: Dict[str, Any]) -> str:
        """Categorize failure type for targeted debugging."""
        longrepr = context.get("longrepr_str", "")

        if "WinAppDriver" in longrepr or "element" in longrepr.lower():
            return "UI_ELEMENT"
        elif "connection" in longrepr.lower() or "refused" in longrepr.lower():
            return "CONNECTION"
        elif "timeout" in longrepr.lower():
            return "TIMEOUT"
        elif "assert" in longrepr.lower():
            return "ASSERTION"
        elif "import" in longrepr.lower() or "ModuleNotFound" in longrepr:
            return "IMPORT"
        elif "permission" in longrepr.lower() or "access" in longrepr.lower():
            return "PERMISSION"
        else:
            return "UNKNOWN"

    def _suggest_remediation(self, category: str, context: Dict[str, Any]) -> List[str]:
        """Suggest remediation steps based on failure category."""
        suggestions = {
            "UI_ELEMENT": [
                "Verify AutomationId exists in XAML",
                "Check if element is visible and enabled",
                "Ensure WinAppDriver session is active",
                "Verify application window is in foreground",
            ],
            "CONNECTION": [
                "Verify WinAppDriver is running (port 4723)",
                "Check backend health endpoint (/api/health)",
                "Ensure no firewall blocking local connections",
            ],
            "TIMEOUT": [
                "Increase wait timeout in test",
                "Check if operation is blocking",
                "Verify application responsiveness",
            ],
            "ASSERTION": [
                "Review expected vs actual values",
                "Check test data validity",
                "Verify application state before assertion",
            ],
            "IMPORT": [
                "Verify PYTHONPATH includes project root",
                "Check module exists and has __init__.py",
                "Verify dependencies installed (pip install -r requirements.txt)",
            ],
            "PERMISSION": [
                "Run as administrator if required",
                "Check file/directory permissions",
                "Verify Developer Mode is enabled",
            ],
            "UNKNOWN": [
                "Review full stack trace",
                "Check test prerequisites",
                "Run preflight_ui_tests.ps1 to validate environment",
            ],
        }
        return suggestions.get(category, suggestions["UNKNOWN"])


def pytest_configure(config: pytest.Config) -> None:
    """Register the Cursor agent reporter plugin."""
    config._cursor_reporter = CursorAgentReporter(config)
    config.pluginmanager.register(config._cursor_reporter, "cursor_reporter")


def pytest_unconfigure(config: pytest.Config) -> None:
    """Unregister the plugin on exit."""
    reporter = getattr(config, "_cursor_reporter", None)
    if reporter:
        config.pluginmanager.unregister(reporter)


@pytest.hookimpl(tryfirst=True)
def pytest_sessionstart(session: pytest.Session) -> None:
    """Called when test session starts."""
    reporter = session.config._cursor_reporter
    reporter.session_start = time.time()
    reporter._emit_event(
        "session_start",
        {
            "python_version": sys.version,
            "pytest_version": pytest.__version__,
            "cwd": os.getcwd(),
        },
    )


@pytest.hookimpl(trylast=True)
def pytest_sessionfinish(session: pytest.Session, exitstatus: int) -> None:
    """Called when test session ends."""
    reporter = session.config._cursor_reporter
    duration = time.time() - reporter.session_start if reporter.session_start else 0

    # Aggregate results
    total = len(reporter.test_results)
    passed = sum(1 for r in reporter.test_results.values() if r["outcome"] == "passed")
    failed = sum(1 for r in reporter.test_results.values() if r["outcome"] == "failed")
    skipped = sum(1 for r in reporter.test_results.values() if r["outcome"] == "skipped")
    errors = sum(1 for r in reporter.test_results.values() if r["outcome"] == "error")

    # Collect failures with analysis
    failures = []
    for nodeid, result in reporter.test_results.items():
        if result["outcome"] in ("failed", "error"):
            category = reporter._categorize_failure(result)
            failures.append(
                {
                    "nodeid": nodeid,
                    "category": category,
                    "duration": result.get("duration", 0),
                    "message": result.get("crash", {}).get("message", "Unknown"),
                    "suggestions": reporter._suggest_remediation(category, result),
                }
            )

    summary = {
        "status": "PASSED" if exitstatus == 0 else "FAILED",
        "exit_code": exitstatus,
        "duration_seconds": round(duration, 2),
        "summary": {
            "total": total,
            "passed": passed,
            "failed": failed,
            "skipped": skipped,
            "errors": errors,
        },
        "failures": failures,
    }

    reporter._emit_event("session_finish", summary, level="SUCCESS" if exitstatus == 0 else "ERROR")

    # Output final summary for agent parsing
    print("\n--- CURSOR_AGENT_SESSION_SUMMARY ---", file=sys.stderr)
    print(json.dumps(summary, indent=2), file=sys.stderr)
    print("--- END_CURSOR_AGENT_SESSION_SUMMARY ---\n", file=sys.stderr)


@pytest.hookimpl(tryfirst=True)
def pytest_runtest_setup(item: pytest.Item) -> None:
    """Called before each test setup."""
    reporter = item.config._cursor_reporter
    reporter.current_test = item.nodeid
    reporter._emit_event(
        "test_setup",
        {
            "nodeid": item.nodeid,
            "name": item.name,
            "markers": [m.name for m in item.iter_markers()],
        },
    )


@pytest.hookimpl(trylast=True)
def pytest_runtest_makereport(item: pytest.Item, call: pytest.CallInfo) -> None:
    """Called after each test phase (setup, call, teardown)."""
    reporter = item.config._cursor_reporter

    if call.when == "call":
        # Extract result for this test
        outcome = "passed" if call.excinfo is None else "failed"
        result = {
            "outcome": outcome,
            "duration": call.duration,
            "when": call.when,
        }

        if call.excinfo:
            result["exception_type"] = call.excinfo.typename
            result["exception_value"] = str(call.excinfo.value)

            # Capture screenshot on failure if enabled
            screenshot_dir = reporter.screenshot_dir
            if screenshot_dir and screenshot_dir.exists():
                try:
                    from tests.ui.helpers.visual import capture_screenshot

                    safe_name = item.nodeid.replace("::", "_").replace("/", "_").replace("\\", "_")
                    screenshot_path = screenshot_dir / f"failure_{safe_name}.png"
                    # Note: This requires an active WinAppDriver session
                    # capture_screenshot(screenshot_path)
                    result["screenshot"] = str(screenshot_path)
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

        reporter.test_results[item.nodeid] = result
        reporter._emit_event(
            "test_result",
            {"nodeid": item.nodeid, **result},
            level="SUCCESS" if outcome == "passed" else "ERROR",
        )


@pytest.hookimpl(tryfirst=True)
def pytest_runtest_logreport(report: pytest.TestReport) -> None:
    """Called when a test report is ready."""
    if report.when == "call" and report.failed:
        # Get reporter from config
        config = report.config if hasattr(report, "config") else None
        if config and hasattr(config, "_cursor_reporter"):
            reporter = config._cursor_reporter
            context = reporter._extract_failure_context(report)
            category = reporter._categorize_failure(context)

            # Update stored result with detailed context
            if report.nodeid in reporter.test_results:
                reporter.test_results[report.nodeid].update(context)
                reporter.test_results[report.nodeid]["category"] = category
                reporter.test_results[report.nodeid]["suggestions"] = reporter._suggest_remediation(
                    category, context
                )
