// VoiceStudio - Shell Navigation Coordination (Premium Reliability Pass Task 9)
// Extracted from MainWindow.xaml.cs per MAINWINDOW_DECOMPOSITION_PLAN.md

using System.Threading.Tasks;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Coordinates panel navigation: OpenPanelByIdAsync, command resolution, region targeting,
/// search-result-to-panel navigation, nav button state sync.
/// MainWindow delegates to this coordinator instead of owning navigation logic.
/// </summary>
public interface IShellNavigationCoordinator
{
    /// <summary>
    /// Opens a panel by its canonical registry ID.
    /// </summary>
    /// <param name="panelId">Canonical panel ID (e.g. "Timeline", "EffectsMixer").</param>
    /// <param name="overrideRegion">Optional region override; defaults to registry-defined region.</param>
    /// <returns>True if the panel was opened; false if ID not found.</returns>
    Task<bool> OpenPanelByIdAsync(string panelId, PanelRegion? overrideRegion = null);

    /// <summary>
    /// Executes a navigation command via CommandRouter, falling back to OpenPanelByIdAsync if unavailable.
    /// </summary>
    Task ExecuteNavCommandAsync(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName);

    /// <summary>
    /// Resolves a panel ID alias (e.g. "studio", "library") to a canonical registry ID.
    /// </summary>
    string ResolvePanelIdAlias(string panelId);

    /// <summary>
    /// Gets the default region for a panel from the registry.
    /// </summary>
    PanelRegion GetPanelRegion(string panelId);

    /// <summary>
    /// Gets the display title for a panel from the registry.
    /// </summary>
    string GetPanelTitle(string panelId);
}
