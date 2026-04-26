// VoiceStudio — GAP-008 Slice 30: global transport, recording toggle, timeline zoom shell.

using System;
using System.Threading.Tasks;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Views;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Shell for <see cref="App.MainWindow"/> global transport (play/stop), transport recording shortcut,
/// recording panel toggle, and center timeline zoom — not search, not edit/undo, not help.
/// </summary>
public sealed class MainWindowGlobalTransportShellBridge
{
    public async Task TogglePlaybackAsync(
        Func<IStartupStateService> getStartup,
        Func<IToastNotificationService?> getToast,
        Func<IGlobalTransportOrchestrator?> getOrchestrator)
    {
        ArgumentNullException.ThrowIfNull(getStartup);
        ArgumentNullException.ThrowIfNull(getToast);
        ArgumentNullException.ThrowIfNull(getOrchestrator);

        if (StartupGatingHelper.ShouldBlockTransportPlayback(getStartup()))
        {
            getToast()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }

        var orchestrator = getOrchestrator();
        if (orchestrator is not null)
        {
            await orchestrator.TogglePlaybackAsync().ConfigureAwait(true);
        }
        else
        {
            getToast()?.ShowToast(ToastType.Info, "No media selected", "Select an audio asset in Library or Timeline, then press Play.");
        }
    }

    public void StopPlayback(
        Func<IStartupStateService> getStartup,
        Func<IToastNotificationService?> getToast,
        Func<IGlobalTransportOrchestrator?> getOrchestrator)
    {
        ArgumentNullException.ThrowIfNull(getStartup);
        ArgumentNullException.ThrowIfNull(getToast);
        ArgumentNullException.ThrowIfNull(getOrchestrator);

        if (StartupGatingHelper.ShouldBlockTransportPlayback(getStartup()))
        {
            getToast()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }

        getOrchestrator()?.StopPlayback();
    }

    public void OpenRecordingPanelFromTransportShortcut(
        Func<IStartupStateService> getStartup,
        Func<IToastNotificationService?> getToast,
        Func<IEventAggregator?> getEventAggregator)
    {
        ArgumentNullException.ThrowIfNull(getStartup);
        ArgumentNullException.ThrowIfNull(getToast);
        ArgumentNullException.ThrowIfNull(getEventAggregator);

        if (!getStartup().IsReady)
        {
            getToast()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }

        var aggregator = getEventAggregator();
        if (aggregator is null)
        {
            return;
        }

        aggregator.Publish(new NavigateToEvent(PanelIds.Timeline, PanelIds.Recording, null));
    }

    public async Task ToggleRecordingAsync(
        Func<PanelHost?> getRightPanelHost,
        Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync,
        Func<IStartupStateService> getStartup,
        Func<IToastNotificationService?> getToast,
        Action<Exception, string> logError)
    {
        ArgumentNullException.ThrowIfNull(getRightPanelHost);
        ArgumentNullException.ThrowIfNull(openPanelByIdAsync);
        ArgumentNullException.ThrowIfNull(getStartup);
        ArgumentNullException.ThrowIfNull(getToast);
        ArgumentNullException.ThrowIfNull(logError);

        if (!getStartup().IsReady)
        {
            getToast()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }

        try
        {
            if (getRightPanelHost() is not { } rightPanelHost)
            {
                return;
            }

            var recordingView = rightPanelHost.HostedPanel as RecordingView;
            if (recordingView is null)
            {
                await openPanelByIdAsync("Recording", PanelRegion.Right).ConfigureAwait(true);
                var hostAfterOpen = getRightPanelHost();
                recordingView = hostAfterOpen?.HostedPanel as RecordingView;
            }

            if (recordingView is null)
            {
                return;
            }

            var viewModel = recordingView.ViewModel;
            if (viewModel.IsRecording)
            {
                if (viewModel.StopRecordingCommand.CanExecute(null))
                {
                    viewModel.StopRecordingCommand.Execute(null);
                }
            }
            else
            {
                if (viewModel.StartRecordingCommand.CanExecute(null))
                {
                    viewModel.StartRecordingCommand.Execute(null);
                }
            }
        }
        catch (Exception ex)
        {
            logError(ex, "ToggleRecording");
            getToast()?.ShowError(
                "Recording toggle failed.",
                "Recording");
        }
    }

    public void ZoomIn(Func<PanelHost?> getCenterPanelHost)
    {
        ArgumentNullException.ThrowIfNull(getCenterPanelHost);
        WithTimelineViewModel(getCenterPanelHost(), static vm =>
        {
            if (vm.ZoomInCommand.CanExecute(null))
            {
                vm.ZoomInCommand.Execute(null);
            }
        });
    }

    public void ZoomOut(Func<PanelHost?> getCenterPanelHost)
    {
        ArgumentNullException.ThrowIfNull(getCenterPanelHost);
        WithTimelineViewModel(getCenterPanelHost(), static vm =>
        {
            if (vm.ZoomOutCommand.CanExecute(null))
            {
                vm.ZoomOutCommand.Execute(null);
            }
        });
    }

    public void ResetZoom(Func<PanelHost?> getCenterPanelHost)
    {
        ArgumentNullException.ThrowIfNull(getCenterPanelHost);
        WithTimelineViewModel(getCenterPanelHost(), static vm => vm.TimelineZoom = 1.0);
    }

    private static void WithTimelineViewModel(PanelHost? center, Action<TimelineViewModel> onVm)
    {
        if (center?.HostedPanel is not TimelineView timelineView || timelineView.ViewModel is null)
        {
            return;
        }

        onVm(timelineView.ViewModel);
    }
}
