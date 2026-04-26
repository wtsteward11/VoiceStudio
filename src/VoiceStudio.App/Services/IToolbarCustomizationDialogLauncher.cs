using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace VoiceStudio.App.Services;

/// <summary>
/// Abstraction for showing the toolbar customization dialog from shell code (test seam).
/// </summary>
public interface IToolbarCustomizationDialogLauncher
{
    /// <summary>
    /// Shows the toolbar customization UI with the given shell XAML root.
    /// </summary>
    Task ShowAsync(XamlRoot? shellXamlRoot);
}
