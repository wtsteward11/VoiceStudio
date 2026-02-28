using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views;
using VoiceStudio.Core.Commands;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Event arguments for panel open requests from command palette.
  /// </summary>
  public class PanelOpenRequestedEventArgs : EventArgs
  {
    public string PanelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public PanelDescriptor? PanelDescriptor { get; set; }
  }

  /// <summary>
  /// Event arguments for help view requests from command palette.
  /// </summary>
  public class HelpViewRequestedEventArgs : EventArgs
  {
    public string ViewType { get; set; } = string.Empty; // "keymap", "help", etc.
  }

  public sealed class CommandPaletteService
  {
    private readonly IPanelRegistry _registry;
    private readonly ThemeManager _theme;
    private readonly List<CommandDescriptor> _moduleCommands = new();

    /// <summary>
    /// Event raised when a panel should be opened.
    /// MainWindow or PanelHost should subscribe to this to handle panel opening.
    /// </summary>
    public event EventHandler<PanelOpenRequestedEventArgs>? PanelOpenRequested;

    /// <summary>
    /// Event raised when a help view should be shown.
    /// MainWindow should subscribe to this to handle help view opening.
    /// </summary>
    public event EventHandler<HelpViewRequestedEventArgs>? HelpViewRequested;

    public CommandPaletteService(IPanelRegistry registry, ThemeManager themeManager)
    {
      _registry = registry ?? throw new ArgumentNullException(nameof(registry));
      _theme = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
    }

    public void Show()
    {
      var vm = new CommandPaletteViewModel(_registry);

      // Subscribe to command execution events
      vm.CommandExecuted += (_, e) => ExecuteCommand(e);

      var win = new CommandPaletteWindow();

      // Set DataContext via your DI/bootstrapper as needed.
      win.Content = new CommandPaletteView
      {
        DataContext = vm
      };

      win.Activate();
    }

    private void ExecuteCommand(VoiceStudio.App.ViewModels.CommandExecutedEventArgs e)
    {
      switch (e.Action)
      {
        case "open":
          // Find panel descriptor from registry
          var panel = _registry.GetPanelsForRegion(VoiceStudio.Core.Panels.PanelRegion.Left)
              .Concat(_registry.GetPanelsForRegion(VoiceStudio.Core.Panels.PanelRegion.Center))
              .Concat(_registry.GetPanelsForRegion(VoiceStudio.Core.Panels.PanelRegion.Right))
              .Concat(_registry.GetPanelsForRegion(VoiceStudio.Core.Panels.PanelRegion.Bottom))
              .FirstOrDefault(p => p.PanelId == e.Value);

          if (panel != null)
          {
            // Raise event for MainWindow/PanelHost to handle panel opening
            PanelOpenRequested?.Invoke(this, new PanelOpenRequestedEventArgs
            {
              PanelId = panel.PanelId,
              DisplayName = panel.DisplayName,
              // PanelDescriptor not available from IPanelView - MainWindow uses PanelId directly
              PanelDescriptor = null
            });
          }
          else
          {
            System.Diagnostics.Debug.WriteLine($"[Palette] Panel not found: {e.Value}");
          }
          break;

        case "theme":
          ApplyTheme(e.Value);
          break;

        case "density":
          ApplyDensity(e.Value);
          break;

        case "help":
          // Raise event for MainWindow to handle help view opening
          HelpViewRequested?.Invoke(this, new HelpViewRequestedEventArgs
          {
            ViewType = e.Value ?? "help"
          });
          break;

        default:
          System.Diagnostics.Debug.WriteLine($"[Palette] Unknown action: {e.Action}");
          break;
      }
    }

    // These are invoked by palette actions (wire in your action dispatcher)
    public void ApplyTheme(string name) => _theme.ApplyTheme(name);
    public void ApplyDensity(string name) => _theme.ApplyLayoutDensity(name);

    /// <summary>
    /// Registers commands from UI modules for the command palette.
    /// Called by AppServices after module initialization.
    /// </summary>
    /// <param name="commands">Commands from ModuleLoader.GetAllCommands()</param>
    public void RegisterCommands(IEnumerable<CommandDescriptor> commands)
    {
      _moduleCommands.AddRange(commands);
    }

    /// <summary>
    /// Gets all registered module commands.
    /// </summary>
    public IReadOnlyList<CommandDescriptor> GetModuleCommands() => _moduleCommands.AsReadOnly();
  }
}