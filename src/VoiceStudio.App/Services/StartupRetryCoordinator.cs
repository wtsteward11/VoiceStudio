// VoiceStudio - Startup Retry Coordination (Premium Reliability Pass Task 10)
// Encapsulates startup retry logic; MainWindow delegates instead of grabbing services directly.
// Category-aware: HealthTimeout gets bounded retry; PortCollision/RuntimeMissing/InvalidAppRoot/SpawnFailure do not.

using System;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Progress reported during startup retry for UX (attempt count, retry messages).
/// </summary>
/// <param name="Attempt">Current attempt (1-based).</param>
/// <param name="MaxAttempts">Maximum retry attempts.</param>
/// <param name="Message">User-facing message (e.g. "Retrying (attempt 1 of 2)…").</param>
public sealed record StartupRetryProgress(int Attempt, int MaxAttempts, string Message);

/// <summary>
/// Owns the startup retry workflow. MainWindow delegates to this instead of
/// directly reaching BackendProcessManager and StartupStateService.
/// </summary>
public sealed class StartupRetryCoordinator
{
    private readonly IStartupStateService? _startupState;
    private readonly Func<Task<bool>>? _ensureBackendRunning;
    private readonly Func<BackendStartFailedEventArgs?>? _getLastFailure;
    private readonly TimeSpan? _retryDelayOverride;

    /// <summary>
    /// Max retries for HealthTimeout (bounded retry).
    /// </summary>
    private const int HealthTimeoutMaxRetries = 2;

    /// <summary>
    /// Delay between HealthTimeout retries (seconds). Used when _retryDelayOverride is null.
    /// </summary>
    private const int HealthTimeoutRetryDelaySeconds = 5;

    /// <summary>
    /// Production constructor: uses ServiceProvider for dependencies.
    /// </summary>
    public StartupRetryCoordinator()
        : this(null, null, null, null)
    {
    }

    /// <summary>
    /// Test constructor: inject dependencies for unit testing.
    /// </summary>
    /// <param name="startupState">When null, uses ServiceProvider.GetStartupStateService().</param>
    /// <param name="ensureBackendRunning">When null, uses BackendProcessManager.EnsureBackendRunningAsync.</param>
    /// <param name="getLastFailure">When null, uses BackendProcessManager.LastFailure.</param>
    /// <param name="retryDelayOverride">When set (e.g. in tests), used instead of 5s delay. Enables fast tests without real Task.Delay.</param>
    public StartupRetryCoordinator(
        IStartupStateService? startupState,
        Func<Task<bool>>? ensureBackendRunning,
        Func<BackendStartFailedEventArgs?>? getLastFailure,
        TimeSpan? retryDelayOverride = null)
    {
        _startupState = startupState;
        _ensureBackendRunning = ensureBackendRunning;
        _getLastFailure = getLastFailure;
        _retryDelayOverride = retryDelayOverride;
    }

    /// <summary>
    /// Runs the startup retry workflow: set BackendStarting, ensure backend running,
    /// set BackendFailed if still starting after failure. Category-aware: HealthTimeout
    /// retries up to HealthTimeoutMaxRetries with delay; other categories do not retry.
    /// </summary>
    /// <param name="progress">Optional. Reports attempt count and retry messages for UX.</param>
    public async Task RetryAsync(IProgress<StartupRetryProgress>? progress = null)
    {
        var startupState = _startupState ?? ServiceProvider.GetStartupStateService();
        if (startupState == null)
            return;

        var backendManager = ServiceProvider.TryGetBackendProcessManager();
        Func<Task<bool>> ensureBackend = _ensureBackendRunning ?? (async () =>
            backendManager != null && await backendManager.EnsureBackendRunningAsync());
        Func<BackendStartFailedEventArgs?> getLastFailure = _getLastFailure ?? (() => backendManager?.LastFailure);

        startupState.SetBackendStarting();
        var started = await ensureBackend();

        if (!started && startupState.CurrentState == StartupState.BackendStarting)
        {
            var lastFailure = getLastFailure();
            var category = lastFailure?.FailureCategory ?? BackendStartFailureCategory.SpawnFailure;
            var message = lastFailure?.Message ?? "Backend failed to start. Check logs in %LOCALAPPDATA%\\VoiceStudio\\crashes\\";

            if (category == BackendStartFailureCategory.HealthTimeout)
            {
                var delay = _retryDelayOverride ?? TimeSpan.FromSeconds(HealthTimeoutRetryDelaySeconds);
                for (var attempt = 0; attempt < HealthTimeoutMaxRetries && startupState.CurrentState == StartupState.BackendStarting; attempt++)
                {
                    progress?.Report(new StartupRetryProgress(
                        attempt + 1,
                        HealthTimeoutMaxRetries,
                        $"Retrying (attempt {attempt + 1} of {HealthTimeoutMaxRetries})…"));

                    await Task.Delay(delay);
                    started = await ensureBackend();
                    if (started)
                        return;
                }
            }
            else
            {
                message = AppendNoRetryExplanation(message, category);
            }

            startupState.SetBackendFailed(message);
        }
    }

    private static string AppendNoRetryExplanation(string message, BackendStartFailureCategory category)
    {
        var suffix = category switch
        {
            BackendStartFailureCategory.PortCollision => " Retry will not help. Close the conflicting app or change the port.",
            BackendStartFailureCategory.RuntimeMissing => " Retry will not help. Install the Python runtime.",
            BackendStartFailureCategory.InvalidAppRoot => " Retry will not help. Reinstall the application.",
            BackendStartFailureCategory.SpawnFailure => " Retry may help. Check logs in %LOCALAPPDATA%\\VoiceStudio\\crashes\\.",
            _ => " Retry may help."
        };
        return message.TrimEnd() + suffix;
    }
}
