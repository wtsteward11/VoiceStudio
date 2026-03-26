using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Tracks and surfaces application startup state.
/// Used by the shell to show startup overlay, disable interactions during warmup, and recovery UX.
/// </summary>
public interface IStartupStateService
{
    /// <summary>Current startup state.</summary>
    StartupState CurrentState { get; }

    /// <summary>Failure message when state is BackendFailed.</summary>
    string? FailureMessage { get; }

    /// <summary>Raised when startup state changes.</summary>
    event EventHandler<StartupStateChangedEventArgs>? StateChanged;

    /// <summary>Whether the app is ready for backend-dependent actions.</summary>
    bool IsReady { get; }

    /// <summary>Sets state to BackendStarting.</summary>
    void SetBackendStarting();

    /// <summary>Sets state to BackendReady.</summary>
    void SetBackendReady();

    /// <summary>Sets state to BackendFailed with message.</summary>
    void SetBackendFailed(string message);

    /// <summary>Sets state to Degraded.</summary>
    void SetDegraded();
}

/// <summary>Event args for startup state changes.</summary>
public sealed class StartupStateChangedEventArgs : EventArgs
{
    public StartupState PreviousState { get; init; }
    public StartupState NewState { get; init; }
    public string? FailureMessage { get; init; }
}
