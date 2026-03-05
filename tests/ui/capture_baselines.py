"""
VoiceStudio Visual Regression Baseline Capture Script.

Launches VoiceStudio, navigates to each core panel, and captures baseline
screenshots for visual regression testing.

Usage:
    python tests/ui/capture_baselines.py [--panels PANEL1,PANEL2,...] [--update-all]

Prerequisites:
    1. WinAppDriver running on localhost:4723
    2. VoiceStudio built in Debug configuration
    3. Backend running on localhost:8000

Environment Variables:
    VOICESTUDIO_APP_PATH: Path to VoiceStudio.App.exe (optional)
    VOICESTUDIO_API_URL: Backend API URL (optional, default: http://127.0.0.1:8000)
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

# Add tests/ui to path for imports
sys.path.insert(0, str(Path(__file__).parent))

from helpers.visual import BASELINE_DIR, capture_baseline, ensure_directories, list_baselines

# Core panels to capture baselines for
CORE_PANELS = {
    "library": {
        "nav_id": "NavLibrary",
        "root_id": "LibraryView_Root",
        "description": "Library panel (asset management)",
    },
    "transcribe": {
        "nav_id": "NavTranscribe",
        "root_id": "TranscribeView_Root",
        "description": "Transcription panel (STT)",
    },
    "synthesis": {
        "nav_id": "NavGenerate",
        "root_id": "VoiceSynthesisView_Root",
        "description": "Voice synthesis panel (TTS)",
    },
    "cloning": {
        "nav_id": "NavCloning",
        "root_id": "VoiceCloningWizardView_Root",
        "description": "Voice cloning wizard",
    },
    "profiles": {
        "nav_id": "NavProfiles",
        "root_id": "ProfilesView_Root",
        "description": "Voice profiles panel",
    },
    "timeline": {
        "nav_id": "NavTimeline",
        "root_id": "TimelineView_Root",
        "description": "Timeline editor",
    },
    "settings": {
        "nav_id": "NavSettings",
        "root_id": "SettingsView_Root",
        "description": "Application settings",
    },
    "diagnostics": {
        "nav_id": "NavDiagnostics",
        "root_id": "DiagnosticsView_Root",
        "description": "System diagnostics",
    },
    "training": {
        "nav_id": "NavTraining",
        "root_id": "TrainingView_Root",
        "description": "Voice model training",
    },
    "analyzer": {
        "nav_id": "NavAnalyzer",
        "root_id": "AnalyzerView_Root",
        "description": "Audio analyzer",
    },
    "effects": {
        "nav_id": "NavEffects",
        "root_id": "EffectsView_Root",
        "description": "Audio effects",
    },
}


def create_session():
    """Create a WinAppDriver session for VoiceStudio."""
    try:
        from conftest import WinAppDriverSession
    except ImportError:
        print("Error: Could not import WinAppDriverSession from conftest.py")
        print("Ensure you're running from the tests/ui directory or project root.")
        sys.exit(1)

    import os

    # Find app path
    project_root = Path(__file__).parent.parent.parent
    default_app_path = (
        project_root
        / "src"
        / "VoiceStudio.App"
        / "bin"
        / "x64"
        / "Debug"
        / "net8.0-windows10.0.19041.0"
        / "win-x64"
        / "VoiceStudio.App.exe"
    )

    app_path = Path(os.environ.get("VOICESTUDIO_APP_PATH", str(default_app_path)))

    if not app_path.exists():
        print(f"Error: Application not found at {app_path}")
        print("Build the application first or set VOICESTUDIO_APP_PATH")
        sys.exit(1)

    try:
        session = WinAppDriverSession("http://127.0.0.1:4723", str(app_path))
        print(f"Session created for: {app_path}")
        return session
    except Exception as e:
        print(f"Error creating session: {e}")
        print("Ensure WinAppDriver is running on localhost:4723")
        sys.exit(1)


def navigate_to_panel(session, nav_id: str, root_id: str, timeout: float = 5.0) -> bool:
    """Navigate to a panel and wait for it to load."""
    try:
        # Click navigation button
        nav_button = session.find_element("accessibility id", nav_id)
        nav_button.click()
        time.sleep(0.3)

        # Wait for panel root to be visible
        start = time.time()
        while time.time() - start < timeout:
            try:
                root = session.find_element("accessibility id", root_id)
                if root.is_displayed():
                    time.sleep(0.5)  # Extra wait for animations
                    return True
            # ALLOWED: bare except - best effort, failure acceptable
            except RuntimeError:
                pass
            time.sleep(0.1)

        return False
    except Exception as e:
        print(f"  Error navigating: {e}")
        return False


def capture_panel_baseline(session, panel_name: str, panel_info: dict) -> bool:
    """Navigate to a panel and capture its baseline."""
    print(f"\nCapturing: {panel_name} - {panel_info['description']}")

    if not navigate_to_panel(session, panel_info["nav_id"], panel_info["root_id"]):
        print(f"  FAILED: Could not navigate to {panel_name}")
        return False

    result = capture_baseline(session, panel_name)
    if result:
        print(f"  OK: {result}")
        return True
    else:
        print("  FAILED: Could not capture screenshot")
        return False


def main():
    parser = argparse.ArgumentParser(
        description="Capture visual regression baselines for VoiceStudio panels"
    )
    parser.add_argument(
        "--panels",
        type=str,
        help="Comma-separated list of panels to capture (default: all)",
    )
    parser.add_argument(
        "--update-all",
        action="store_true",
        help="Update all existing baselines",
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="List available panels and existing baselines",
    )
    args = parser.parse_args()

    # Ensure directories exist
    ensure_directories()

    if args.list:
        print("\nAvailable panels:")
        for name, info in CORE_PANELS.items():
            print(f"  {name}: {info['description']}")

        existing = list_baselines()
        if existing:
            print(f"\nExisting baselines ({len(existing)}):")
            for name in existing:
                print(f"  {name}")
        else:
            print("\nNo existing baselines.")
        return

    # Determine which panels to capture
    if args.panels:
        panel_names = [p.strip().lower() for p in args.panels.split(",")]
        panels = {name: info for name, info in CORE_PANELS.items() if name in panel_names}
        invalid = set(panel_names) - set(panels.keys())
        if invalid:
            print(f"Warning: Unknown panels: {', '.join(invalid)}")
    else:
        panels = CORE_PANELS

    print("VoiceStudio Visual Regression Baseline Capture")
    print("=" * 50)
    print(f"Baseline directory: {BASELINE_DIR}")
    print(f"Panels to capture: {len(panels)}")

    # Create session
    session = create_session()

    # Wait for app to initialize
    print("\nWaiting for application to initialize...")
    time.sleep(3)

    # Capture baselines
    results = {"success": 0, "failed": 0}

    for name, info in panels.items():
        if capture_panel_baseline(session, name, info):
            results["success"] += 1
        else:
            results["failed"] += 1

    # Summary
    print(f"\n{'=' * 50}")
    print("Baseline capture complete!")
    print(f"  Success: {results['success']}")
    print(f"  Failed:  {results['failed']}")
    print(f"\nBaselines saved to: {BASELINE_DIR}")

    # Close session
    try:
        session.quit()
    # ALLOWED: bare except - best effort, failure acceptable
    except Exception:
        pass

    sys.exit(0 if results["failed"] == 0 else 1)


if __name__ == "__main__":
    main()
