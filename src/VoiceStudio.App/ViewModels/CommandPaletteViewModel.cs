using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.ViewModels
{
  public sealed partial class CommandPaletteViewModel : BaseViewModel
  {
    [ObservableProperty] private string filterText = string.Empty;

    public ObservableCollection<CommandItem> Items { get; } = new();
    public ObservableCollection<CommandItem> FilteredItems { get; } = new();

    public CommandItem? SelectedItem { get; set; }

    public IRelayCommand RunSelectedCmd { get; }
    public IRelayCommand<string?> RunByIdCmd { get; }

    private readonly IPanelRegistry _panelRegistry;
    private readonly IUnifiedCommandRegistry? _commandRegistry;

    /// <summary>
    /// Event raised when a command is executed.
    /// </summary>
    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public CommandPaletteViewModel(IPanelRegistry panelRegistry)
      : this(panelRegistry, AppServices.TryGetCommandRegistry())
    {
    }

    public CommandPaletteViewModel(IPanelRegistry panelRegistry, IUnifiedCommandRegistry? commandRegistry)
        : base(AppServices.GetViewModelContext())
    {
      _panelRegistry = panelRegistry;
      _commandRegistry = commandRegistry;
      RunSelectedCmd = new RelayCommand(() => Run(SelectedItem?.Id));
      RunByIdCmd = new RelayCommand<string?>(Run);

      LoadDefaultItems();
      LoadRegistryCommands();
      ApplyFilter();

      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(FilterText))
          ApplyFilter();
      };
    }

    void LoadDefaultItems()
    {
      // Load panels from all regions
      foreach (var region in Enum.GetValues<PanelRegion>())
      {
        foreach (var d in _panelRegistry.GetPanelsForRegion(region))
        {
          Items.Add(new CommandItem
          {
            Id = "open:" + d.PanelId,
            Title = "Open " + d.DisplayName,
            Kind = "Panel"
          });
        }
      }

      Items.Add(new CommandItem { Id = "help:keymap", Title = "Show keybindings", Kind = "System", Shortcut = "Ctrl+/" });
      Items.Add(new CommandItem { Id = "theme:Dark", Title = "Theme: Dark", Kind = "Theme" });
      Items.Add(new CommandItem { Id = "theme:SciFi", Title = "Theme: Sci-Fi", Kind = "Theme" });
      Items.Add(new CommandItem { Id = "theme:Light", Title = "Theme: Light", Kind = "Theme" });
      Items.Add(new CommandItem { Id = "density:Compact", Title = "Density: Compact", Kind = "Theme" });
      Items.Add(new CommandItem { Id = "density:Comfort", Title = "Density: Comfort", Kind = "Theme" });
    }

    void LoadRegistryCommands()
    {
      if (_commandRegistry == null)
      {
        Debug.WriteLine("[CommandPalette] Command registry not available");
        return;
      }

      // Get all registered commands from the unified registry
      var commands = _commandRegistry.GetAllCommands();
      Debug.WriteLine($"[CommandPalette] Loading {commands.Count} commands from registry");

      foreach (var descriptor in commands)
      {
        // Skip commands already added from panels
        if (Items.Any(i => i.Id == descriptor.Id))
          continue;

        // Map category to Kind
        var kind = descriptor.Category switch
        {
          "file" => "File",
          "profile" => "Profile",
          "playback" => "Playback",
          "nav" or "navigation" => "Navigation",
          "settings" => "Settings",
          _ => "Command"
        };

        Items.Add(new CommandItem
        {
          Id = descriptor.Id,
          Title = descriptor.Title,
          Kind = kind,
          Shortcut = descriptor.KeyboardShortcut,
          IsRegistryCommand = true
        });
      }
    }

    void ApplyFilter()
    {
      var q = (FilterText ?? string.Empty).Trim().ToLowerInvariant();
      var src = string.IsNullOrEmpty(q)
          ? Items
          : new ObservableCollection<CommandItem>(
              Items.Where(i => (i.Title ?? "").ToLowerInvariant().Contains(q))
          );

      FilteredItems.Clear();
      foreach (var it in src)
        FilteredItems.Add(it);

      if (FilteredItems.Count > 0)
        SelectedItem = FilteredItems[0];
    }

    void Run(string? id)
    {
      if (string.IsNullOrEmpty(id)) return;

      // Check if this is a registry command
      var item = Items.FirstOrDefault(i => i.Id == id);
      if (item?.IsRegistryCommand == true && _commandRegistry != null)
      {
        // Execute through the unified registry
        _ = ExecuteRegistryCommandAsync(id);
        return;
      }

      // Parse command ID (format: "action:value")
      var parts = id.Split(':', 2);
      if (parts.Length != 2)
      {
        Debug.WriteLine($"[Palette] Invalid command format: {id}");
        return;
      }

      var action = parts[0].ToLowerInvariant();
      var value = parts[1];

      // Raise event for command execution
      var args = new CommandExecutedEventArgs
      {
        Action = action,
        Value = value,
        CommandId = id
      };

      CommandExecuted?.Invoke(this, args);

      Debug.WriteLine($"[Palette] Executed: {action} = {value}");
    }

    private async Task ExecuteRegistryCommandAsync(string commandId)
    {
      if (_commandRegistry == null) return;

      try
      {
        await _commandRegistry.ExecuteAsync(commandId, null, CancellationToken.None);
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, $"ExecuteCommand:{commandId}");
      }
    }
  }

  /// <summary>
  /// Event arguments for command execution.
  /// </summary>
  public class CommandExecutedEventArgs : EventArgs
  {
    public string Action { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string CommandId { get; set; } = string.Empty;
  }

  public sealed class CommandItem
  {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Shortcut { get; set; }

    /// <summary>
    /// Indicates if this command is registered with the UnifiedCommandRegistry.
    /// </summary>
    public bool IsRegistryCommand { get; set; }
  }
}