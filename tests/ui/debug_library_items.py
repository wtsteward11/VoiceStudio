"""
Debug script to investigate library items visibility in UI.
"""

from __future__ import annotations

import time
from pathlib import Path

import requests


# Simple driver that directly talks to WinAppDriver
class SimpleDriver:
    """Direct HTTP client for WinAppDriver."""

    def __init__(self, winappdriver_url: str = "http://127.0.0.1:4723"):
        self.url = winappdriver_url
        self.session_id = None

    def create_session(self, app_path: str):
        """Create a WinAppDriver session."""
        caps = {
            "platformName": "Windows",
            "app": app_path,
            "appTopLevelWindow": None,
            "ms:waitForAppLaunch": "10",
            "deviceName": "WindowsPC",
        }

        response = requests.post(
            f"{self.url}/session", json={"capabilities": {"alwaysMatch": caps}}
        )
        response.raise_for_status()
        data = response.json()
        self.session_id = data.get("value", {}).get("sessionId") or data.get("sessionId")
        print(f"Session created: {self.session_id}")
        return self.session_id

    def close(self):
        """Close the session."""
        if self.session_id:
            try:
                requests.delete(f"{self.url}/session/{self.session_id}")
                print(f"Session closed: {self.session_id}")
            except Exception as e:
                print(f"Error closing session: {e}")
            self.session_id = None

    def find_element(self, strategy: str, value: str):
        """Find a single element."""
        strat_map = {
            "accessibility id": "accessibility id",
            "xpath": "xpath",
            "name": "name",
            "class name": "class name",
        }

        response = requests.post(
            f"{self.url}/session/{self.session_id}/element",
            json={"using": strat_map.get(strategy, strategy), "value": value},
        )
        response.raise_for_status()
        data = response.json()
        element_id = next(iter(data.get("value", {}).values())) if data.get("value") else None
        return Element(self, element_id) if element_id else None

    def find_elements(self, strategy: str, value: str):
        """Find multiple elements."""
        strat_map = {
            "accessibility id": "accessibility id",
            "xpath": "xpath",
            "name": "name",
            "class name": "class name",
        }

        response = requests.post(
            f"{self.url}/session/{self.session_id}/elements",
            json={"using": strat_map.get(strategy, strategy), "value": value},
        )
        response.raise_for_status()
        data = response.json()
        elements = []
        for elem in data.get("value", []):
            elem_id = next(iter(elem.values())) if elem else None
            if elem_id:
                elements.append(Element(self, elem_id))
        return elements

    def get_page_source(self):
        """Get the page source (XML)."""
        response = requests.get(f"{self.url}/session/{self.session_id}/source")
        response.raise_for_status()
        return response.json().get("value", "")


class Element:
    """Simple element wrapper."""

    def __init__(self, driver, element_id):
        self.driver = driver
        self.element_id = element_id

    def click(self):
        """Click the element."""
        response = requests.post(
            f"{self.driver.url}/session/{self.driver.session_id}/element/{self.element_id}/click"
        )
        response.raise_for_status()

    def get_attribute(self, name: str):
        """Get attribute value."""
        response = requests.get(
            f"{self.driver.url}/session/{self.driver.session_id}/element/{self.element_id}/attribute/{name}"
        )
        response.raise_for_status()
        return response.json().get("value")

    def find_elements(self, strategy: str, value: str):
        """Find child elements."""
        strat_map = {"xpath": "xpath", "class name": "class name"}

        response = requests.post(
            f"{self.driver.url}/session/{self.driver.session_id}/element/{self.element_id}/elements",
            json={"using": strat_map.get(strategy, strategy), "value": value},
        )
        response.raise_for_status()
        data = response.json()
        elements = []
        for elem in data.get("value", []):
            elem_id = next(iter(elem.values())) if elem else None
            if elem_id:
                elements.append(Element(self.driver, elem_id))
        return elements


def main():
    app_path = r"E:\VoiceStudio\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"

    print("=" * 60)
    print("Library Items Debug Script")
    print("=" * 60)

    # Check backend
    print("\n1. Checking backend API...")
    try:
        response = requests.get("http://localhost:8000/api/library/assets")
        data = response.json()
        print(f"   Backend has {data.get('total', 0)} assets")
        for asset in data.get("assets", [])[:5]:
            print(f"   - {asset.get('name')}")
    except Exception as e:
        print(f"   Backend error: {e}")
        return

    # Create WinAppDriver session
    print("\n2. Creating WinAppDriver session...")
    driver = SimpleDriver()

    try:
        driver.create_session(app_path)
        time.sleep(5)  # Wait for app to fully load

        # Navigate to Library
        print("\n3. Navigating to Library panel...")
        try:
            nav_btn = driver.find_element("accessibility id", "MainNav_Library")
            if nav_btn:
                nav_btn.click()
                print("   Clicked Library nav button")
                time.sleep(2)  # Wait for panel to load
            else:
                print("   Could not find Library nav button")
        except Exception as e:
            print(f"   Navigation error: {e}")

        # Wait for library to load items
        print("\n4. Waiting for library items to load...")
        time.sleep(3)  # Give backend time to respond

        # Try to find assets list
        print("\n5. Looking for AssetsListView...")
        try:
            assets_list = driver.find_element("accessibility id", "LibraryView_AssetsListView")
            if assets_list:
                print("   Found LibraryView_AssetsListView")
                auto_id = assets_list.get_attribute("AutomationId")
                name = assets_list.get_attribute("Name")
                print(f"   AutomationId: {auto_id}")
                print(f"   Name: {name}")
            else:
                print("   LibraryView_AssetsListView NOT found")
        except Exception as e:
            print(f"   Error finding AssetsListView: {e}")

        # Try different XPath strategies
        print("\n6. Trying different XPath strategies...")

        strategies = [
            ("//List[@AutomationId='LibraryView_AssetsListView']//ListItem", "List/ListItem"),
            ("//*[@ClassName='ListViewItem']", "ClassName ListViewItem"),
            ("//*[@LocalizedControlType='list item']", "LocalizedControlType list item"),
            (
                "//List[@AutomationId='LibraryView_AssetsListView']//*",
                "All children of AssetsListView",
            ),
        ]

        for xpath, desc in strategies:
            try:
                items = driver.find_elements("xpath", xpath)
                print(f"   {desc}: {len(items)} elements")
                if items and len(items) > 0:
                    for i, item in enumerate(items[:3]):
                        try:
                            name = item.get_attribute("Name")
                            print(f"      [{i}] Name: {name[:50] if name else 'N/A'}")
                        except Exception:
                            print(f"      [{i}] (could not read)")
            except Exception as e:
                print(f"   {desc}: Error - {e}")

        # Save page source for analysis
        print("\n7. Saving page source for analysis...")
        try:
            source = driver.get_page_source()
            output_path = Path(r"E:\VoiceStudio\.buildlogs\ui_tests\library_debug.xml")
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(source, encoding="utf-8")
            print(f"   Saved to {output_path}")

            # Check if AssetsListView is in the source
            if "LibraryView_AssetsListView" in source:
                print("   LibraryView_AssetsListView found in source")
            if "Allan Watts" in source:
                print("   'Allan Watts' text found in source")
            if "ListViewItem" in source:
                count = source.count("ListViewItem")
                print(f"   ListViewItem appears {count} times")
        except Exception as e:
            print(f"   Error saving source: {e}")

    except Exception as e:
        print(f"Error: {e}")
        import traceback

        traceback.print_exc()
    finally:
        print("\n8. Cleaning up...")
        driver.close()
        print("Done!")


if __name__ == "__main__":
    main()
