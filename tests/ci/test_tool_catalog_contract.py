"""CI gate: Tool Catalog dialog contract.

Ensures ToolCatalogDialog exists, uses registry (GetAllDescriptors),
opens via panel ID (PanelDescriptor), and MainWindow integrates it.
Validates region chooser, filters, pin support, and SelectedRegion wiring.
"""
from __future__ import annotations

from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
TOOL_CATALOG_CS = ROOT / "src" / "VoiceStudio.App" / "Views" / "Dialogs" / "ToolCatalogDialog.cs"
MAIN_WINDOW_DIR = ROOT / "src" / "VoiceStudio.App"


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
    """MainWindow must wire Tool Catalog (inline or via launcher) and expose ShowToolCatalogAsync.

    Implementation may delegate dialog instantiation to ToolCatalogShellLauncher
    (shell-bridge pattern); the wiring is preserved as long as either MainWindow
    or the launcher service references ToolCatalogDialog.
    """
    main_cs = MAIN_WINDOW_DIR / "MainWindow.xaml.cs"
    launcher_cs = MAIN_WINDOW_DIR / "Services" / "ToolCatalogShellLauncher.cs"
    if not main_cs.exists():
        pytest.skip(f"MainWindow.xaml.cs not found: {main_cs}")
    main_content = main_cs.read_text(encoding="utf-8-sig")
    launcher_content = launcher_cs.read_text(encoding="utf-8-sig") if launcher_cs.exists() else ""
    combined = main_content + launcher_content
    assert "ToolCatalogDialog" in combined, (
        "MainWindow or ToolCatalogShellLauncher must reference ToolCatalogDialog"
    )
    assert "ShowToolCatalogAsync" in main_content, "MainWindow must have ShowToolCatalogAsync"


def test_tool_catalog_has_region_chooser() -> None:
    """ToolCatalogDialog must have region chooser (SelectedRegion wiring)."""
    if not TOOL_CATALOG_CS.exists():
        pytest.skip(f"ToolCatalogDialog.cs not found: {TOOL_CATALOG_CS}")
    content = TOOL_CATALOG_CS.read_text(encoding="utf-8-sig")
    assert "_regionChooser" in content or "SelectedRegion" in content, (
        "ToolCatalogDialog must have region chooser (_regionChooser or SelectedRegion)"
    )
    assert "is PanelRegion" in content, (
        "ToolCatalogDialog must use pattern match for SelectedRegion (is PanelRegion, not as)"
    )
    assert ("IndexFromContainer" in content or "OriginalSource" in content), (
        "ToolCatalogDialog must target right-clicked item (IndexFromContainer or OriginalSource)"
    )


def test_tool_catalog_has_category_filter() -> None:
    """ToolCatalogDialog must have category filter."""
    if not TOOL_CATALOG_CS.exists():
        pytest.skip(f"ToolCatalogDialog.cs not found: {TOOL_CATALOG_CS}")
    content = TOOL_CATALOG_CS.read_text(encoding="utf-8-sig")
    assert "_categoryFilter" in content, "ToolCatalogDialog must have _categoryFilter"


def test_tool_catalog_has_maturity_filter() -> None:
    """ToolCatalogDialog must have maturity filter."""
    if not TOOL_CATALOG_CS.exists():
        pytest.skip(f"ToolCatalogDialog.cs not found: {TOOL_CATALOG_CS}")
    content = TOOL_CATALOG_CS.read_text(encoding="utf-8-sig")
    assert "_maturityFilter" in content, "ToolCatalogDialog must have _maturityFilter"


def test_tool_catalog_has_pin_support() -> None:
    """ToolCatalogDialog must support pinning panels."""
    if not TOOL_CATALOG_CS.exists():
        pytest.skip(f"ToolCatalogDialog.cs not found: {TOOL_CATALOG_CS}")
    content = TOOL_CATALOG_CS.read_text(encoding="utf-8-sig")
    assert "IsPanelPinned" in content or "TogglePinnedPanel" in content, (
        "ToolCatalogDialog must have pin support (IsPanelPinned or TogglePinnedPanel)"
    )


def test_main_window_uses_selected_region() -> None:
    """MainWindow (or partials) must use dialog.SelectedRegion when opening from Tool Catalog."""
    main_files = list(MAIN_WINDOW_DIR.glob("MainWindow*.cs"))
    if not main_files:
        pytest.skip("No MainWindow*.cs files found")
    combined = ""
    for p in main_files:
        combined += p.read_text(encoding="utf-8-sig")
    assert "dialog.SelectedRegion" in combined, (
        "MainWindow must use dialog.SelectedRegion when opening panel from Tool Catalog"
    )


def test_tool_catalog_region_flows_to_open() -> None:
    """Prove Tool Catalog region selection flows through to OpenPanelByIdAsync (not bypassed)."""
    main_files = list(MAIN_WINDOW_DIR.glob("MainWindow*.cs"))
    if not main_files:
        pytest.skip("No MainWindow*.cs files found")
    combined = ""
    for p in main_files:
        combined += p.read_text(encoding="utf-8-sig")
    assert "dialog.SelectedRegion ?? desc.DefaultRegion" in combined, (
        "ShowToolCatalogAsync must use override cascade: dialog.SelectedRegion ?? desc.DefaultRegion"
    )
    assert "OpenPanelByIdAsync(desc.PanelId, region)" in combined, (
        "ShowToolCatalogAsync must pass region variable to OpenPanelByIdAsync, not desc.DefaultRegion"
    )
    assert "OpenPanelByIdAsync(desc.PanelId, desc.DefaultRegion)" not in combined, (
        "ShowToolCatalogAsync must NOT bypass SelectedRegion by passing desc.DefaultRegion directly"
    )
