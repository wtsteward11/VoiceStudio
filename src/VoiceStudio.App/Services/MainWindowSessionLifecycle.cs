using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App;

namespace VoiceStudio.App.Services;

/// <summary>
/// Crash-recovery prompt + session autosave wiring for <see cref="App.MainWindow"/>.
/// Lives outside MainWindow*.cs partials to satisfy CI MainWindow total-size budget.
/// </summary>
public sealed class MainWindowSessionLifecycle : IDisposable
{
    private SessionAutosaveOrchestrator? _autosaveOrchestrator;
    private bool _recoveryHandlersAttached;
    private int _recoveryDialogGate;
    private int _recoveryDeferralSubscribed;

    public void AttachRecoveryHandlers(MainWindow window, FrameworkElement xamlRootSource)
    {
        if (_recoveryHandlersAttached)
            return;
        _recoveryHandlersAttached = true;

        var crash = ServiceProvider.GetCrashRecoveryService();
        var self = this;
        void MaybePrompt(object? sender, EventArgs e)
        {
            _ = window.DispatcherQueue.TryEnqueue(async () =>
                await self.TryPromptRecoveryAsync(window, crash, xamlRootSource).ConfigureAwait(true));
        }

        crash.PendingRecoveryDetermined += MaybePrompt;
        if (crash.HasPendingUserRecoveryPrompt)
            MaybePrompt(null, EventArgs.Empty);
    }

    private async Task TryPromptRecoveryAsync(
        MainWindow window,
        CrashRecoveryService crash,
        FrameworkElement xamlRootSource)
    {
        if (Interlocked.CompareExchange(ref _recoveryDialogGate, 1, 0) != 0)
            return;
        try
        {
            if (!crash.HasPendingUserRecoveryPrompt)
                return;

            if (xamlRootSource.XamlRoot == null)
                return;

            var startup = ServiceProvider.GetStartupStateService();
            if (!startup.IsReady)
            {
                if (Interlocked.CompareExchange(ref _recoveryDeferralSubscribed, 1, 0) == 0)
                {
                    void OnStartupReady(object? s, StartupStateChangedEventArgs e)
                    {
                        if (!startup.IsReady)
                            return;
                        startup.StateChanged -= OnStartupReady;
                        _ = Interlocked.Exchange(ref _recoveryDeferralSubscribed, 0);
                        _ = window.DispatcherQueue.TryEnqueue(async () =>
                            await TryPromptRecoveryAsync(window, crash, xamlRootSource).ConfigureAwait(true));
                    }

                    startup.StateChanged += OnStartupReady;
                    if (startup.IsReady)
                    {
                        OnStartupReady(startup, new StartupStateChangedEventArgs());
                    }
                }

                return;
            }

            var state = crash.PeekPendingRecovery();
            if (state == null)
                return;

            var recoveryState = AppServices.GetService<IMultitrackRecoveryStateService>();
            MultitrackRecoveryPayload? multitrack = null;
            var hasMultitrackPending = recoveryState != null
                && recoveryState.TryReadPayload(state, out multitrack)
                && multitrack != null
                && recoveryState.HasPendingPayload(state);

            var projectLabel = string.IsNullOrWhiteSpace(state.ActiveProjectName)
                ? "(unsaved / unknown name)"
                : state.ActiveProjectName;

            var body = new StringBuilder();
            body.AppendLine("VoiceStudio did not shut down cleanly. A recovery snapshot is available.");
            body.AppendLine();
            body.AppendLine($"Project: {projectLabel}");
            if (hasMultitrackPending && multitrack != null)
            {
                body.AppendLine();
                body.AppendLine("Multitrack recording was interrupted:");
                body.AppendLine($"• Successful takes on disk: {multitrack.SuccessCount}");
                body.AppendLine($"• Failed / missing legs: {multitrack.FailedCount}");
                body.AppendLine();
                body.AppendLine(
                    "Restore reopens this project and imports completed takes (even if microphones are unplugged).");
                body.AppendLine(
                    "Discard deletes preserved temp audio files listed in the snapshot and clears the recovery prompt.");
                body.AppendLine();
                body.AppendLine(
                    "New capture always requires devices selected in the Recording panel — restore does not resume capture automatically.");
            }
            else
            {
                body.AppendLine();
                body.AppendLine(
                    "Restore will reopen that project from your library (same as Open Recent). " +
                    "Discard deletes only the recovery snapshot — it does not remove saved project data.");
            }

            var dialog = new ContentDialog
            {
                Title = "Restore previous session?",
                Content = body.ToString(),
                PrimaryButtonText = "Restore",
                SecondaryButtonText = "Discard",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = xamlRootSource.XamlRoot,
            };

            var result = await dialog.ShowAsync();
            var coordinator = window.GetProjectWorkflowCoordinatorForSessionLifecycle();
            var apply = AppServices.GetService<IMultitrackRecoveryApplyService>();

            if (result == ContentDialogResult.Primary)
            {
                if (string.IsNullOrWhiteSpace(state.ActiveProjectId) || coordinator == null)
                {
                    ServiceProvider.TryGetToastNotificationService()?.ShowError(
                        "Recovery snapshot has no project to reopen.",
                        "Recovery");
                    return;
                }

                if (hasMultitrackPending && multitrack != null)
                {
                    if (apply == null)
                    {
                        ServiceProvider.TryGetToastNotificationService()?.ShowError(
                            "Multitrack restore service is unavailable.",
                            "Recovery");
                        return;
                    }

                    try
                    {
                        await coordinator.OpenRecentProjectAsync(
                            state.ActiveProjectId,
                            state.ActiveProjectName ?? "Recovered",
                            CancellationToken.None).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SessionRecovery] Open project failed: {ex.Message}");
                        ServiceProvider.TryGetToastNotificationService()?.ShowError(
                            $"Could not restore project: {ex.Message}",
                            "Recovery");
                        return;
                    }

                    var restoreResult = await apply.TryRestoreCompletedTakesAsync(
                        state.ActiveProjectId,
                        multitrack,
                        CancellationToken.None).ConfigureAwait(true);
                    if (!restoreResult.Success)
                    {
                        ServiceProvider.TryGetToastNotificationService()?.ShowError(
                            restoreResult.ErrorMessage ?? "Multitrack restore failed.",
                            "Recovery");
                        return;
                    }

                    ServiceProvider.TryGetToastNotificationService()?.ShowSuccess(
                        $"Imported {restoreResult.RestoredLegCount} multitrack take(s).",
                        "Recovery");
                    var guidance = MultitrackRecoveryOperatorCopy.ContinuationGuidanceAfterRestore(multitrack);
                    if (!string.IsNullOrWhiteSpace(guidance))
                      ServiceProvider.TryGetToastNotificationService()?.ShowInfo(guidance, "Recovery");
                }
                else
                {
                    try
                    {
                        await coordinator.OpenRecentProjectAsync(
                            state.ActiveProjectId,
                            state.ActiveProjectName ?? "Recovered",
                            CancellationToken.None).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SessionRecovery] Open project failed: {ex.Message}");
                        ServiceProvider.TryGetToastNotificationService()?.ShowError(
                            $"Could not restore project: {ex.Message}",
                            "Recovery");
                        return;
                    }
                }

                crash.NotifyRecoveryAccepted(state);
            }
            else
            {
                if (multitrack != null && recoveryState != null)
                    recoveryState.DeletePreservedLegFiles(multitrack);
                crash.DiscardPendingRecovery();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryDialogGate, 0);
        }
    }

    public void StartAutosave(MainWindow window)
    {
        if (_autosaveOrchestrator != null)
            return;

        var coordinator = window.GetProjectWorkflowCoordinatorForSessionLifecycle();
        if (coordinator == null)
            return;

        var dq = window.DispatcherQueue;
        if (dq == null)
            return;

        _autosaveOrchestrator = new SessionAutosaveOrchestrator(
            ServiceProvider.GetStartupStateService(),
            ServiceProvider.GetSettingsService(),
            ServiceProvider.GetProjectSessionDirtyState(),
            coordinator,
            dq);
        _autosaveOrchestrator.Start();
    }

    public void TryMarkCleanShutdown()
    {
        try
        {
            ServiceProvider.GetCrashRecoveryService().MarkCleanShutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] MarkCleanShutdown: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _autosaveOrchestrator?.Dispose();
        _autosaveOrchestrator = null;
    }
}
