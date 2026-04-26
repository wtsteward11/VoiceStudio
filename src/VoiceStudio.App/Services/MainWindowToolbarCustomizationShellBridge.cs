using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 7: MainWindow shell wiring for toolbar customization dialog only — not command palette;
/// not tool catalog; not search overlay; not workflow.
/// </summary>
public sealed class MainWindowToolbarCustomizationShellBridge
{
    private readonly Func<XamlRoot?> _getShellXamlRoot;
    private readonly IToolbarCustomizationDialogLauncher _dialogLauncher;
    private readonly Func<IToastNotificationService?> _getToast;

    public MainWindowToolbarCustomizationShellBridge(
        Func<XamlRoot?> getShellXamlRoot,
        IToolbarCustomizationDialogLauncher dialogLauncher,
        Func<IToastNotificationService?> getToast)
    {
        _getShellXamlRoot = getShellXamlRoot ?? throw new ArgumentNullException(nameof(getShellXamlRoot));
        _dialogLauncher = dialogLauncher ?? throw new ArgumentNullException(nameof(dialogLauncher));
        _getToast = getToast ?? throw new ArgumentNullException(nameof(getToast));
    }

    /// <summary>
    /// Opens toolbar customization from the shell (menu handler forwards here).
    /// </summary>
    public async Task ShowCustomizationDialogAsync()
    {
        try
        {
            await _dialogLauncher.ShowAsync(_getShellXamlRoot()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            var toastService = _getToast();
            toastService?.ShowError(
                "Customization Failed",
                $"Could not open toolbar customization: {ex.Message}");
        }
    }
}
