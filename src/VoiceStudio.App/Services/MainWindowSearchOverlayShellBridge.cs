using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Views;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 6: MainWindow shell wiring for the global search overlay only — forwards to
/// <see cref="ISearchOverlayCoordinator"/>. Not toolbar; not project/recent workflow; not import.
/// </summary>
public sealed class MainWindowSearchOverlayShellBridge
{
    private readonly ISearchOverlayCoordinator? _coordinator;
    private readonly Func<string, object?> _findName;

    public MainWindowSearchOverlayShellBridge(
        ISearchOverlayCoordinator? coordinator,
        Func<string, object?> findName)
    {
        _coordinator = coordinator;
        _findName = findName ?? throw new ArgumentNullException(nameof(findName));
    }

    /// <summary>
    /// True when the user tapped the dimmed overlay background (not child content).
    /// Overlay root is compared by reference; use plain CLR objects in tests without constructing WinUI elements.
    /// </summary>
    /// <param name="originalSource">Same reference as <see cref="TappedRoutedEventArgs.OriginalSource"/> for the overlay tap.</param>
    /// <param name="overlayRoot">The element returned by <c>FindName("GlobalSearchOverlay")</c> (reference compared to <paramref name="originalSource"/>).</param>
    public static bool ShouldDismissSearchOverlayOnBackgroundTap(object? originalSource, object? overlayRoot) =>
        overlayRoot != null && ReferenceEquals(originalSource, overlayRoot);

    public void Show() => _coordinator?.Show();

    public async Task OnNavigateRequestedAsync(SearchNavigationEventArgs e)
    {
        if (_coordinator == null)
        {
            return;
        }

        await _coordinator.HandleNavigateRequestedAsync(e.Result).ConfigureAwait(true);
    }

    public void OnOverlayTappedForDismiss(TappedRoutedEventArgs e) =>
        OnOverlayBackgroundTapDismiss(e?.OriginalSource);

    /// <summary>
    /// Dismisses the overlay when <paramref name="tapOriginalSource"/> is the overlay root element.
    /// Exposed for tests without constructing <see cref="TappedRoutedEventArgs"/>.
    /// </summary>
    /// <param name="tapOriginalSource">Same as <see cref="TappedRoutedEventArgs.OriginalSource"/> for the overlay tap.</param>
    public void OnOverlayBackgroundTapDismiss(object? tapOriginalSource)
    {
        var overlayRoot = _findName("GlobalSearchOverlay");
        if (!ShouldDismissSearchOverlayOnBackgroundTap(tapOriginalSource, overlayRoot))
        {
            return;
        }

        _coordinator?.Hide();
    }

    /// <summary>
    /// Sets <see cref="FrameworkElement.Visibility"/> to <see cref="Visibility.Collapsed"/> only when
    /// <paramref name="found"/> is a <see cref="FrameworkElement"/>; otherwise no-ops (headless-safe).
    /// </summary>
    /// <returns>True when visibility was set.</returns>
    public static bool TryCollapseGlobalSearchOverlayIfFrameworkElement(object? found)
    {
        if (found is not FrameworkElement fe)
        {
            return false;
        }

        fe.Visibility = Visibility.Collapsed;
        return true;
    }

    public void EnsureGlobalSearchOverlayCollapsed() =>
        TryCollapseGlobalSearchOverlayIfFrameworkElement(_findName("GlobalSearchOverlay"));
}
