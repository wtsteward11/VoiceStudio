using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Windows.Foundation;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for managing contextual right-click menus.
  /// Implements IDEA 10: Contextual Right-Click Menus for All Interactive Elements.
  /// </summary>
  public class ContextMenuService
  {
    /// <summary>
    /// Creates a context menu for a given context type.
    /// </summary>
    public MenuFlyout CreateContextMenu(string contextType, object? contextData = null)
    {
      var menu = new MenuFlyout();

      switch (contextType.ToLower())
      {
        case "timeline":
          AddTimelineMenuItems(menu, contextData);
          break;
        case "profile":
          AddProfileMenuItems(menu, contextData);
          break;
        case "audio":
          AddAudioMenuItems(menu, contextData);
          break;
        case "effect":
          AddEffectMenuItems(menu, contextData);
          break;
        case "track":
          AddTrackMenuItems(menu, contextData);
          break;
        case "clip":
          AddClipMenuItems(menu, contextData);
          break;
        case "marker":
          AddMarkerMenuItems(menu, contextData);
          break;
        default:
          AddDefaultMenuItems(menu, contextData);
          break;
      }

      return menu;
    }

    private void AddTimelineMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("Add Track", "Ctrl+T", null));
      menu.Items.Add(CreateMenuItem("Add Marker", "M", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Snap to Grid", null, null, true));
      menu.Items.Add(CreateMenuItem("Zoom In", "Ctrl+Plus", null));
      menu.Items.Add(CreateMenuItem("Zoom Out", "Ctrl+Minus", null));
      menu.Items.Add(CreateMenuItem("Zoom to Fit", "Ctrl+0", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Cut", "Ctrl+X", null));
      menu.Items.Add(CreateMenuItem("Copy", "Ctrl+C", null));
      menu.Items.Add(CreateMenuItem("Paste", "Ctrl+V", null));
    }

    private void AddProfileMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("New Profile", "Ctrl+N", null));
      menu.Items.Add(CreateMenuItem("Import Profile", null, null));
      menu.Items.Add(CreateMenuItem("Export Profile", null, null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Test Voice", "F5", null));
      menu.Items.Add(CreateMenuItem("Analyze Quality", null, null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Edit", "F2", null));
      menu.Items.Add(CreateMenuItem("Duplicate", "Ctrl+D", null));
      menu.Items.Add(CreateMenuItem("Delete", "Delete", null));
    }

    private void AddAudioMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("Play", "Space", null));
      menu.Items.Add(CreateMenuItem("Stop", "Space", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Export", "Ctrl+E", null));
      menu.Items.Add(CreateMenuItem("Analyze", null, null));
      menu.Items.Add(CreateMenuItem("Apply Effects", null, null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Properties", "Alt+Enter", null));
      menu.Items.Add(CreateMenuItem("Delete", "Delete", null));
    }

    private void AddEffectMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("Edit Parameters", "Enter", null));
      menu.Items.Add(CreateMenuItem("Bypass", "B", null, true));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Move Up", "Ctrl+Up", null));
      menu.Items.Add(CreateMenuItem("Move Down", "Ctrl+Down", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Duplicate", "Ctrl+D", null));
      menu.Items.Add(CreateMenuItem("Remove", "Delete", null));
    }

    private void AddTrackMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("Add Clip", null, null));
      menu.Items.Add(CreateMenuItem("Add Effect", null, null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Mute", "M", null, true));
      menu.Items.Add(CreateMenuItem("Solo", "S", null, true));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Rename", "F2", null));
      menu.Items.Add(CreateMenuItem("Delete", "Delete", null));
    }

    private void AddClipMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("Play", "Space", null));
      menu.Items.Add(CreateMenuItem("Edit", "Enter", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Split at playhead", null, null));
      menu.Items.Add(CreateMenuItem("Trim start to playhead", null, null));
      menu.Items.Add(CreateMenuItem("Trim end to playhead", null, null));
      menu.Items.Add(CreateMenuItem("Fade in 0.5s", null, null));
      menu.Items.Add(CreateMenuItem("Fade out 0.5s", null, null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Cut", "Ctrl+X", null));
      menu.Items.Add(CreateMenuItem("Copy", "Ctrl+C", null));
      menu.Items.Add(CreateMenuItem("Paste", "Ctrl+V", null));
      menu.Items.Add(CreateMenuItem("Duplicate", "Ctrl+D", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Properties", "Alt+Enter", null));
      menu.Items.Add(CreateMenuItem("Delete", "Delete", null));
    }

    private void AddMarkerMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("Go To", "Enter", null));
      menu.Items.Add(CreateMenuItem("Edit", "F2", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Delete", "Delete", null));
    }

    private void AddDefaultMenuItems(MenuFlyout menu, object? _)
    {
      menu.Items.Add(CreateMenuItem("Copy", "Ctrl+C", null));
      menu.Items.Add(CreateMenuItem("Paste", "Ctrl+V", null));
      menu.Items.Add(new MenuFlyoutSeparator());
      menu.Items.Add(CreateMenuItem("Properties", "Alt+Enter", null));
    }

    /// <summary>
    /// Creates a menu flyout item with optional keyboard shortcut and toggle state.
    /// </summary>
    private MenuFlyoutItem CreateMenuItem(string text, string? keyboardShortcut, ICommand? command, bool isToggle = false)
    {
      MenuFlyoutItem item;

      if (isToggle)
      {
        item = new ToggleMenuFlyoutItem
        {
          Text = text,
          IsChecked = false
        };
      }
      else
      {
        item = new MenuFlyoutItem
        {
          Text = text,
          Command = command
        };
      }

      // Add keyboard shortcut to tooltip
      // Note: MenuFlyoutItem doesn't support ToolTipService in WinUI 3
      // Tooltip information is available via AutomationProperties
      if (!string.IsNullOrEmpty(keyboardShortcut))
      {
        AutomationProperties.SetHelpText(item, $"{text} ({keyboardShortcut})");
      }
      else
      {
        AutomationProperties.SetHelpText(item, text);
      }

      AutomationProperties.SetName(item, text);

      return item;
    }

    /// <summary>
    /// Shows a context menu at the specified position.
    /// </summary>
    public void ShowContextMenu(MenuFlyout menu, UIElement target, Point position)
    {
      menu.ShowAt(target, position);
    }

    /// <summary>
    /// Shows a context menu at the cursor position.
    /// </summary>
    public void ShowContextMenuAtCursor(MenuFlyout menu, UIElement target)
    {
      // Get cursor position (simplified - in real implementation, get from pointer event)
      if (target is FrameworkElement frameworkElement)
      {
        menu.ShowAt(frameworkElement);
      }
    }
  }
}