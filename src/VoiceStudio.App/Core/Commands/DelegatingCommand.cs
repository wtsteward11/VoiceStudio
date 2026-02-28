// Copyright (c) 2024 VoiceStudio. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Windows.Input;
using Microsoft.UI.Dispatching;

namespace VoiceStudio.App.Core.Commands;

/// <summary>
/// A delegating command that forwards execution and CanExecute checks to an active delegate.
/// Used to share a single ICommand instance between toolbar and panel commands,
/// ensuring consistent CanExecute state across the UI.
/// </summary>
/// <remarks>
/// Resolves GAP-B21: Different ICommand instances for same logical action.
/// The DelegatingCommand acts as a stable proxy that can have its underlying
/// delegate changed when the active panel changes.
/// </remarks>
public sealed class DelegatingCommand : ICommand
{
    private ICommand? _activeDelegate;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly object _lock = new();

    /// <summary>
    /// Occurs when the CanExecute state may have changed.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Gets the command ID for diagnostic purposes.
    /// </summary>
    public string CommandId { get; }

    /// <summary>
    /// Gets or sets whether the command is enabled regardless of delegate state.
    /// When false, CanExecute always returns false.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegatingCommand"/> class.
    /// </summary>
    /// <param name="commandId">The command identifier for diagnostics.</param>
    /// <param name="dispatcherQueue">Optional dispatcher queue for UI thread marshalling.</param>
    public DelegatingCommand(string commandId, DispatcherQueue? dispatcherQueue = null)
    {
        CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        _dispatcherQueue = dispatcherQueue;
    }

    /// <summary>
    /// Sets the active delegate command. Called when the active panel changes.
    /// </summary>
    /// <param name="command">The new delegate command, or null to clear.</param>
    public void SetDelegate(ICommand? command)
    {
        lock (_lock)
        {
            if (_activeDelegate != null)
            {
                _activeDelegate.CanExecuteChanged -= OnDelegateCanExecuteChanged;
            }

            _activeDelegate = command;

            if (_activeDelegate != null)
            {
                _activeDelegate.CanExecuteChanged += OnDelegateCanExecuteChanged;
            }
        }

        RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Gets the currently active delegate command.
    /// </summary>
    public ICommand? GetDelegate()
    {
        lock (_lock)
        {
            return _activeDelegate;
        }
    }

    /// <summary>
    /// Clears the active delegate without notifying.
    /// </summary>
    public void ClearDelegate()
    {
        lock (_lock)
        {
            if (_activeDelegate != null)
            {
                _activeDelegate.CanExecuteChanged -= OnDelegateCanExecuteChanged;
                _activeDelegate = null;
            }
        }
    }

    /// <summary>
    /// Determines whether the command can execute in its current state.
    /// </summary>
    /// <param name="parameter">Data used by the command.</param>
    /// <returns>true if the command can be executed; otherwise, false.</returns>
    public bool CanExecute(object? parameter)
    {
        if (!IsEnabled)
            return false;

        ICommand? activeDelegate;
        lock (_lock)
        {
            activeDelegate = _activeDelegate;
        }

        if (activeDelegate == null)
            return false;

        try
        {
            return activeDelegate.CanExecute(parameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DelegatingCommand] CanExecute error for '{CommandId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="parameter">Data used by the command.</param>
    public void Execute(object? parameter)
    {
        ICommand? activeDelegate;
        lock (_lock)
        {
            activeDelegate = _activeDelegate;
        }

        if (activeDelegate == null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DelegatingCommand] Execute called but no delegate for '{CommandId}'");
            return;
        }

        try
        {
            activeDelegate.Execute(parameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DelegatingCommand] Execute error for '{CommandId}': {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Raises the CanExecuteChanged event.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
        else
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDelegateCanExecuteChanged(object? sender, EventArgs e)
    {
        RaiseCanExecuteChanged();
    }
}
