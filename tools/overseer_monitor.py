#!/usr/bin/env python3
"""
Overseer Automated Monitoring System

Monitors worker progress and triggers reviews intelligently.
Event-driven + periodic monitoring (no spamming).

Features:
- Ledger integration for status tracking
- Gate status monitoring
- Handoff reconciliation
- Automated report generation
- Worker progress tracking
"""

import argparse
import hashlib
import json
import re
import sys
import time
from datetime import datetime, timedelta
from pathlib import Path

# Try to import watchdog, but don't fail if not available
try:
    from watchdog.events import FileSystemEventHandler
    from watchdog.observers import Observer

    WATCHDOG_AVAILABLE = True
except ImportError:
    WATCHDOG_AVAILABLE = False

# Import overseer tools
try:
    # Try relative import first (when run from project root)
    from tools.overseer.gate_tracker import GateTracker
    from tools.overseer.handoff_manager import HandoffManager
    from tools.overseer.ledger_parser import LedgerParser
    from tools.overseer.models import Gate, LedgerState
    from tools.overseer.report_engine import ReportEngine

    OVERSEER_TOOLS_AVAILABLE = True
except ImportError:
    try:
        # Try adding project root to path
        _project_root = Path(__file__).parent.parent
        if str(_project_root) not in sys.path:
            sys.path.insert(0, str(_project_root))

        from tools.overseer.gate_tracker import GateTracker
        from tools.overseer.handoff_manager import HandoffManager
        from tools.overseer.ledger_parser import LedgerParser
        from tools.overseer.models import Gate, LedgerState
        from tools.overseer.report_engine import ReportEngine

        OVERSEER_TOOLS_AVAILABLE = True
    except ImportError:
        OVERSEER_TOOLS_AVAILABLE = False


class TaskChecklistMonitor:
    """Monitor MASTER_TASK_CHECKLIST.md for changes."""

    def __init__(self, checklist_path: Path, callback):
        self.checklist_path = checklist_path
        self.callback = callback
        self.last_hash: str | None = None
        self.last_mtime: float = 0.0
        self.last_task_count: int = 0
        self.last_complete_count: int = 0

    def check_for_changes(self) -> bool:
        """Check if checklist has changed. Returns True if changed."""
        if not self.checklist_path.exists():
            return False

        # Check modification time first (faster)
        current_mtime = self.checklist_path.stat().st_mtime
        if current_mtime == self.last_mtime:
            return False

        # Calculate hash of current file
        try:
            content = self.checklist_path.read_text(encoding="utf-8")
            current_hash = hashlib.md5(content.encode()).hexdigest()
        except Exception as e:
            print(f"Error reading checklist: {e}")
            return False

        if current_hash != self.last_hash:
            self.last_hash = current_hash
            self.last_mtime = current_mtime

            # Parse task counts
            task_count, complete_count = self._count_tasks(content)

            change_info = {
                "total_tasks": task_count,
                "complete_tasks": complete_count,
                "prev_complete": self.last_complete_count,
                "newly_completed": complete_count - self.last_complete_count,
            }

            self.last_task_count = task_count
            self.last_complete_count = complete_count

            self.callback("checklist_updated", str(self.checklist_path), change_info)
            return True

        return False

    def _count_tasks(self, content: str) -> tuple[int, int]:
        """Count total and completed tasks in checklist."""
        # Count task patterns: [x] = complete, [ ] = incomplete
        complete = len(re.findall(r"\[x\]|\[X\]|COMPLETE", content))
        incomplete = len(re.findall(r"\[ \]|PENDING|IN_PROGRESS", content))
        return complete + incomplete, complete


class ProgressFileMonitor:
    """Monitor worker progress files."""

    def __init__(self, progress_dir: Path, callback):
        self.progress_dir = progress_dir
        self.callback = callback
        self.known_files: dict[str, float] = {}

    def check_for_changes(self):
        """Check for new or updated progress files."""
        if not self.progress_dir.exists():
            return

        # Check all JSON files in progress directory
        for progress_file in self.progress_dir.glob("WORKER_*.json"):
            file_path = str(progress_file)
            current_mtime = progress_file.stat().st_mtime

            if file_path not in self.known_files:
                # New file
                self.known_files[file_path] = current_mtime
                self.callback("progress_file_created", file_path, {})
            elif self.known_files[file_path] != current_mtime:
                # Updated file
                self.known_files[file_path] = current_mtime
                self.callback("progress_file_updated", file_path, {})


class LedgerMonitor:
    """Monitor Quality Ledger for changes."""

    def __init__(self, ledger_path: Path, callback):
        self.ledger_path = ledger_path
        self.callback = callback
        self.last_hash: str | None = None
        self.last_mtime: float = 0.0

    def check_for_changes(self) -> bool:
        """Check if ledger has changed."""
        if not self.ledger_path.exists():
            return False

        current_mtime = self.ledger_path.stat().st_mtime
        if current_mtime == self.last_mtime:
            return False

        try:
            content = self.ledger_path.read_text(encoding="utf-8")
            current_hash = hashlib.md5(content.encode()).hexdigest()
        except Exception as e:
            print(f"Error reading ledger: {e}")
            return False

        if current_hash != self.last_hash:
            self.last_hash = current_hash
            self.last_mtime = current_mtime
            self.callback("ledger_updated", str(self.ledger_path), {})
            return True

        return False


class OverseerMonitor:
    """Main monitoring system - Event-driven + periodic with ledger integration."""

    def __init__(self, project_root: Path, quiet: bool = False):
        self.project_root = project_root
        self.quiet = quiet
        self.checklist_path = project_root / "docs/governance/MASTER_TASK_CHECKLIST.md"
        self.progress_dir = project_root / "docs/governance/progress"
        self.reviews_dir = project_root / "docs/governance/reviews"
        self.ledger_path = project_root / "Recovery Plan/QUALITY_LEDGER.md"

        # Create directories if needed
        self.progress_dir.mkdir(parents=True, exist_ok=True)
        self.reviews_dir.mkdir(parents=True, exist_ok=True)

        # Initialize monitors
        self.checklist_monitor = TaskChecklistMonitor(
            self.checklist_path, self.handle_event
        )
        self.progress_monitor = ProgressFileMonitor(
            self.progress_dir, self.handle_event
        )
        self.ledger_monitor = LedgerMonitor(self.ledger_path, self.handle_event)

        # Initialize overseer tools if available
        self.parser: LedgerParser | None = None
        self.tracker: GateTracker | None = None
        self.handoff_mgr: HandoffManager | None = None
        self.report_engine: ReportEngine | None = None

        if OVERSEER_TOOLS_AVAILABLE:
            try:
                self.parser = LedgerParser(self.ledger_path)
                self.tracker = GateTracker(self.parser)
                self.handoff_mgr = HandoffManager(ledger_parser=self.parser)
                self.report_engine = ReportEngine(
                    ledger_parser=self.parser,
                    gate_tracker=self.tracker,
                    handoff_manager=self.handoff_mgr,
                    reports_dir=self.reviews_dir,
                )
            except Exception as e:
                self.log(f"Warning: Could not initialize overseer tools: {e}")

        # Review timing
        self.last_quick_review = datetime.now()
        self.last_comprehensive_review = datetime.now()
        self.quick_review_interval = timedelta(hours=2)
        self.comprehensive_review_interval = timedelta(hours=6)

        # File watching (if available)
        self.observer: Observer | None = None
        if WATCHDOG_AVAILABLE:
            self.setup_file_watching()

    def log(self, message: str):
        """Log a message (unless quiet mode)."""
        if not self.quiet:
            timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            print(f"[{timestamp}] {message}")

    def setup_file_watching(self):
        """Set up file system watching (if watchdog available)."""
        if not WATCHDOG_AVAILABLE:
            return

        monitor = self

        class FileHandler(FileSystemEventHandler):
            def on_modified(self, event):
                if event.src_path.endswith("MASTER_TASK_CHECKLIST.md"):
                    monitor.checklist_monitor.check_for_changes()
                elif event.src_path.endswith("QUALITY_LEDGER.md"):
                    monitor.ledger_monitor.check_for_changes()
                elif event.src_path.endswith(".json"):
                    monitor.progress_monitor.check_for_changes()

            def on_created(self, event):
                if event.src_path.endswith(".json"):
                    monitor.progress_monitor.check_for_changes()

        self.observer = Observer()

        # Watch governance directory
        governance_dir = self.project_root / "docs/governance"
        if governance_dir.exists():
            self.observer.schedule(FileHandler(), str(governance_dir), recursive=True)

        # Watch Recovery Plan directory
        recovery_dir = self.project_root / "Recovery Plan"
        if recovery_dir.exists():
            self.observer.schedule(FileHandler(), str(recovery_dir), recursive=False)

    def handle_event(self, event_type: str, file_path: str, info: dict):
        """Handle monitoring events."""
        self.log(f"Event: {event_type}")

        if event_type == "checklist_updated":
            self.review_checklist_changes(info)
        elif event_type in ["progress_file_created", "progress_file_updated"]:
            self.review_worker_progress(file_path)
        elif event_type == "ledger_updated":
            self.review_ledger_changes()

    def review_checklist_changes(self, info: dict):
        """Review checklist changes with detailed analysis."""
        self.log("Reviewing checklist changes...")

        total = info.get("total_tasks", 0)
        complete = info.get("complete_tasks", 0)
        newly_completed = info.get("newly_completed", 0)

        if total > 0:
            pct = (complete / total) * 100
            self.log(f"  Progress: {complete}/{total} ({pct:.1f}%)")

        if newly_completed > 0:
            self.log(f"  Newly completed: {newly_completed} tasks")

        # Check against ledger if available
        if self.parser:
            try:
                summary = self.parser.get_summary()
                self.log(f"  Ledger status: {summary.done_entries}/{summary.total_entries} entries DONE")
            except Exception as e:
                self.log(f"  Error checking ledger: {e}")

    def review_worker_progress(self, progress_file: str):
        """Review worker progress with guidance generation."""
        self.log(f"Reviewing worker progress: {Path(progress_file).name}")

        try:
            with open(progress_file) as f:
                progress = json.load(f)

            worker = progress.get("worker", "Unknown")
            status = progress.get("status", "unknown")
            current_task = progress.get("current_task", "None")
            blockers = progress.get("blockers", [])
            completed = progress.get("completed_tasks", [])

            self.log(f"  Worker: {worker}")
            self.log(f"  Status: {status}")
            self.log(f"  Current Task: {current_task}")
            self.log(f"  Completed: {len(completed)} tasks")

            if blockers:
                self.log(f"  BLOCKERS: {len(blockers)}")
                for blocker in blockers:
                    self.log(f"    - {blocker}")

                # Generate guidance
                guidance = self._generate_blocker_guidance(blockers)
                if guidance:
                    self.log("  GUIDANCE:")
                    for g in guidance:
                        self.log(f"    - {g}")

        except Exception as e:
            self.log(f"  Error reading progress file: {e}")

    def _generate_blocker_guidance(self, blockers: list[str]) -> list[str]:
        """Generate guidance for resolving blockers."""
        guidance = []

        for blocker in blockers:
            blocker_lower = blocker.lower()

            if "build" in blocker_lower or "compile" in blocker_lower:
                guidance.append("Check build logs in .buildlogs/ for error details")
            elif "test" in blocker_lower:
                guidance.append("Run tests locally to reproduce: python -m pytest tests/")
            elif "dependency" in blocker_lower or "import" in blocker_lower:
                guidance.append("Verify dependencies: pip install -r requirements.txt")
            elif "gate" in blocker_lower:
                guidance.append("Check gate status: python -m tools.overseer.cli.main gate status")
            elif "proof" in blocker_lower:
                guidance.append("Document proof run in handoff file before marking complete")

        if not guidance:
            guidance.append("Log blocker in QUALITY_LEDGER.md with repro steps")

        return guidance

    def review_ledger_changes(self):
        """Review ledger changes with gate analysis."""
        self.log("Reviewing ledger changes...")

        if not self.parser or not self.tracker:
            self.log("  Overseer tools not available for detailed analysis")
            return

        try:
            # Force re-parse
            self.parser.parse(force=True)
            self.tracker.compute_statuses(force=True)

            summary = self.parser.get_summary()
            current_gate = self.tracker.get_current_gate()
            blockers = self.tracker.get_blockers()

            self.log(f"  Total entries: {summary.total_entries}")
            self.log(f"  Completion: {summary.completion_percent:.1f}%")
            self.log(f"  Current gate: {current_gate.value}")

            if blockers:
                self.log(f"  Active blockers: {len(blockers)}")
                for b in blockers[:3]:
                    self.log(f"    - {b.id}: {b.title[:50]}")

        except Exception as e:
            self.log(f"  Error analyzing ledger: {e}")

    def quick_review(self):
        """Quick progress check (every 2-4 hours)."""
        self.log("=" * 50)
        self.log("QUICK REVIEW")
        self.log("=" * 50)

        # Check all monitors
        self.checklist_monitor.check_for_changes()
        self.progress_monitor.check_for_changes()
        self.ledger_monitor.check_for_changes()

        # Gate status summary
        if self.tracker:
            try:
                self.tracker.compute_statuses(force=True)
                statuses = self.tracker.get_all_statuses()

                self.log("Gate Status:")
                for status in statuses:
                    if status.total_entries > 0:
                        symbol = "[PASS]" if status.is_green else "[OPEN]"
                        self.log(f"  {symbol} Gate {status.gate.value}: {status.done_entries}/{status.total_entries}")
            except Exception as e:
                self.log(f"Error getting gate status: {e}")

        # Generate quick review report
        self._generate_quick_report()

    def comprehensive_review(self):
        """Comprehensive review (every 6-8 hours)."""
        self.log("=" * 50)
        self.log("COMPREHENSIVE REVIEW")
        self.log("=" * 50)

        # Full ledger analysis
        if self.parser:
            try:
                validation = self.parser.validate()
                if not validation.valid:
                    self.log(f"Ledger validation: {len(validation.errors)} errors")
                    for err in validation.errors[:5]:
                        self.log(f"  - {err}")
            except Exception as e:
                self.log(f"Error validating ledger: {e}")

        # Handoff reconciliation
        if self.handoff_mgr:
            try:
                matched, missing, orphan = self.handoff_mgr.reconcile_with_ledger()
                self.log("Handoff reconciliation:")
                self.log(f"  Matched: {len(matched)}")
                self.log(f"  Missing handoffs: {len(missing)}")
                self.log(f"  Orphan handoffs: {len(orphan)}")
            except Exception as e:
                self.log(f"Error reconciling handoffs: {e}")

        # Generate comprehensive report
        if self.report_engine:
            try:
                self.report_engine.generate_comprehensive_report(save=True)
                self.log("Comprehensive report generated")
            except Exception as e:
                self.log(f"Error generating report: {e}")

    def _generate_quick_report(self):
        """Generate a quick review report."""
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        report_path = self.reviews_dir / f"{timestamp}_quick_review.md"

        lines = [
            "# Quick Review Report",
            "",
            f"**Date:** {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}",
            "",
            "## Status",
            "",
        ]

        if self.parser and self.tracker:
            try:
                summary = self.parser.get_summary()
                current_gate = self.tracker.get_current_gate()

                lines.extend([
                    f"- **Ledger Entries:** {summary.total_entries}",
                    f"- **Completion:** {summary.completion_percent:.1f}%",
                    f"- **Current Gate:** {current_gate.value}",
                    "",
                ])
            except Exception:
                lines.append("- Error getting status")
                lines.append("")

        lines.extend([
            "## Next Actions",
            "",
        ])

        if self.tracker:
            try:
                actions = self.tracker.get_next_actions()
                for action in actions:
                    lines.append(f"- {action}")
            except Exception:
                lines.append("- Check gate status manually")

        lines.append("")

        report_path.write_text("\n".join(lines), encoding="utf-8")

    def run_once(self):
        """Run a single review cycle (for testing/one-shot use)."""
        self.log("Running single review cycle...")
        self.quick_review()
        return 0

    def start(self):
        """Start continuous monitoring."""
        self.log("Overseer monitoring system started")
        self.log(f"  Project root: {self.project_root}")
        self.log(f"  Checklist: {self.checklist_path}")
        self.log(f"  Ledger: {self.ledger_path}")
        self.log(f"  Reviews: {self.reviews_dir}")

        if not OVERSEER_TOOLS_AVAILABLE:
            self.log("  WARNING: Overseer tools not available - limited functionality")

        if self.observer:
            self.observer.start()
            self.log("  File watching: ENABLED (watchdog)")
        else:
            self.log("  File watching: DISABLED (using polling)")
            if not WATCHDOG_AVAILABLE:
                self.log("  Install watchdog for better performance: pip install watchdog")

        # Initial review
        self.quick_review()

        try:
            while True:
                now = datetime.now()

                # Quick review (every 2-4 hours)
                if now - self.last_quick_review >= self.quick_review_interval:
                    self.quick_review()
                    self.last_quick_review = now

                # Comprehensive review (every 6-8 hours)
                if now - self.last_comprehensive_review >= self.comprehensive_review_interval:
                    self.comprehensive_review()
                    self.last_comprehensive_review = now

                # Polling check (if watchdog not available)
                if not self.observer:
                    self.checklist_monitor.check_for_changes()
                    self.progress_monitor.check_for_changes()
                    self.ledger_monitor.check_for_changes()

                # Sleep for 5 minutes
                time.sleep(300)

        except KeyboardInterrupt:
            self.log("Stopping monitor...")
            if self.observer:
                self.observer.stop()
                self.observer.join()
            self.log("Monitor stopped")


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        prog="overseer_monitor",
        description="Overseer automated monitoring system",
    )

    parser.add_argument(
        "--once",
        action="store_true",
        help="Run a single review cycle and exit",
    )
    parser.add_argument(
        "--quiet", "-q",
        action="store_true",
        help="Suppress output (except errors)",
    )
    parser.add_argument(
        "--project-root",
        type=Path,
        default=None,
        help="Project root directory",
    )

    args = parser.parse_args()

    # Find project root
    if args.project_root:
        project_root = args.project_root
    else:
        project_root = Path(__file__).parent.parent
        # Verify it looks like the project root
        if not (project_root / "Recovery Plan").exists():
            project_root = Path(__file__).parent
            if not (project_root / "Recovery Plan").exists():
                print(f"Error: Cannot find project root from {Path(__file__)}")
                return 1

    if not project_root.exists():
        print(f"Error: Project root not found: {project_root}")
        return 1

    monitor = OverseerMonitor(project_root, quiet=args.quiet)

    if args.once:
        return monitor.run_once()
    else:
        monitor.start()
        return 0


if __name__ == "__main__":
    sys.exit(main())
