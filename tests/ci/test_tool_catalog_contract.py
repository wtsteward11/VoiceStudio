"""CI gate: Tool Catalog dialog contract.

Ensures ToolCatalogDialog exists, uses registry (GetAllDescriptors),
opens via panel ID (PanelDescriptor), and MainWindow integrates it.
"""
from __future__ import annotations

from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
TOOL_CATALOG_CS = ROOT / "src" / "VoiceStudio.App" / "Views" / "Dialogs" / "ToolCatalogDialog.cs"
MAIN_WINDOW_CS = ROOT / "src" / "VoiceStudio.App" / "MainWindow.xaml.cs"


def test_tool_catalog_dialog_exists() -> None:
    """ToolCatalogDialog.cs must exist."""
    assert TOOL_CATALOG_CS.exists(), f"ToolCatalogDialog.cs not found: {TOOL_CATALOG_CS}"


def test_tool_catalog_uses_registry() -> None:
    """ToolCatalogDialog must use GetAllDescriptors (registry, not hardcoded panels)."""
    if not TOOL_CATALOG_CS.exists():
        pytest.skip(f"ToolCatalogDialog.cs not found: {TOOL_CATALOG_CS}")
    content = TOOL_CATALOG_CS.read_text(encoding="utf-8-sig")
    assert "GetAllDescriptors" in content, "ToolCatalogDialog must use GetAllDescriptors from registry"


def test_tool_catalog_uses_panel_descriptor() -> None:
    """ToolCatalogDialog must store/use PanelDescriptor (opens via ID, not direct instantiation)."""
    if not TOOL_CATALOG_CS.exists():
        pytest.skip(f"ToolCatalogDialog.cs not found: {TOOL_CATALOG_CS}")
    content = TOOL_CATALOG_CS.read_text(encoding="utf-8-sig")
    assert "PanelDescriptor" in content, "ToolCatalogDialog must use PanelDescriptor"
    assert "SelectedDescriptor" in content, "ToolCatalogDialog must expose SelectedDescriptor"


def test_main_window_references_tool_catalog() -> None:
    """MainWindow must reference ToolCatalogDialog and ShowToolCatalogAsync."""
    if not MAIN_WINDOW_CS.exists():
        pytest.skip(f"MainWindow.xaml.cs not found: {MAIN_WINDOW_CS}")
    content = MAIN_WINDOW_CS.read_text(encoding="utf-8-sig")
    assert "ToolCatalogDialog" in content, "MainWindow must reference ToolCatalogDialog"
    assert "ShowToolCatalogAsync" in content, "MainWindow must have ShowToolCatalogAsync"
