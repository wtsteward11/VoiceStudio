"""Debug script for Import functionality testing with WinAppDriver."""

from __future__ import annotations

import subprocess
import sys
import time
from pathlib import Path

import requests

sys.stdout.reconfigure(encoding="utf-8")


def main():
    """Test Import button with WinAppDriver."""
    print("=== Import Debug Test ===")

    # WinAppDriver should already be running from previous tests
    winappdriver_url = "http://127.0.0.1:4723"

    # Check WinAppDriver is running
    try:
        resp = requests.get(f"{winappdriver_url}/status", timeout=5)
        print(f"WinAppDriver status: {resp.status_code}")
    except Exception as e:
        print(f"WinAppDriver not running: {e}")
        print("Starting WinAppDriver...")
        subprocess.Popen(
            [r"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"],
            creationflags=subprocess.CREATE_NEW_CONSOLE,
        )
        time.sleep(3)

    # App path
    app_path = r"E:\VoiceStudio\src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"

    if not Path(app_path).exists():
        print(f"ERROR: App not found at {app_path}")
        return 1

    print(f"Launching app: {app_path}")

    # Create session
    caps = {
        "desiredCapabilities": {
            "app": app_path,
            "platformName": "Windows",
            "deviceName": "WindowsPC",
        }
    }

    try:
        resp = requests.post(f"{winappdriver_url}/session", json=caps, timeout=30)
        if resp.status_code != 200:
            print(f"Failed to create session: {resp.status_code} {resp.text}")
            return 1

        session_data = resp.json()
        session_id = session_data.get("sessionId") or session_data.get("value", {}).get("sessionId")
        print(f"Session ID: {session_id}")

        # Wait for app to load
        print("Waiting for app to load (7 seconds)...")
        time.sleep(7)

        # Get window title to confirm app is loaded
        try:
            resp = requests.get(f"{winappdriver_url}/session/{session_id}/title", timeout=10)
            title = resp.json().get("value", "")
            print(f"Window title: {title}")
        except Exception as e:
            print(f"Could not get title: {e}")

        # Try to find Import button by name
        print("\n--- Looking for Import button ---")
        find_payload = {"using": "name", "value": "Import"}
        resp = requests.post(
            f"{winappdriver_url}/session/{session_id}/element", json=find_payload, timeout=10
        )

        if resp.status_code == 200:
            element_data = resp.json()
            element_id = element_data.get("value", {}).get("ELEMENT")
            print(f"Found Import button, element ID: {element_id}")

            # Click Import button
            print("Clicking Import button...")
            click_resp = requests.post(
                f"{winappdriver_url}/session/{session_id}/element/{element_id}/click", timeout=10
            )
            print(f"Click response: {click_resp.status_code}")

            # Wait for dialog
            print("Waiting 3 seconds for dialog...")
            time.sleep(3)

            # Check for file dialog by looking for common dialog elements
            print("\n--- Checking for file dialog ---")

            # List all window handles
            resp = requests.get(
                f"{winappdriver_url}/session/{session_id}/window/handles", timeout=10
            )
            handles = resp.json().get("value", [])
            print(f"Window handles: {len(handles)}")

            for handle in handles:
                print(f"  Handle: {handle}")
                # Switch to window and check title
                try:
                    requests.post(
                        f"{winappdriver_url}/session/{session_id}/window",
                        json={"handle": handle},
                        timeout=5,
                    )
                    title_resp = requests.get(
                        f"{winappdriver_url}/session/{session_id}/title", timeout=5
                    )
                    window_title = title_resp.json().get("value", "")
                    print(f"    Title: {window_title}")
                except Exception as e:
                    print(f"    Error: {e}")

            # Try to find common dialog elements
            dialog_elements = ["Open", "Cancel", "File name:", "Files of type:"]
            for elem_name in dialog_elements:
                try:
                    find_resp = requests.post(
                        f"{winappdriver_url}/session/{session_id}/element",
                        json={"using": "name", "value": elem_name},
                        timeout=5,
                    )
                    if find_resp.status_code == 200:
                        print(f"Found dialog element: '{elem_name}'")
                # ALLOWED: bare except - best effort, failure acceptable
                except:
                    pass

            # Take screenshot
            screenshot_dir = Path(r"E:\VoiceStudio\.buildlogs\ui_tests\screenshots")
            screenshot_dir.mkdir(parents=True, exist_ok=True)
            screenshot_path = screenshot_dir / "import_debug_v2.png"

            try:
                resp = requests.get(
                    f"{winappdriver_url}/session/{session_id}/screenshot", timeout=10
                )
                if resp.status_code == 200:
                    import base64

                    screenshot_data = resp.json().get("value", "")
                    if screenshot_data:
                        with open(screenshot_path, "wb") as f:
                            f.write(base64.b64decode(screenshot_data))
                        print(f"Screenshot saved: {screenshot_path}")
            except Exception as e:
                print(f"Screenshot failed: {e}")

        else:
            print(f"Import button not found: {resp.status_code}")
            print(f"Response: {resp.text}")

            # Try XPath search
            print("\n--- Trying XPath search for Import ---")
            find_payload = {"using": "xpath", "value": "//*[contains(@Name, 'Import')]"}
            resp = requests.post(
                f"{winappdriver_url}/session/{session_id}/elements", json=find_payload, timeout=10
            )
            if resp.status_code == 200:
                elements = resp.json().get("value", [])
                print(f"Found {len(elements)} elements containing 'Import'")
                for i, elem in enumerate(elements):
                    elem_id = elem.get("ELEMENT")
                    # Get element name
                    name_resp = requests.get(
                        f"{winappdriver_url}/session/{session_id}/element/{elem_id}/attribute/Name",
                        timeout=5,
                    )
                    name = name_resp.json().get("value", "")
                    print(f"  [{i}] Name: {name}")

                # Click the first Import element found
                if elements:
                    element_id = elements[0].get("ELEMENT")
                    print("\n--- Clicking Import element ---")
                    click_resp = requests.post(
                        f"{winappdriver_url}/session/{session_id}/element/{element_id}/click",
                        timeout=10,
                    )
                    print(f"Click response: {click_resp.status_code}")

                    # Wait for dialog
                    print("Waiting 5 seconds for dialog...")
                    time.sleep(5)

                    # Check for file dialog
                    print("\n--- Checking for file dialog ---")

                    # List all window handles
                    resp = requests.get(
                        f"{winappdriver_url}/session/{session_id}/window/handles", timeout=10
                    )
                    handles = resp.json().get("value", [])
                    print(f"Window handles: {len(handles)}")

                    for handle in handles:
                        print(f"  Handle: {handle}")
                        try:
                            requests.post(
                                f"{winappdriver_url}/session/{session_id}/window",
                                json={"handle": handle},
                                timeout=5,
                            )
                            title_resp = requests.get(
                                f"{winappdriver_url}/session/{session_id}/title", timeout=5
                            )
                            window_title = title_resp.json().get("value", "")
                            print(f"    Title: {window_title}")
                        except Exception as e:
                            print(f"    Error: {e}")

                    # Try to find common dialog elements
                    dialog_elements = ["Open", "Cancel", "File name:", "Files of type:"]
                    for elem_name in dialog_elements:
                        try:
                            find_resp = requests.post(
                                f"{winappdriver_url}/session/{session_id}/element",
                                json={"using": "name", "value": elem_name},
                                timeout=5,
                            )
                            if find_resp.status_code == 200:
                                print(f"Found dialog element: '{elem_name}'")
                        # ALLOWED: bare except - best effort, failure acceptable
                        except:
                            pass

                    # Take screenshot
                    screenshot_dir = Path(r"E:\VoiceStudio\.buildlogs\ui_tests\screenshots")
                    screenshot_dir.mkdir(parents=True, exist_ok=True)
                    screenshot_path = screenshot_dir / "import_debug_v2.png"

                    try:
                        resp = requests.get(
                            f"{winappdriver_url}/session/{session_id}/screenshot", timeout=10
                        )
                        if resp.status_code == 200:
                            import base64

                            screenshot_data = resp.json().get("value", "")
                            if screenshot_data:
                                with open(screenshot_path, "wb") as f:
                                    f.write(base64.b64decode(screenshot_data))
                                print(f"Screenshot saved: {screenshot_path}")
                    except Exception as e:
                        print(f"Screenshot failed: {e}")

        # Clean up
        print("\nClosing session...")
        requests.delete(f"{winappdriver_url}/session/{session_id}", timeout=10)
        print("Done")
        return 0

    except Exception as e:
        print(f"Error: {e}")
        import traceback

        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
