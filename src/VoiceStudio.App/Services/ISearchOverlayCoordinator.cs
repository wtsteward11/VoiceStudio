// VoiceStudio - Search Overlay Coordination (Architecture Wave, SEARCH_OVERLAY_SCOPING.md)
// Extracted from MainWindow.xaml.cs per MAINWINDOW_DECOMPOSITION_PLAN.md

using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Coordinates global search overlay: show/hide, result navigation, panel routing.
/// MainWindow delegates to this coordinator instead of owning search overlay logic.
/// </summary>
public interface ISearchOverlayCoordinator
{
    /// <summary>
    /// Shows the global search overlay.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the global search overlay.
    /// </summary>
    void Hide();

    /// <summary>
    /// Handles a search result navigation request. Opens the appropriate panel and selects the item.
    /// </summary>
    /// <param name="result">The search result to navigate to.</param>
    Task HandleNavigateRequestedAsync(VoiceStudio.Core.Models.SearchResultItem result);
}
