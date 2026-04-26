using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Views.Dialogs;

namespace VoiceStudio.App.Services;

/// <summary>
/// Production launcher for <see cref="ToolbarCustomizationDialog"/>.
/// </summary>
public sealed class ToolbarCustomizationDialogLauncher : IToolbarCustomizationDialogLauncher
{
    /// <inheritdoc />
    public Task ShowAsync(XamlRoot? shellXamlRoot)
    {
        var dialog = new ToolbarCustomizationDialog();
        dialog.XamlRoot = shellXamlRoot;
        return dialog.ShowAsync().AsTask();
    }
}
