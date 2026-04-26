using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Test/production seam for showing the tool catalog dialog from shell glue (<see cref="MainWindowToolCatalogShellBridge"/>).
/// </summary>
public interface IToolCatalogShellLauncher
{
    /// <summary>
    /// Shows the catalog dialog. Returns <c>null</c> if the user cancels or does not confirm a selection.
    /// </summary>
    Task<ToolCatalogShellChoice?> ShowAsync(XamlRoot xamlRoot);
}

/// <summary>
/// Outcome of a confirmed tool catalog pick (launcher → bridge).
/// </summary>
public sealed class ToolCatalogShellChoice
{
    public required string PanelId { get; init; }

    public PanelRegion EffectiveRegion { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Icon { get; init; }
}
