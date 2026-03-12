"""
Debug script to inspect UI element tree after navigation.
This helps identify why LibraryView_Root isn't being found.
"""

from __future__ import annotations

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from conftest import WinAppDriverSession, start_winappdriver

VS_APP_PATH = os.getenv(
    "VS_APP_PATH",
    r"E:\VoiceStudio\src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe",
)


def launch_app(app_path: str) -> WinAppDriverSession | None:
    """Launch the application and return a session."""
    if not start_winappdriver():
        print("ERROR: Could not start WinAppDriver")
        return None

    try:
        session = WinAppDriverSession(app_path)
        return session
    except Exception as e:
        print(f"ERROR creating session: {e}")
        return None


def get_element_tree(session: WinAppDriverSession, depth: int = 3) -> dict:
    """Get simplified element tree from WinAppDriver."""
    try:
        # Get page source (XML of UI tree)
        source = session._post("/source")
        return {"page_source_length": len(source.get("value", ""))}
    except Exception as e:
        return {"error": str(e)}


def find_all_with_pattern(session: WinAppDriverSession, pattern: str) -> list[dict]:
    """Find all elements matching a pattern in their automation ID."""
    results = []
    try:
        # Use XPath to find elements with automation IDs containing the pattern
        elements = session.find_elements("xpath", f"//*[contains(@AutomationId, '{pattern}')]")
        for elem in elements:
            try:
                results.append(
                    {
                        "automation_id": elem.get_attribute("AutomationId") or "N/A",
                        "name": elem.get_attribute("Name") or "N/A",
                        "class": elem.get_attribute("ClassName") or "N/A",
                        "enabled": elem.is_enabled(),
                    }
                )
            except Exception as e:
                results.append({"error": str(e)})
    except Exception as e:
        results.append({"error": str(e)})
    return results


def main():
    print("=" * 60)
    print("UI Navigation Debug Script")
    print("=" * 60)
    print(f"App path: {VS_APP_PATH}")
    print()

    # Launch app
    print("[1] Launching VoiceStudio...")
    session = launch_app(VS_APP_PATH)

    if not session:
        print("ERROR: Failed to launch app")
        return

    print("App launched. Waiting for load...")
    time.sleep(3)

    # Step 1: Find all Nav elements
    print("\n[2] Finding all Nav* elements...")
    nav_elements = find_all_with_pattern(session, "Nav")
    print(f"Found {len(nav_elements)} elements with 'Nav' in automation ID:")
    for elem in nav_elements:
        print(f"  - {elem}")

    # Step 2: Find NavLibrary and click it
    print("\n[3] Looking for NavLibrary button...")
    try:
        nav_library = session.find_element("accessibility id", "NavLibrary")
        print(f"Found NavLibrary: enabled={nav_library.is_enabled()}")

        print("\n[4] Clicking NavLibrary...")
        nav_library.click()
        time.sleep(2)  # Wait for panel to load
        print("Clicked NavLibrary")
    except Exception as e:
        print(f"ERROR finding/clicking NavLibrary: {e}")
        session.quit()
        return

    # Step 3: Find all Library* elements after navigation
    print("\n[5] Finding all Library* elements after navigation...")
    library_elements = find_all_with_pattern(session, "Library")
    print(f"Found {len(library_elements)} elements with 'Library' in automation ID:")
    for elem in library_elements:
        print(f"  - {elem}")

    # Step 4: Try to find LibraryView_Root specifically
    print("\n[6] Looking for LibraryView_Root specifically...")
    try:
        root = session.find_element("accessibility id", "LibraryView_Root")
        print("SUCCESS! Found LibraryView_Root")
        print(f"  - name: {root.get_attribute('Name')}")
        print(f"  - enabled: {root.is_enabled()}")
    except Exception as e:
        print(f"FAILED to find LibraryView_Root: {e}")

    # Step 5: Try finding by other strategies
    print("\n[7] Trying alternative search strategies...")

    # Try by name
    try:
        by_name = session.find_elements("name", "Library")
        print(f"By name 'Library': found {len(by_name)} elements")
        for elem in by_name[:5]:
            try:
                print(
                    f"  - AutomationId: {elem.get_attribute('AutomationId')}, Class: {elem.get_attribute('ClassName')}"
                )
            except Exception:
                pass  # Best effort, failure acceptable
    except Exception as e:
        print(f"By name failed: {e}")

    # Try to find PanelHost elements
    print("\n[8] Looking for PanelHost elements...")
    panel_hosts = find_all_with_pattern(session, "PanelHost")
    print(f"Found {len(panel_hosts)} PanelHost elements:")
    for elem in panel_hosts:
        print(f"  - {elem}")

    # Step 6: Get page source for analysis
    print("\n[9] Getting page source size...")
    try:
        source_info = get_element_tree(session)
        print(f"Page source info: {source_info}")
    except Exception as e:
        print(f"Error getting page source: {e}")

    # Take screenshot
    print("\n[10] Taking screenshot...")
    try:
        screenshot_path = r"E:\VoiceStudio\.buildlogs\ui_tests\screenshots\debug_nav_state.png"
        session.save_screenshot(screenshot_path)
        print(f"Screenshot saved to: {screenshot_path}")
    except Exception as e:
        print(f"Screenshot failed: {e}")

    # Cleanup
    print("\n[11] Closing app...")
    try:
        session.quit()
        print("App closed")
    except Exception:
        pass  # Best effort, failure acceptable

    print("\n" + "=" * 60)
    print("Debug complete")
    print("=" * 60)


if __name__ == "__main__":
    main()
