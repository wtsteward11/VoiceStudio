namespace VoiceStudio.Core.Services;

/// <summary>
/// GAP-034: OS-level (Windows App Notifications) completion signals for long-running operations.
/// </summary>
public enum CompletionOsNotificationCategory
{
    Batch = 0,
    Training = 1,
    Export = 2,
}

/// <summary>
/// Publishes at most one Windows toast per unique terminal (category, operationId, success) tuple per process.
/// Implementations must not throw through to producers.
/// </summary>
public interface ICompletionOsNotificationService
{
    /// <param name="category">Batch, training, or export.</param>
    /// <param name="operationId">Stable job id (batch/training) or per-export correlation id.</param>
    /// <param name="success">Terminal outcome.</param>
    /// <param name="title">Short operator-facing title.</param>
    /// <param name="body">Short body; avoid secrets and long stack traces.</param>
    void TryNotifyTerminalCompletion(
        CompletionOsNotificationCategory category,
        string operationId,
        bool success,
        string title,
        string body);
}
