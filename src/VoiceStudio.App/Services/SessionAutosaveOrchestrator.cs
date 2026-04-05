using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services;

/// <summary>
/// Debounced + failsafe autosave while <see cref="IProjectSessionDirtyState.IsProjectDirty"/> and settings allow.
/// Uses canonical <see cref="IProjectWorkflowCoordinator.TryAutosaveProjectAsync"/> (same handler as manual save).
/// </summary>
public sealed class SessionAutosaveOrchestrator : IDisposable
{
    private const int DebounceMs = 3000;
    private const int FailsafeMinimumSeconds = 30;

    private readonly IStartupStateService _startup;
    private readonly ISettingsService _settings;
    private readonly IProjectSessionDirtyState _dirty;
    private readonly IProjectWorkflowCoordinator _workflow;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _debounceTimer;
    private readonly DispatcherQueueTimer _failsafeTimer;
    private readonly SemaphoreSlim _autosaveGate = new(1, 1);
    private bool _disposed;
    private bool _dirtySubscribed;

    public SessionAutosaveOrchestrator(
        IStartupStateService startup,
        ISettingsService settings,
        IProjectSessionDirtyState dirty,
        IProjectWorkflowCoordinator workflow,
        DispatcherQueue dispatcher)
    {
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dirty = dirty ?? throw new ArgumentNullException(nameof(dirty));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _debounceTimer = _dispatcher.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceMs);
        _debounceTimer.Tick += OnDebounceTick;

        _failsafeTimer = _dispatcher.CreateTimer();
        _failsafeTimer.Interval = TimeSpan.FromSeconds(FailsafeMinimumSeconds);
        _failsafeTimer.Tick += OnFailsafeTick;
    }

    /// <summary>Subscribe to dirty changes and start failsafe timer.</summary>
    public void Start()
    {
        if (_dirtySubscribed)
            return;
        _dirtySubscribed = true;
        _dirty.DirtyStateChanged += OnDirtyStateChanged;
        _failsafeTimer.Start();
        if (_dirty.IsProjectDirty)
            RestartDebounce();
    }

    private void OnDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_dirty.IsProjectDirty)
            RestartDebounce();
        else
            _debounceTimer.Stop();
    }

    private void RestartDebounce()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void OnDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        await TryRunAutosaveAsync().ConfigureAwait(true);
    }

    private async void OnFailsafeTick(DispatcherQueueTimer sender, object args)
    {
        if (!_dirty.IsProjectDirty)
            return;
        await TryRunAutosaveAsync().ConfigureAwait(true);
    }

    private async Task TryRunAutosaveAsync()
    {
        if (_disposed || !_dirty.IsProjectDirty || !_startup.IsReady)
            return;

        SettingsData settings;
        try
        {
            settings = await _settings.LoadSettingsAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Session autosave skipped — settings load failed: {ex.Message}", "SessionAutosave");
            return;
        }

        var general = settings.General;
        if (general == null || !general.AutoSave)
            return;

        var intervalSec = general.AutoSaveInterval > 0 ? general.AutoSaveInterval : 300;
        var failsafeSeconds = Math.Max(FailsafeMinimumSeconds, intervalSec);
        if (Math.Abs(_failsafeTimer.Interval.TotalSeconds - failsafeSeconds) > 0.5)
            _failsafeTimer.Interval = TimeSpan.FromSeconds(failsafeSeconds);

        if (!await _autosaveGate.WaitAsync(0).ConfigureAwait(true))
            return;
        try
        {
            if (!_dirty.IsProjectDirty)
                return;
            await _workflow.TryAutosaveProjectAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            _autosaveGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dirtySubscribed)
            _dirty.DirtyStateChanged -= OnDirtyStateChanged;
        _debounceTimer.Stop();
        _failsafeTimer.Stop();
        _autosaveGate.Dispose();
    }
}
