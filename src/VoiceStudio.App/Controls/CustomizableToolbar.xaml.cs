using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Controls
{
  public sealed partial class CustomizableToolbar : UserControl
  {
    private ToolbarConfigurationService? _toolbarService;
    private readonly Dictionary<string, UIElement> _toolbarButtons = new Dictionary<string, UIElement>();

    /// <summary>Ordered list of (DisplayName, ProfileId) for workspace combo. Single source of truth.</summary>
    private static readonly (string DisplayName, string ProfileId)[] WorkspaceList =
    {
      ("Studio", "studio"),
      ("Recording", "recording"),
      ("Mixing", "mixing"),
      ("Synthesis", "synthesis"),
      ("Training", "training"),
      ("Analysis", "analysis"),
      ("Batch Lab", "batch_lab"),
      ("Pro Mix", "pro_mix")
    };

    private ComboBox? _workspaceComboBox;
    private bool _updatingWorkspaceComboFromProfile;
    private bool _workspaceProfileChangedSubscribed;

    private static Style? TryGetFocusStyle()
    {
      try
      {
        return Application.Current?.Resources?["VSQ.Button.FocusStyle"] as Style;
      }
      catch
      {
        return null;
      }
    }

    public CustomizableToolbar()
    {
      this.InitializeComponent();
      _toolbarService = ServiceProvider.TryGetToolbarConfigurationService();

      if (_toolbarService != null)
      {
        _toolbarService.ConfigurationChanged += ToolbarService_ConfigurationChanged;
        LoadToolbar();
      }
    }

    private void ToolbarService_ConfigurationChanged(object? sender, ToolbarConfigurationChangedEventArgs e)
    {
      LoadToolbar();
    }

    private void LoadToolbar()
    {
      if (_toolbarService == null)
        return;

      var config = _toolbarService.GetConfiguration();

      // Clear existing buttons
      TransportSection.Children.Clear();
      ProjectSection.Children.Clear();
      HistorySection.Children.Clear();
      PerformanceSection.Children.Clear();

      // Group items by section and order
      var visibleItems = config.Items
          .Where(i => i.IsVisible)
          .OrderBy(i => i.Order)
          .ToList();

      foreach (var item in visibleItems)
      {
        var button = CreateToolbarButton(item);
        if (button != null)
        {
          AddButtonToSection(item.Section, button);
        }
      }
    }

    private UIElement? CreateToolbarButton(ToolbarItem item)
    {
      // Create appropriate control based on item ID
      return item.Id switch
      {
        "play" => CreateButton("▶", "Play (Space)", item),
        "pause" => CreateButton("⏸", "Pause (Space)", item),
        "stop" => CreateButton("⏹", "Stop", item),
        "record" => CreateButton("⏺", "Record (Ctrl+R)", item),
        "loop" => CreateToggleButton("Loop", "Toggle loop playback", item),
        "project" => CreateProjectControl(item),
        "import_audio" => CreateButton("📥 Import", "Import Audio File (Ctrl+I)", item),
        "engine" => CreateEngineControl(item),
        "undo" => CreateButton("Undo", "Undo last action (Ctrl+Z)", item),
        "redo" => CreateButton("Redo", "Redo last action (Ctrl+Y)", item),
        "workspace" => CreateWorkspaceControl(item),
        "cpu" => CreatePerformanceControl("CPU", item),
        "gpu" => CreatePerformanceControl("GPU", item),
        "latency" => CreatePerformanceControl("Latency", item),
        _ => null
      };
    }

    private Button CreateButton(string content, string tooltip, ToolbarItem item)
    {
      var button = new Button
      {
        Content = content,
        Margin = new Thickness(0, 0, 4, 0),
        Style = TryGetFocusStyle()
      };
      if (!string.IsNullOrEmpty(tooltip))
      {
        ToolTipService.SetToolTip(button, tooltip);
      }

      // Wire up click handler based on item ID
      button.Click += (_, _) => HandleToolbarButtonClick(item.Id);

      return button;
    }

    private ToggleButton CreateToggleButton(string content, string tooltip, ToolbarItem item)
    {
      var button = new ToggleButton
      {
        Content = content,
        Margin = new Thickness(8, 0, 0, 0),
        Style = TryGetFocusStyle()
      };
      if (!string.IsNullOrEmpty(tooltip))
      {
        ToolTipService.SetToolTip(button, tooltip);
      }

      // Wire up click handler based on item ID
      button.Click += (_, _) => HandleToolbarButtonClick(item.Id);

      return button;
    }

    private StackPanel CreateProjectControl(ToolbarItem _)
    {
      var panel = new StackPanel { Orientation = Orientation.Horizontal };
      panel.Children.Add(new TextBlock { Text = "Project:", Margin = new Thickness(0, 0, 4, 0) });
      panel.Children.Add(new TextBox { Width = 200, Text = "Untitled Project" });
      return panel;
    }

    private StackPanel CreateEngineControl(ToolbarItem _)
    {
      var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
      panel.Children.Add(new TextBlock { Text = "Engine:", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
      var comboBox = new ComboBox { Width = 140 };
      comboBox.Items.Add("XTTS v2");
      comboBox.Items.Add("OpenVoice");
      comboBox.Items.Add("RVC");
      comboBox.Items.Add("Piper");
      comboBox.Items.Add("Bark");
      comboBox.SelectedIndex = 0; // Default to first engine

      // Wire up selection changed to switch engines
      comboBox.SelectionChanged += (sender, e) =>
      {
        if (sender is ComboBox cb && cb.SelectedItem is string engineName)
        {
          var toastService = ServiceProvider.TryGetToastNotificationService();
          toastService?.ShowInfo("Engine", $"Selected: {engineName}");
          System.Diagnostics.Debug.WriteLine($"Engine switch requested: {engineName}");
        }
      };

      panel.Children.Add(comboBox);
      return panel;
    }

    private StackPanel CreateWorkspaceControl(ToolbarItem _)
    {
      var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
      panel.Children.Add(new TextBlock { Text = "Workspace:", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
      var comboBox = new ComboBox { Width = 150 };
      AutomationProperties.SetName(comboBox, "Workspace");
      AutomationProperties.SetHelpText(comboBox, "Select workspace layout: Studio, Recording, Mixing, Synthesis, Training, Analysis, Batch Lab, or Pro Mix.");

      foreach (var (displayName, _) in WorkspaceList)
        comboBox.Items.Add(displayName);

      SyncWorkspaceComboToCurrentProfile(comboBox);
      _workspaceComboBox = comboBox;

      comboBox.SelectionChanged += async (sender, e) =>
      {
        if (_updatingWorkspaceComboFromProfile)
          return;
        if (sender is ComboBox cb && cb.SelectedItem is string displayName)
        {
          var profileId = GetProfileIdFromDisplayName(displayName);
          if (!string.IsNullOrEmpty(profileId))
            await SwitchWorkspaceAsync(profileId);
        }
      };

      EnsureWorkspaceProfileChangedSubscription();
      panel.Children.Add(comboBox);
      return panel;
    }

    private static string? GetProfileIdFromDisplayName(string displayName)
    {
      foreach (var (name, id) in WorkspaceList)
        if (string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase))
          return id;
      return null;
    }

    private static string GetDisplayNameFromProfileId(string profileId)
    {
      if (string.IsNullOrEmpty(profileId))
        return WorkspaceList[0].DisplayName;
      foreach (var (name, id) in WorkspaceList)
        if (string.Equals(id, profileId, StringComparison.OrdinalIgnoreCase))
          return name;
      return profileId.Replace("_", " ");
    }

    private void SyncWorkspaceComboToCurrentProfile(ComboBox? combo)
    {
      if (combo == null)
        return;
      var panelStateService = ServiceProvider.GetPanelStateService();
      var currentProfile = panelStateService?.CurrentWorkspaceProfile ?? "studio";
      var displayName = GetDisplayNameFromProfileId(currentProfile);
      _updatingWorkspaceComboFromProfile = true;
      try
      {
        var idx = -1;
        for (var i = 0; i < combo.Items.Count; i++)
        {
          if (combo.Items[i] is string s && string.Equals(s, displayName, StringComparison.OrdinalIgnoreCase))
          {
            idx = i;
            break;
          }
        }
        if (idx >= 0)
          combo.SelectedIndex = idx;
        else
          combo.SelectedIndex = 0;
      }
      finally
      {
        _updatingWorkspaceComboFromProfile = false;
      }
    }

    private bool _workspaceFallbackSubscribed;

    private void EnsureWorkspaceProfileChangedSubscription()
    {
      if (_workspaceProfileChangedSubscribed)
        return;
      var panelStateService = ServiceProvider.GetPanelStateService();
      if (panelStateService == null)
        return;
      panelStateService.WorkspaceProfileChanged += OnWorkspaceProfileChanged;
      if (!_workspaceFallbackSubscribed)
      {
        panelStateService.WorkspaceFallbackToEmpty += OnWorkspaceFallbackToEmpty;
        _workspaceFallbackSubscribed = true;
      }
      _workspaceProfileChangedSubscribed = true;
    }

    private void OnWorkspaceFallbackToEmpty(object? sender, WorkspaceFallbackToEmptyEventArgs e)
    {
      var displayName = GetDisplayNameFromProfileId(e.ProfileName);
      var toastService = ServiceProvider.TryGetToastNotificationService();
      toastService?.ShowWarning("Workspace", $"Workspace '{displayName}' has no saved layout; using empty layout.");
    }

    private void OnWorkspaceProfileChanged(object? sender, WorkspaceProfileChangedEventArgs e)
    {
      if (_workspaceComboBox == null)
        return;
      this.DispatcherQueue.TryEnqueue(() =>
      {
        SyncWorkspaceComboToCurrentProfile(_workspaceComboBox);
      });
    }

    /// <summary>
    /// Switch to a different workspace.
    /// </summary>
    private async System.Threading.Tasks.Task SwitchWorkspaceAsync(string workspaceId)
    {
      try
      {
        var panelStateService = ServiceProvider.GetPanelStateService();
        var toastService = ServiceProvider.TryGetToastNotificationService();

        if (panelStateService != null)
        {
          // Switch workspace using PanelStateService
          var success = await panelStateService.SwitchWorkspaceProfileAsync(workspaceId);
          if (success)
          {
            toastService?.ShowSuccess("Workspace", $"Switched to: {GetDisplayNameFromProfileId(workspaceId)}");
          }
          else
          {
            toastService?.ShowWarning("Workspace", $"Workspace '{GetDisplayNameFromProfileId(workspaceId)}' created (default layout)");
          }
        }
        else
        {
          toastService?.ShowInfo("Workspace", $"Selected: {GetDisplayNameFromProfileId(workspaceId)}");
        }

        System.Diagnostics.Debug.WriteLine($"Workspace switch: {workspaceId}");
      }
      catch (Exception ex)
      {
        var toastService = ServiceProvider.TryGetToastNotificationService();
        toastService?.ShowError("Workspace Error", ex.Message);
        System.Diagnostics.Debug.WriteLine($"Workspace switch error: {ex}");
      }
    }

    private StackPanel CreatePerformanceControl(string label, ToolbarItem _)
    {
      var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
      panel.Children.Add(new TextBlock
      {
        Text = label,
        FontSize = 10,
        HorizontalAlignment = HorizontalAlignment.Right
      });
      panel.Children.Add(new ProgressBar
      {
        Width = 80,
        Height = 6,
        Value = label == "CPU" ? 20 : label == "GPU" ? 10 : 5
      });
      return panel;
    }

    private void AddButtonToSection(ToolbarSection section, UIElement button)
    {
      switch (section)
      {
        case ToolbarSection.Transport:
          TransportSection.Children.Add(button);
          break;
        case ToolbarSection.Project:
          ProjectSection.Children.Add(button);
          break;
        case ToolbarSection.History:
        case ToolbarSection.Workspace:
          HistorySection.Children.Add(button);
          break;
        case ToolbarSection.Performance:
          PerformanceSection.Children.Add(button);
          break;
      }
    }

    /// <summary>
    /// Handles toolbar button clicks by executing the appropriate command.
    /// </summary>
    private void HandleToolbarButtonClick(string itemId)
    {
      // Log to file for debugging
      var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceStudio", "import_debug.log");
      void Log(string msg)
      {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        // ALLOWED: empty catch - Best effort debug logging, failure is acceptable
        try { System.IO.File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
      }
      
      Log($"[Toolbar] HandleToolbarButtonClick called: {itemId}");
      
      // Handle special cases directly first (before keyboard shortcuts)
      switch (itemId)
      {
        case "import_audio":
          Log("[Toolbar] import_audio case matched");
          // Call MainWindow's working ImportAudioFile method
          if (App.MainWindowInstance is MainWindow mainWindow)
          {
            Log("[Toolbar] MainWindowInstance available, calling ImportAudioFile");
            mainWindow.ImportAudioFile();
          }
          else
          {
            Log($"[Toolbar] MainWindowInstance NOT available: {App.MainWindowInstance?.GetType().Name ?? "null"}");
            var toastService = ServiceProvider.TryGetToastNotificationService();
            toastService?.ShowError("Import Error", "MainWindow not available");
            System.Diagnostics.Debug.WriteLine($"Import failed: App.MainWindowInstance is {App.MainWindowInstance?.GetType().Name ?? "null"}");
          }
          return;
        case "loop":
          // Toggle loop on the canonical audio player service (Audit M-5)
          try
          {
            var audioPlayer = ServiceProvider.GetAudioPlayerService();
            audioPlayer.IsLooping = !audioPlayer.IsLooping;
            var toastSvc = ServiceProvider.TryGetToastNotificationService();
            toastSvc?.ShowInfo("Loop", audioPlayer.IsLooping ? "Loop playback enabled" : "Loop playback disabled");
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine($"[Toolbar] Loop toggle failed: {ex.Message}");
          }
          return;
      }

      // Map toolbar item IDs to command IDs and execute via KeyboardShortcutService
      var keyboardService = ServiceProvider.TryGetKeyboardShortcutService();

      // Map toolbar item IDs to command IDs
      var commandId = itemId switch
      {
        "play" => "playback.play",
        "pause" => "playback.play", // Toggle play/pause (same as play)
        "stop" => "playback.stop",
        "record" => "playback.record",
        "undo" => "edit.undo",
        "redo" => "edit.redo",
        _ => null
      };

      if (commandId != null && keyboardService != null)
      {
        // Execute the command through the keyboard shortcut service
        keyboardService.ExecuteShortcut(commandId);
      }
    }

  }
}