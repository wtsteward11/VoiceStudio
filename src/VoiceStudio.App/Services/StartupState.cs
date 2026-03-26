namespace VoiceStudio.App.Services;

/// <summary>
/// Explicit startup states for the application.
/// Drives splash/loading overlay, disabled interactions, and user messaging.
/// </summary>
public enum StartupState
{
    /// <summary>Initial app startup; services initializing.</summary>
    Starting,

    /// <summary>Backend process is starting or waiting for health.</summary>
    BackendStarting,

    /// <summary>Backend is reachable; app is ready for use.</summary>
    BackendReady,

    /// <summary>Backend failed to start; user sees recovery path.</summary>
    BackendFailed,

    /// <summary>Backend was ready but is now degraded (e.g. rate-limited, disconnected).</summary>
    Degraded,
}
