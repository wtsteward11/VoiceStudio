using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Views.Dialogs;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Default launcher: shows <see cref="ToolCatalogDialog"/> and maps dialog state to <see cref="ToolCatalogShellChoice"/>.
/// </summary>
public sealed class ToolCatalogShellLauncher : IToolCatalogShellLauncher
{
    public async Task<ToolCatalogShellChoice?> ShowAsync(XamlRoot xamlRoot)
    {
        var dialog = new ToolCatalogDialog(xamlRoot);
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || dialog.SelectedDescriptor == null)
        {
            return null;
        }

        var desc = dialog.SelectedDescriptor;
        var region = dialog.SelectedRegion ?? desc.DefaultRegion;
        return new ToolCatalogShellChoice
        {
            PanelId = desc.PanelId,
            EffectiveRegion = region,
            DisplayName = desc.DisplayName,
            Icon = desc.Icon
        };
    }
}
