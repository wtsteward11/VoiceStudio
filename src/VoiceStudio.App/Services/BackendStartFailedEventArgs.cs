using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Failure categories for backend startup, used by StartupRetryCoordinator for category-aware retry behavior.
/// </summary>
public enum BackendStartFailureCategory
{
    PortCollision,
    RuntimeMissing,
    InvalidAppRoot,
    HealthTimeout,
    SpawnFailure
}

/// <summary>
/// Event args for BackendStartFailed, carrying failure category and message.
/// </summary>
public sealed class BackendStartFailedEventArgs : EventArgs
{
    public BackendStartFailureCategory FailureCategory { get; }
    public string Message { get; }

    public BackendStartFailedEventArgs(BackendStartFailureCategory category, string message)
    {
        FailureCategory = category;
        Message = message ?? string.Empty;
    }
}
