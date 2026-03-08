using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Dialogs;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App
{
    public sealed partial class MainWindow
    {
        private async Task InitializePanelsAsync(
          Controls.PanelHost? leftPanelHost,
          Controls.PanelHost? centerPanelHost,
          Controls.PanelHost? rightPanelHost,
          Controls.PanelHost? bottomPanelHost)
        {
            var (restored, hadRegions, failedItems) = await RestorePanelsFromLayoutAsync();
            if (!restored)
            {
                if (hadRegions)
                {
                    var msg = FormatRestoreFailureMessage(failedItems);
                    var toast = ServiceProvider.TryGetToastNotificationService();
                    toast?.ShowError(
                      msg,
                      "Restore Failed",
                      () => { _ = _panelStateService?.SwitchWorkspaceProfileAsync("studio"); });
                }
                if (leftPanelHost != null)
                {
                    await OpenPanelByIdAsync("Profiles", PanelRegion.Left);
                    SetPanelHostMeta(leftPanelHost, "Voice Profiles", "👤");
                }
                if (centerPanelHost != null)
                {
                    await OpenPanelByIdAsync("Timeline", PanelRegion.Center);
                    SetPanelHostMeta(centerPanelHost, "Timeline", "🎬");
                }
                if (rightPanelHost != null)
                {
                    await OpenPanelByIdAsync("EffectsMixer", PanelRegion.Right);
                    SetPanelHostMeta(rightPanelHost, "Effects & Mixer", "🎚️");
                }
                if (bottomPanelHost != null)
                {
                    await OpenPanelByIdAsync("Macro", PanelRegion.Bottom);
                    SetPanelHostMeta(bottomPanelHost, "Macros", "⚡");
                }
            }
            else if (failedItems.Count > 0)
            {
                var msg = FormatRestoreFailureMessage(failedItems);
                var toast = ServiceProvider.TryGetToastNotificationService();
                toast?.ShowError(
                  msg,
                  "Partial Restore Failed",
                  () => { _ = _panelStateService?.SwitchWorkspaceProfileAsync("studio"); });
            }
            SetActiveNavButton("NavStudio");
        }

        private async void ManageWorkspaces_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                var xamlRoot = (Content as FrameworkElement)?.XamlRoot;
                var dialog = new Views.Dialogs.WorkspaceManagerDialog(xamlRoot);
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var toastService = ServiceProvider.TryGetToastNotificationService();
                toastService?.ShowError(
                    "Workspace Management",
                    $"Could not open workspace manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores panels from saved workspace layout.
        /// Only the active panel per region is restored. OpenedPanels is persisted for future tab support;
        /// PanelHost currently supports a single panel per region.
        /// Returns (restored, hadRegions, failedItems): hadRegions true when layout had regions to restore;
        /// restored true when at least one succeeded; failedItems lists (region, panelId) for panels that failed.
        /// </summary>
        private async Task<(bool restored, bool hadRegions, List<(PanelRegion region, string panelId)> failedItems)> RestorePanelsFromLayoutAsync()
        {
            var failedItems = new List<(PanelRegion region, string panelId)>();
            if (_panelStateService == null)
                return (false, false, failedItems);

            try
            {
                var layout = _panelStateService.GetCurrentLayout();

                if (layout.Regions == null || layout.Regions.Count == 0)
                {
                    Debug.WriteLine("No saved regions to restore, using defaults.");
                    return (false, false, failedItems);
                }

                bool restoredAny = false;

                foreach (var regionState in layout.Regions)
                {
                    try
                    {
                        Controls.PanelHost? targetHost = regionState.Region switch
                        {
                            PanelRegion.Left => FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                            PanelRegion.Center => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                            PanelRegion.Right => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                            PanelRegion.Bottom => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                            _ => null
                        };

                        if (targetHost == null)
                            continue;

                        var activePanelId = regionState.ActivePanelId;
                        if (!string.IsNullOrEmpty(activePanelId))
                        {
                            Func<UserControl>? legacyFactory = null;
                            if (_legacyPanelRegistry.TryGetValue(activePanelId, out var legacyEntry))
                            {
                                legacyFactory = legacyEntry.Factory;
                                Debug.WriteLine($"[MainWindow] Restore using legacy factory for '{activePanelId}'");
                            }
                            var panel = await targetHost.LoadPanelAsync(activePanelId, legacyFactory);
                            if (panel != null)
                            {
                                targetHost.PanelTitle = GetPanelTitle(activePanelId);
                                targetHost.IsCollapsed = regionState.IsCollapsed;
                                restoredAny = true;
                                Debug.WriteLine($"Restored panel '{activePanelId}' to {regionState.Region}");
                            }
                            else
                            {
                                Debug.WriteLine($"Panel ID '{activePanelId}' not found in registry for region {regionState.Region}; skipping.");
                                failedItems.Add((regionState.Region, activePanelId));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] Failed to restore {regionState.Region}: {ex.Message}");
                        var pid = regionState.ActivePanelId ?? string.Empty;
                        failedItems.Add((regionState.Region, pid));
                    }
                }

                RestoreSplitterRatios(layout);

                return (restoredAny, true, failedItems);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to restore panels from layout: {ex.Message}");
                return (false, true, failedItems);
            }
        }

        private void RestoreSplitterRatios(WorkspaceLayout layout)
        {
            var leftCol = FindNameOnContent("LeftColumn") as ColumnDefinition;
            var centerCol = FindNameOnContent("CenterColumn") as ColumnDefinition;
            var rightCol = FindNameOnContent("RightColumn") as ColumnDefinition;
            var topRow = FindNameOnContent("TopRow") as RowDefinition;
            var bottomRow = FindNameOnContent("BottomRow") as RowDefinition;
            if (leftCol == null || centerCol == null || rightCol == null || topRow == null || bottomRow == null)
                return;

            var leftState = layout.Regions.FirstOrDefault(r => r.Region == PanelRegion.Left);
            var centerState = layout.Regions.FirstOrDefault(r => r.Region == PanelRegion.Center);
            var rightState = layout.Regions.FirstOrDefault(r => r.Region == PanelRegion.Right);
            var bottomState = layout.Regions.FirstOrDefault(r => r.Region == PanelRegion.Bottom);

            if (leftState?.WidthRatio > 0 && centerState?.WidthRatio > 0 && rightState?.WidthRatio > 0)
            {
                leftCol.Width = new GridLength(leftState.WidthRatio.Value, GridUnitType.Star);
                centerCol.Width = new GridLength(centerState.WidthRatio.Value, GridUnitType.Star);
                rightCol.Width = new GridLength(rightState.WidthRatio.Value, GridUnitType.Star);
                Debug.WriteLine($"Restored column ratios: L={leftState.WidthRatio:F3} C={centerState.WidthRatio:F3} R={rightState.WidthRatio:F3}");
            }

            var topState = leftState ?? centerState;
            if (topState?.HeightRatio > 0 && bottomState?.HeightRatio > 0)
            {
                topRow.Height = new GridLength(topState.HeightRatio.Value, GridUnitType.Star);
                bottomRow.Height = new GridLength(bottomState.HeightRatio.Value, GridUnitType.Star);
                Debug.WriteLine($"Restored row ratios: T={topState.HeightRatio:F3} B={bottomState.HeightRatio:F3}");
            }
        }

        /// <summary>
        /// Saves current workspace layout including all panel states and splitter ratios.
        /// </summary>
        private void SaveWorkspaceLayout()
        {
            if (_panelStateService == null)
                return;

            try
            {
                var leftCol = FindNameOnContent("LeftColumn") as ColumnDefinition;
                var centerCol = FindNameOnContent("CenterColumn") as ColumnDefinition;
                var rightCol = FindNameOnContent("RightColumn") as ColumnDefinition;
                var topRow = FindNameOnContent("TopRow") as RowDefinition;
                var bottomRow = FindNameOnContent("BottomRow") as RowDefinition;

                double colSum = (leftCol?.Width.Value ?? 20) + (centerCol?.Width.Value ?? 55) + (rightCol?.Width.Value ?? 25);
                double rowSum = (topRow?.Height.Value ?? 4) + (bottomRow?.Height.Value ?? 1);

                double leftRatio = (leftCol?.Width.Value ?? 20) / colSum;
                double centerRatio = (centerCol?.Width.Value ?? 55) / colSum;
                double rightRatio = (rightCol?.Width.Value ?? 25) / colSum;
                double topRatio = (topRow?.Height.Value ?? 4) / rowSum;
                double bottomRatio = (bottomRow?.Height.Value ?? 1) / rowSum;

                SavePanelHostRegion("LeftPanelHost", PanelRegion.Left, leftRatio, topRatio);
                SavePanelHostRegion("CenterPanelHost", PanelRegion.Center, centerRatio, topRatio);
                SavePanelHostRegion("RightPanelHost", PanelRegion.Right, rightRatio, topRatio);
                SavePanelHostRegion("BottomPanelHost", PanelRegion.Bottom, null, bottomRatio);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save workspace layout: {ex.Message}");
            }
        }

        private void SavePanelHostRegion(string hostName, PanelRegion region, double? widthRatio, double? heightRatio)
        {
            var host = FindNameOnContent(hostName) as Controls.PanelHost;
            if (host == null || _panelStateService == null)
                return;

            string activePanelId = string.Empty;
            var openedPanels = new List<string>();

            if (host.Content != null)
            {
                var panelId = GetPanelIdFromHost(host);
                if (!string.IsNullOrEmpty(panelId))
                {
                    activePanelId = panelId;
                    openedPanels.Add(panelId);
                }
            }

            _panelStateService.SaveRegionState(region, activePanelId, openedPanels, widthRatio, heightRatio);
            _panelStateService.SaveRegionCollapsedState(region, host.IsCollapsed);
        }

        private static string FormatRestoreFailureMessage(List<(PanelRegion region, string panelId)> failedItems)
        {
            if (failedItems == null || failedItems.Count == 0)
                return "Workspace restore failed — reset to Studio?";
            var take = Math.Min(5, failedItems.Count);
            var parts = failedItems.Take(take)
                .Select(x => $"'{x.panelId}' ({x.region})")
                .ToList();
            var list = string.Join(", ", parts);
            var more = failedItems.Count > 5 ? $" (+{failedItems.Count - 5} more)" : "";
            return $"Failed to restore: {list}{more}. Reset to Studio?";
        }

        private static string? GetPanelIdFromHost(Controls.PanelHost host)
        {
            if (host.Content == null) return null;

            if (host.Content is UserControl uc
                && uc.DataContext is IPanelView pv
                && !string.IsNullOrEmpty(pv.PanelId))
                return pv.PanelId;

            if (host.Content is FrameworkElement fe)
            {
                var typeName = fe.GetType().Name;
                if (typeName.EndsWith("View", StringComparison.Ordinal))
                    return typeName[..^4];
            }
            return null;
        }

        /// <summary>
        /// Handles workspace profile changes.
        /// </summary>
        private void OnWorkspaceProfileChanged(object? sender, WorkspaceProfileChangedEventArgs e)
        {
            var enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                _ = RestorePanelsFromLayoutAsync().ContinueWith(t =>
          {
                  if (t.IsFaulted)
                  {
                      Debug.WriteLine(
                  $"[MainWindow] WorkspaceProfileChanged restore failed: {t.Exception?.GetBaseException()?.Message}");
                      DispatcherQueue.TryEnqueue(() =>
                {
                      var toastService = ServiceProvider.TryGetToastNotificationService();
                      toastService?.ShowError(
                  "Workspace restore failed — reset to Studio?",
                  "Restore Failed",
                  () => { _ = _panelStateService?.SwitchWorkspaceProfileAsync("studio"); });
                  });
                  }
                  else if (t.IsCompletedSuccessfully && ((!t.Result.restored && t.Result.hadRegions) || t.Result.failedItems.Count > 0))
                  {
                      var msg = FormatRestoreFailureMessage(t.Result.failedItems);
                      var title = t.Result.restored ? "Partial Restore Failed" : "Restore Failed";
                      DispatcherQueue.TryEnqueue(() =>
                {
                      var toastService = ServiceProvider.TryGetToastNotificationService();
                      toastService?.ShowError(
                  msg,
                  title,
                  () => { _ = _panelStateService?.SwitchWorkspaceProfileAsync("studio"); });
                  });
                  }
              }, TaskScheduler.Default);
            });

            if (!enqueued)
            {
                Debug.WriteLine(
                  "[MainWindow] OnWorkspaceProfileChanged: DispatcherQueue.TryEnqueue returned false — " +
                  "window may be closing. Restore skipped.");
            }
        }

        #region Workspace Splitter Handlers

        private void WorkspaceSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement splitter)
                return;

            var workspaceGrid = FindNameOnContent("WorkspaceGrid") as Grid;
            var leftCol = FindNameOnContent("LeftColumn") as ColumnDefinition;
            var centerCol = FindNameOnContent("CenterColumn") as ColumnDefinition;
            var rightCol = FindNameOnContent("RightColumn") as ColumnDefinition;
            var topRow = FindNameOnContent("TopRow") as RowDefinition;
            var bottomRow = FindNameOnContent("BottomRow") as RowDefinition;
            if (workspaceGrid == null || leftCol == null || centerCol == null || rightCol == null || topRow == null || bottomRow == null)
                return;

            var pt = e.GetCurrentPoint(workspaceGrid);
            _splitterStartX = pt.Position.X;
            _splitterStartY = pt.Position.Y;
            _splitterStartLeft = leftCol.Width.IsStar ? leftCol.Width.Value : 20;
            _splitterStartCenter = centerCol.Width.IsStar ? centerCol.Width.Value : 55;
            _splitterStartRight = rightCol.Width.IsStar ? rightCol.Width.Value : 25;
            _splitterStartTop = topRow.Height.IsStar ? topRow.Height.Value : 4;
            _splitterStartBottom = bottomRow.Height.IsStar ? bottomRow.Height.Value : 1;

            var name = splitter.Name;
            if (string.Equals(name, "VerticalSplitter1", StringComparison.Ordinal))
                _activeSplitter = SplitterKind.Vertical1;
            else if (string.Equals(name, "VerticalSplitter2", StringComparison.Ordinal))
                _activeSplitter = SplitterKind.Vertical2;
            else if (string.Equals(name, "HorizontalSplitter", StringComparison.Ordinal))
                _activeSplitter = SplitterKind.Horizontal;
            else
                _activeSplitter = SplitterKind.None;

            if (_activeSplitter != SplitterKind.None)
            {
                splitter.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void WorkspaceSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitter == SplitterKind.None)
                return;

            var workspaceGrid = FindNameOnContent("WorkspaceGrid") as Grid;
            var leftCol = FindNameOnContent("LeftColumn") as ColumnDefinition;
            var centerCol = FindNameOnContent("CenterColumn") as ColumnDefinition;
            var rightCol = FindNameOnContent("RightColumn") as ColumnDefinition;
            var topRow = FindNameOnContent("TopRow") as RowDefinition;
            var bottomRow = FindNameOnContent("BottomRow") as RowDefinition;
            if (workspaceGrid == null || leftCol == null || centerCol == null || rightCol == null || topRow == null || bottomRow == null)
                return;

            var pt = e.GetCurrentPoint(workspaceGrid);
            var deltaX = pt.Position.X - _splitterStartX;
            var deltaY = pt.Position.Y - _splitterStartY;

            // Scale: ~100px drag ≈ 1 star unit
            var scale = 100.0;
            var dStar = deltaX / scale;
            var dStarV = deltaY / scale;

            if (_activeSplitter == SplitterKind.Vertical1)
            {
                var newLeft = Math.Max(MinStarValue, Math.Min(_splitterStartLeft + _splitterStartCenter - MinStarValue, _splitterStartLeft + dStar));
                var newCenter = _splitterStartLeft + _splitterStartCenter - newLeft;
                if (newCenter >= MinStarValue)
                {
                    leftCol.Width = new GridLength(newLeft, GridUnitType.Star);
                    centerCol.Width = new GridLength(newCenter, GridUnitType.Star);
                    _splitterStartX = pt.Position.X;
                    _splitterStartLeft = newLeft;
                    _splitterStartCenter = newCenter;
                }
            }
            else if (_activeSplitter == SplitterKind.Vertical2)
            {
                var newCenter = Math.Max(MinStarValue, Math.Min(_splitterStartCenter + _splitterStartRight - MinStarValue, _splitterStartCenter + dStar));
                var newRight = _splitterStartCenter + _splitterStartRight - newCenter;
                if (newRight >= MinStarValue)
                {
                    centerCol.Width = new GridLength(newCenter, GridUnitType.Star);
                    rightCol.Width = new GridLength(newRight, GridUnitType.Star);
                    _splitterStartX = pt.Position.X;
                    _splitterStartCenter = newCenter;
                    _splitterStartRight = newRight;
                }
            }
            else if (_activeSplitter == SplitterKind.Horizontal)
            {
                var newTop = Math.Max(MinStarValue, Math.Min(_splitterStartTop + _splitterStartBottom - MinStarValue, _splitterStartTop + dStarV));
                var newBottom = _splitterStartTop + _splitterStartBottom - newTop;
                if (newBottom >= MinStarValue)
                {
                    topRow.Height = new GridLength(newTop, GridUnitType.Star);
                    bottomRow.Height = new GridLength(newBottom, GridUnitType.Star);
                    _splitterStartY = pt.Position.Y;
                    _splitterStartTop = newTop;
                    _splitterStartBottom = newBottom;
                }
            }

            e.Handled = true;
        }

        private void WorkspaceSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement splitter && _activeSplitter != SplitterKind.None)
            {
                splitter.ReleasePointerCapture(e.Pointer);
                _activeSplitter = SplitterKind.None;
                e.Handled = true;
                _layoutSaveDebouncer?.Invoke();
            }
        }

        #endregion
    }
}
