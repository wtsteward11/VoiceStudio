using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Tracks application startup state and surfaces it to the shell.
/// </summary>
public sealed class StartupStateService : IStartupStateService
{
    private StartupState _currentState = StartupState.Starting;
    private string? _failureMessage;

    /// <inheritdoc />
    public StartupState CurrentState => _currentState;

    /// <inheritdoc />
    public string? FailureMessage => _failureMessage;

    /// <inheritdoc />
    public bool IsReady => _currentState == StartupState.BackendReady || _currentState == StartupState.Degraded;

    /// <inheritdoc />
    public event EventHandler<StartupStateChangedEventArgs>? StateChanged;

    /// <summary>Sets state to BackendStarting.</summary>
    public void SetBackendStarting()
    {
        SetState(StartupState.BackendStarting, null);
    }

    /// <summary>Sets state to BackendReady.</summary>
    public void SetBackendReady()
    {
        SetState(StartupState.BackendReady, null);
    }

    /// <summary>Sets state to BackendFailed with message.</summary>
    public void SetBackendFailed(string message)
    {
        _failureMessage = message;
        SetState(StartupState.BackendFailed, message);
    }

    /// <summary>Sets state to Degraded.</summary>
    public void SetDegraded()
    {
        SetState(StartupState.Degraded, null);
    }

    private void SetState(StartupState newState, string? failureMessage)
    {
        if (_currentState == newState)
        {
            return;
        }

        var previous = _currentState;
        _currentState = newState;
        if (newState == StartupState.BackendFailed)
        {
            _failureMessage = failureMessage;
        }
        else
        {
            _failureMessage = null;
        }

        StateChanged?.Invoke(this, new StartupStateChangedEventArgs
        {
            PreviousState = previous,
            NewState = newState,
            FailureMessage = failureMessage,
        });
    }
}
