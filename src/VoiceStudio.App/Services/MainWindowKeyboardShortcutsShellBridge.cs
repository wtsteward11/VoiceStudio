// VoiceStudio — GAP-008 Slice 21: MainWindow keyboard shortcuts / customize dialog shell only.

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Shell for Help → Keyboard Shortcuts menu: cheat sheet dialog and optional customize dialog (Slice 20 menu/tool bridge OUT).
/// </summary>
public sealed class MainWindowKeyboardShortcutsShellBridge
{
    public async Task RunKeyboardShortcutsMenuFlowAsync(
        Func<XamlRoot?> getXamlRoot,
        Func<KeyboardCustomizationViewModel> getKeyboardCustomizationViewModel,
        Func<IToastNotificationService?> getToastForError)
    {
        ArgumentNullException.ThrowIfNull(getXamlRoot);
        ArgumentNullException.ThrowIfNull(getKeyboardCustomizationViewModel);
        ArgumentNullException.ThrowIfNull(getToastForError);

        try
        {
            var root = getXamlRoot();
            if (root == null)
            {
                throw new InvalidOperationException("Cannot show keyboard shortcuts: XamlRoot is not available.");
            }

            var shortcutsView = new VoiceStudio.App.Views.KeyboardShortcutsView();
            var dialog = new ContentDialog
            {
                Title = "Keyboard Shortcuts",
                Content = shortcutsView,
                PrimaryButtonText = "Customize…",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root,
                Width = 800,
                Height = 600
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                KeyboardCustomizationViewModel? customizeVm = null;
                try
                {
                    customizeVm = getKeyboardCustomizationViewModel();
                    customizeVm.RefreshShortcuts();
                    var customizeView = new KeyboardCustomizationView
                    {
                        DataContext = customizeVm
                    };
                    var customizeDialog = new ContentDialog
                    {
                        Title = "Customize keyboard shortcuts",
                        Content = customizeView,
                        CloseButtonText = "Close",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = root,
                        MaxWidth = 640
                    };
                    await customizeDialog.ShowAsync();
                }
                finally
                {
                    customizeVm?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            var toastService = getToastForError();
            toastService?.ShowToast(
                ToastType.Error,
                "Failed to Open Documentation",
                $"Unable to open keyboard shortcuts documentation: {ex.Message}");
        }
    }
}
