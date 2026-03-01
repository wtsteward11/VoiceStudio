// Phase 4.1: Shared Delegating Command Service
// Resolves GAP-B21: Different ICommand instances for same logical action.
// Provides shared DelegatingCommand instances for common toolbar/panel actions.

using System;
using VoiceStudio.App.Logging;
using System.Collections.Concurrent;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using VoiceStudio.App.Core.Commands;

namespace VoiceStudio.App.Services;

/// <summary>
/// Interface for the shared delegating command service.
/// </summary>
public interface ISharedDelegatingCommandService
{
    /// <summary>
    /// Gets the shared DelegatingCommand for the specified command ID.
    /// Creates one if it doesn't exist.
    /// </summary>
    DelegatingCommand GetSharedCommand(string commandId);

    /// <summary>
    /// Sets the delegate for a shared command. Called when a panel becomes active.
    /// </summary>
    void SetDelegate(string commandId, ICommand? command);

    /// <summary>
    /// Clears all delegates for the specified panel. Called when a panel is deactivated.
    /// </summary>
    void ClearDelegatesForPanel(string panelId);

    /// <summary>
    /// Gets the shared Play command.
    /// </summary>
    DelegatingCommand PlayCommand { get; }

    /// <summary>
    /// Gets the shared Stop command.
    /// </summary>
    DelegatingCommand StopCommand { get; }

    /// <summary>
    /// Gets the shared Save command.
    /// </summary>
    DelegatingCommand SaveCommand { get; }

    /// <summary>
    /// Gets the shared Export command.
    /// </summary>
    DelegatingCommand ExportCommand { get; }

    /// <summary>
    /// Gets the shared Refresh command.
    /// </summary>
    DelegatingCommand RefreshCommand { get; }
}

/// <summary>
/// Service that manages shared DelegatingCommand instances for common toolbar/panel actions.
/// This ensures a single ICommand instance is used across the application for each logical action,
/// with the underlying delegate changing based on the active panel.
/// </summary>
public class SharedDelegatingCommandService : ISharedDelegatingCommandService
{
    private readonly ConcurrentDictionary<string, DelegatingCommand> _sharedCommands = new();
    private readonly ConcurrentDictionary<string, string> _commandToPanelMap = new();
    private readonly DispatcherQueue? _dispatcherQueue;

    // Pre-defined shared commands for the top 5 common actions
    public DelegatingCommand PlayCommand { get; }
    public DelegatingCommand StopCommand { get; }
    public DelegatingCommand SaveCommand { get; }
    public DelegatingCommand ExportCommand { get; }
    public DelegatingCommand RefreshCommand { get; }

    public SharedDelegatingCommandService(DispatcherQueue? dispatcherQueue = null)
    {
        _dispatcherQueue = dispatcherQueue;

        // Initialize the top 5 shared commands
        PlayCommand = CreateSharedCommand("toolbar.play");
        StopCommand = CreateSharedCommand("toolbar.stop");
        SaveCommand = CreateSharedCommand("toolbar.save");
        ExportCommand = CreateSharedCommand("toolbar.export");
        RefreshCommand = CreateSharedCommand("toolbar.refresh");

        // Note: Future enhancement - subscribe to panel activation events when PanelActivatedEvent
        // and PanelDeactivatedEvent are added to VoiceStudio.Core.Events
        // For now, ViewModels wire their commands directly in their constructors

        ErrorLogger.LogDebug("[SharedDelegatingCommandService] Initialized with 5 shared commands", "SharedDelegatingCommandService");
    }

    private DelegatingCommand CreateSharedCommand(string commandId)
    {
        var command = new DelegatingCommand(commandId, _dispatcherQueue);
        _sharedCommands[commandId] = command;
        return command;
    }

    public DelegatingCommand GetSharedCommand(string commandId)
    {
        return _sharedCommands.GetOrAdd(commandId, id => new DelegatingCommand(id, _dispatcherQueue));
    }

    public void SetDelegate(string commandId, ICommand? command)
    {
        if (_sharedCommands.TryGetValue(commandId, out var sharedCommand))
        {
            sharedCommand.SetDelegate(command);
            ErrorLogger.LogDebug($"[SharedDelegatingCommandService] Set delegate for '{commandId}'", "SharedDelegatingCommandService");
        }
        else
        {
            // Create new shared command if it doesn't exist
            var newCommand = GetSharedCommand(commandId);
            newCommand.SetDelegate(command);
            ErrorLogger.LogDebug($"[SharedDelegatingCommandService] Created and set delegate for '{commandId}'", "SharedDelegatingCommandService");
        }
    }

    public void ClearDelegatesForPanel(string panelId)
    {
        foreach (var kvp in _commandToPanelMap)
        {
            if (kvp.Value == panelId)
            {
                if (_sharedCommands.TryGetValue(kvp.Key, out var command))
                {
                    command.ClearDelegate();
                    ErrorLogger.LogDebug($"[SharedDelegatingCommandService] Cleared delegate for '{kvp.Key}' (panel: {panelId})", "SharedDelegatingCommandService");
                }
            }
        }
    }

    /// <summary>
    /// Sets the delegate for a command and tracks which panel owns it.
    /// </summary>
    public void SetDelegateForPanel(string commandId, string panelId, ICommand? command)
    {
        SetDelegate(commandId, command);
        _commandToPanelMap[commandId] = panelId;
    }
}
