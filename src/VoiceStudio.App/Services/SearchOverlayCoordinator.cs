// VoiceStudio - Search Overlay Coordination (Architecture Wave, SEARCH_OVERLAY_SCOPING.md)
// Extracted from MainWindow.xaml.cs per MAINWINDOW_DECOMPOSITION_PLAN.md

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Views;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Coordinates global search overlay: show/hide, result navigation, panel routing.
/// Owns NavigateToSearchResultAsync logic; MainWindow wires and delegates.
/// </summary>
public sealed class SearchOverlayCoordinator : ISearchOverlayCoordinator
{
    private readonly Func<string, object?> _findName;
    private readonly IShellNavigationCoordinator _shellNavigation;
    private readonly IToastNotificationService? _toastService;

    public SearchOverlayCoordinator(
        Func<string, object?> findName,
        IShellNavigationCoordinator shellNavigation,
        IToastNotificationService? toastService = null)
    {
        _findName = findName ?? throw new ArgumentNullException(nameof(findName));
        _shellNavigation = shellNavigation ?? throw new ArgumentNullException(nameof(shellNavigation));
        _toastService = toastService;
    }

    /// <summary>
    /// Unit-test hook: bypass <see cref="PanelHost"/> lookup and supply focus target + navigable panel (avoids WinUI ctor off the UI thread).
    /// Production code must leave this null.
    /// </summary>
    public Func<PanelRegion, (FrameworkElement? FocusTarget, INavigatablePanel? Navigable)>? PanelNavigationTestHook { get; set; }

    private IToastNotificationService? GetToast() => _toastService ?? ServiceProvider.GetToastNotificationService();

    /// <inheritdoc />
    public void Show()
    {
        var globalSearchView = _findName("GlobalSearchView") as GlobalSearchView;
        var globalSearchOverlay = _findName("GlobalSearchOverlay") as FrameworkElement;
        if (globalSearchView != null && globalSearchOverlay != null)
        {
            globalSearchOverlay.Visibility = Visibility.Visible;
            globalSearchView.Show();
        }
    }

    /// <inheritdoc />
    public void Hide()
    {
        var globalSearchView = _findName("GlobalSearchView") as GlobalSearchView;
        var globalSearchOverlay = _findName("GlobalSearchOverlay") as FrameworkElement;
        if (globalSearchView != null && globalSearchOverlay != null)
        {
            globalSearchView.Hide();
            globalSearchOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <inheritdoc />
    public async Task HandleNavigateRequestedAsync(SearchResultItem result)
    {
        Hide();

        try
        {
            await NavigateToSearchResultAsync(result).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            GetToast()?.ShowToast(ToastType.Error, $"Could not navigate to search result: {ex.Message}", "Navigation Failed");
        }
    }

    private async Task NavigateToSearchResultAsync(SearchResultItem result)
    {
        var itemId = result.Id ?? string.Empty;
        var resultTypeRaw = result.Type ?? string.Empty;
        var resultTitle = result.Title ?? "Unknown";
        var panelId = result.PanelId?.ToLowerInvariant() ?? string.Empty;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = SearchNavigationContext.FromSearchResult(itemId, resultTypeRaw, resultTitle, panelId, cts.Token);

        var canonicalId = _shellNavigation.ResolvePanelIdAlias(panelId);

        if (string.IsNullOrEmpty(canonicalId))
        {
            GetToast()?.ShowToast(ToastType.Error, $"Could not find panel: {result.PanelId ?? "Unknown"}", "Panel Not Found");
            return;
        }

        var region = _shellNavigation.GetPanelRegion(canonicalId);
        if (!await _shellNavigation.OpenPanelByIdAsync(canonicalId, region).ConfigureAwait(true))
        {
            GetToast()?.ShowToast(ToastType.Error, $"Could not create panel: {canonicalId}", "Panel Not Found");
            return;
        }

        FrameworkElement? panelFe;
        INavigatablePanel? navigable;

        if (PanelNavigationTestHook != null)
        {
            (panelFe, navigable) = PanelNavigationTestHook(region);
        }
        else
        {
            var targetHost = region switch
            {
                PanelRegion.Left => _findName("LeftPanelHost") as PanelHost,
                PanelRegion.Center => _findName("CenterPanelHost") as PanelHost,
                PanelRegion.Right => _findName("RightPanelHost") as PanelHost,
                PanelRegion.Bottom => _findName("BottomPanelHost") as PanelHost,
                _ => null
            };

            if (targetHost == null)
            {
                GetToast()?.ShowToast(
                    ToastType.Warning,
                    $"Opened {canonicalId}, but the shell could not locate the panel host for that region.",
                    "Navigation Incomplete");
                return;
            }

            panelFe = targetHost.Content as FrameworkElement;
            navigable = panelFe as INavigatablePanel;

            if (panelFe == null)
            {
                GetToast()?.ShowToast(
                    ToastType.Warning,
                    $"Opened {canonicalId}, but the panel content is not ready yet. Try the result again in a moment.",
                    "Navigation Incomplete");
                return;
            }
        }

        var resultTypeForPanel = SearchResultTypeMapper.ToPanelResultTypeString(resultTypeRaw);

        if (string.IsNullOrEmpty(itemId))
        {
            if (panelFe != null)
                TryFocusPanel(panelFe);
            GetToast()?.ShowToast(
                ToastType.Info,
                $"Opened {canonicalId}: \"{resultTitle}\" has no item id to focus.",
                "Panel Opened");
            return;
        }

        if (navigable == null)
        {
            if (panelFe != null)
                TryFocusPanel(panelFe);
            GetToast()?.ShowToast(
                ToastType.Info,
                $"Opened {canonicalId}. This panel does not support jumping to a specific item from search.",
                "Panel Opened");
            return;
        }

        IReadOnlyDictionary<string, object>? meta = result.Metadata is { Count: > 0 } ? result.Metadata : null;
        var selected = await TrySelectItemInPanelAsync(navigable, itemId, resultTypeForPanel, meta, cts.Token).ConfigureAwait(true);
        if (selected)
        {
            if (panelFe != null)
                TryFocusPanel(panelFe);
            GetToast()?.ShowToast(ToastType.Success, $"Opened {canonicalId} and selected: {resultTitle}", "Navigation Complete");
            return;
        }

        if (panelFe != null)
            TryFocusPanel(panelFe);
        GetToast()?.ShowToast(
            ToastType.Warning,
            $"Opened {canonicalId}, but could not select \"{resultTitle}\" (item missing, wrong type for this panel, or not loaded).",
            "Selection Incomplete");
    }

    private static void TryFocusPanel(FrameworkElement panelView)
    {
        try
        {
            _ = panelView.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Best effort panel focus failed: {ex.Message}", "SearchOverlayCoordinator.TryFocusPanel");
        }
    }

    private static async Task<bool> TrySelectItemInPanelAsync(
        INavigatablePanel nav,
        string itemId,
        string resultTypeForPanel,
        IReadOnlyDictionary<string, object>? searchMetadata,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(itemId))
            return false;

        return await nav.NavigateToItemAsync(itemId, resultTypeForPanel, cancellationToken, searchMetadata)
            .ConfigureAwait(true);
    }
}
