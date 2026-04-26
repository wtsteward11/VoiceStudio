// VoiceStudio — GAP-008 Slice 20: MainWindow menu / tool activation shell (bounded cluster only).

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views;
using VoiceStudio.App.Views.Dialogs;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Shell activations for a bounded set of menu/tool handlers; keyboard shortcuts dialog remains on <see cref="MainWindow"/> (DEFERRED).
/// </summary>
public sealed class MainWindowMenuToolActivationShellBridge
{
    private static void SetPanelHostMeta(PanelHost? host, string title, string icon)
    {
        if (host == null)
        {
            return;
        }

        host.PanelTitle = title;
        host.PanelIcon = icon;
    }

    public async Task RunCheckForUpdatesAsync(
        Func<IViewModelContext> getContext,
        IUpdateService updateService,
        Func<IErrorDialogService> getErrorDialogService)
    {
        ArgumentNullException.ThrowIfNull(getContext);
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(getErrorDialogService);

        try
        {
            var context = getContext();
            var updateViewModel = new UpdateViewModel(context, updateService);
            var updateDialog = new UpdateDialog(updateViewModel);
            await updateDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var errorService = getErrorDialogService();
            await errorService.ShowErrorAsync(
                "Update Check Failed",
                $"Unable to check for updates: {ex.Message}",
                "OK");
        }
    }

    public async Task RunToggleMiniTimelineAsync(
        Func<bool> getIsMiniTimelineVisible,
        Action<bool> setIsMiniTimelineVisible,
        Func<PanelHost?> getBottomPanelHost,
        Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync,
        Action refreshMenuItemText,
        Func<IToastNotificationService?> tryGetToast)
    {
        ArgumentNullException.ThrowIfNull(getIsMiniTimelineVisible);
        ArgumentNullException.ThrowIfNull(setIsMiniTimelineVisible);
        ArgumentNullException.ThrowIfNull(getBottomPanelHost);
        ArgumentNullException.ThrowIfNull(openPanelByIdAsync);
        ArgumentNullException.ThrowIfNull(refreshMenuItemText);
        ArgumentNullException.ThrowIfNull(tryGetToast);

        var nextVisible = !getIsMiniTimelineVisible();
        setIsMiniTimelineVisible(nextVisible);

        var bottomPanelHost = getBottomPanelHost();
        if (bottomPanelHost != null)
        {
            if (nextVisible)
            {
                await openPanelByIdAsync("MiniTimeline", PanelRegion.Bottom).ConfigureAwait(true);
                SetPanelHostMeta(bottomPanelHost, "Mini Timeline", "\uD83C\uDFAC");
            }
            else
            {
                await openPanelByIdAsync("Macro", PanelRegion.Bottom).ConfigureAwait(true);
                SetPanelHostMeta(bottomPanelHost, "Macros", "\u26A1");
            }
        }

        refreshMenuItemText();

        var toastService = tryGetToast();
        toastService?.ShowSuccess(
            "Panel Switched",
            nextVisible ? "Mini Timeline is now visible" : "Macro View is now visible");
    }

    public void ToggleCollaborationPanelVisibility(Func<FrameworkElement?> findCollaborationPanel)
    {
        ArgumentNullException.ThrowIfNull(findCollaborationPanel);

        var collaborationPanel = findCollaborationPanel();
        if (collaborationPanel == null)
        {
            return;
        }

        collaborationPanel.Visibility = collaborationPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public void HideCollaborationPanel(Func<FrameworkElement?> findCollaborationPanel)
    {
        ArgumentNullException.ThrowIfNull(findCollaborationPanel);

        var collaborationPanel = findCollaborationPanel();
        if (collaborationPanel == null)
        {
            return;
        }

        collaborationPanel.Visibility = Visibility.Collapsed;
    }

    public async Task RunManageWorkspacesAsync(
        Func<XamlRoot?> getXamlRoot,
        Func<IToastNotificationService?> tryGetToast)
    {
        ArgumentNullException.ThrowIfNull(getXamlRoot);
        ArgumentNullException.ThrowIfNull(tryGetToast);

        try
        {
            var xamlRoot = getXamlRoot();
            var dialog = new WorkspaceManagerDialog(xamlRoot);
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var toastService = tryGetToast();
            toastService?.ShowError(
                "Workspace Management",
                $"Could not open workspace manager: {ex.Message}");
        }
    }
}
