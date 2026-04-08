using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.ViewModels;

/// <summary>
/// Canonical toolbar ViewModel that centralizes toolbar capability logic.
/// Keeps shell command routing and workspace switching out of control code-behind.
/// </summary>
public partial class ToolbarViewModel : ObservableObject
{
    private readonly ToolbarConfigurationService _toolbarConfigurationService;
    private readonly IUnifiedCommandRegistry _commandRegistry;
    private readonly IUnifiedWorkspaceService _workspaceService;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IToastNotificationService? _toastNotificationService;

    public ToolbarViewModel(
        ToolbarConfigurationService toolbarConfigurationService,
        IUnifiedCommandRegistry commandRegistry,
        IUnifiedWorkspaceService workspaceService,
        IAudioPlayerService audioPlayerService,
        IToastNotificationService? toastNotificationService = null)
    {
        _toolbarConfigurationService = toolbarConfigurationService ?? throw new ArgumentNullException(nameof(toolbarConfigurationService));
        _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _audioPlayerService = audioPlayerService ?? throw new ArgumentNullException(nameof(audioPlayerService));
        _toastNotificationService = toastNotificationService;
        _toolbarConfigurationService.ConfigurationChanged += (_, _) => ToolbarConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ToolbarConfigurationChanged;

    public IReadOnlyList<ToolbarItem> GetVisibleItems()
    {
        return _toolbarConfigurationService
            .GetConfiguration()
            .Items
            .Where(item => item.IsVisible)
            .OrderBy(item => item.Order)
            .ToList();
    }

    public async Task ExecuteToolbarActionAsync(string itemId, Action? importAudioAction = null)
    {
        switch (itemId)
        {
            case "import_audio":
                importAudioAction?.Invoke();
                return;
            case "loop":
                _audioPlayerService.IsLooping = !_audioPlayerService.IsLooping;
                _toastNotificationService?.ShowInfo(
                    _audioPlayerService.IsLooping ? "Loop playback enabled" : "Loop playback disabled",
                    "Loop");
                return;
            default:
                break;
        }

        var commandId = itemId switch
        {
            "play" => "playback.play",
            "pause" => "playback.play",
            "stop" => "playback.stop",
            "record" => "playback.record",
            "undo" => "edit.undo",
            "redo" => "edit.redo",
            _ => null
        };

        if (commandId == null || !_commandRegistry.IsRegistered(commandId))
        {
            return;
        }

        await _commandRegistry.ExecuteAsync(commandId);
    }

    public async Task<bool> SwitchWorkspaceAsync(string workspaceId)
    {
        var succeeded = await _workspaceService.SwitchWorkspaceProfileAsync(workspaceId);
        if (succeeded)
        {
            _toastNotificationService?.ShowSuccess($"Switched to: {workspaceId}", "Workspace");
            return true;
        }

        _toastNotificationService?.ShowWarning($"Workspace '{workspaceId}' created (default layout)", "Workspace");
        return false;
    }
}
