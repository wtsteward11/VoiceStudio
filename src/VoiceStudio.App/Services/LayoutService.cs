// VoiceStudio - Panel Architecture Phase 3: Workspace System
// LayoutService implements low-level panel layout operations
//
// GAP-014: Not registered in AppServices DI. Legacy helper for WorkspaceService when constructed manually;
// live shell uses PanelStateService + MainWindow restore.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Internal state for tracking panel layout.
/// </summary>
internal class PanelLayoutState
{
    public string PanelId { get; set; } = string.Empty;
    public PanelRegion Region { get; set; }
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsCollapsed { get; set; } = false;
    public double RelativeWidth { get; set; } = 1.0;
    public double RelativeHeight { get; set; } = 1.0;
}

/// <summary>
/// Manages low-level panel layout operations.
/// </summary>
public class LayoutService : ILayoutService
{
    private readonly IPanelRegistry _panelRegistry;
    private readonly ILogger<LayoutService>? _logger;
    private readonly object _lock = new();
    
    // Track layout state for each panel
    private readonly Dictionary<string, PanelLayoutState> _layoutState = new();

    public LayoutService(IPanelRegistry panelRegistry, ILogger<LayoutService>? logger = null)
    {
        _panelRegistry = panelRegistry ?? throw new ArgumentNullException(nameof(panelRegistry));
        _logger = logger;

        // Initialize state from registered panels
        InitializeFromRegistry();
    }

    private void InitializeFromRegistry()
    {
        var registeredPanels = _panelRegistry.GetAllDescriptors();
        foreach (var descriptor in registeredPanels)
        {
            if (!_layoutState.ContainsKey(descriptor.PanelId))
            {
                _layoutState[descriptor.PanelId] = new PanelLayoutState
                {
                    PanelId = descriptor.PanelId,
                    Region = descriptor.DefaultRegion,
                    IsVisible = true
                };
            }
        }
    }

    #region Panel Visibility

    public void ShowPanel(string panelId)
    {
        lock (_lock)
        {
            if (!_layoutState.TryGetValue(panelId, out var state))
            {
                _logger?.LogWarning("Panel not found for ShowPanel: {PanelId}", panelId);
                return;
            }

            if (!state.IsVisible)
            {
                state.IsVisible = true;
                OnPanelVisibilityChanged(new PanelVisibilityChangedEventArgs(panelId, true));
                _logger?.LogDebug("Showed panel: {PanelId}", panelId);
            }
        }
    }

    public void HidePanel(string panelId)
    {
        lock (_lock)
        {
            if (!_layoutState.TryGetValue(panelId, out var state))
            {
                _logger?.LogWarning("Panel not found for HidePanel: {PanelId}", panelId);
                return;
            }

            if (state.IsVisible)
            {
                state.IsVisible = false;
                OnPanelVisibilityChanged(new PanelVisibilityChangedEventArgs(panelId, false));
                _logger?.LogDebug("Hid panel: {PanelId}", panelId);
            }
        }
    }

    public void TogglePanel(string panelId)
    {
        lock (_lock)
        {
            if (!_layoutState.TryGetValue(panelId, out var state))
            {
                _logger?.LogWarning("Panel not found for TogglePanel: {PanelId}", panelId);
                return;
            }

            state.IsVisible = !state.IsVisible;
            OnPanelVisibilityChanged(new PanelVisibilityChangedEventArgs(panelId, state.IsVisible));
            _logger?.LogDebug("Toggled panel {PanelId} to {Visible}", panelId, state.IsVisible);
        }
    }

    public bool IsPanelVisible(string panelId)
    {
        lock (_lock)
        {
            return _layoutState.TryGetValue(panelId, out var state) && state.IsVisible;
        }
    }

    #endregion

    #region Panel Region Management

    public void MovePanel(string panelId, PanelRegion targetRegion)
    {
        lock (_lock)
        {
            if (!_layoutState.TryGetValue(panelId, out var state))
            {
                _logger?.LogWarning("Panel not found for MovePanel: {PanelId}", panelId);
                return;
            }

            var oldRegion = state.Region;
            if (oldRegion == targetRegion)
                return;

            state.Region = targetRegion;
            state.Order = GetNextOrderInRegion(targetRegion);

            OnPanelRegionChanged(new PanelRegionChangedEventArgs(panelId, oldRegion, targetRegion));
            _logger?.LogDebug("Moved panel {PanelId} from {Old} to {New}", panelId, oldRegion, targetRegion);
        }
    }

    public PanelRegion? GetPanelRegion(string panelId)
    {
        lock (_lock)
        {
            if (_layoutState.TryGetValue(panelId, out var state))
                return state.Region;
            return null;
        }
    }

    public void ReorderPanels(PanelRegion region, IEnumerable<string> orderedPanelIds)
    {
        lock (_lock)
        {
            var order = 0;
            foreach (var panelId in orderedPanelIds)
            {
                if (_layoutState.TryGetValue(panelId, out var state) && state.Region == region)
                {
                    state.Order = order++;
                }
            }

            _logger?.LogDebug("Reordered panels in region {Region}", region);
        }
    }

    private int GetNextOrderInRegion(PanelRegion region)
    {
        // Must be called within lock
        var maxOrder = _layoutState.Values
            .Where(s => s.Region == region)
            .Select(s => s.Order)
            .DefaultIfEmpty(-1)
            .Max();
        return maxOrder + 1;
    }

    #endregion

    #region Panel Size

    public void SetPanelSize(string panelId, double relativeWidth, double relativeHeight)
    {
        lock (_lock)
        {
            if (!_layoutState.TryGetValue(panelId, out var state))
            {
                _logger?.LogWarning("Panel not found for SetPanelSize: {PanelId}", panelId);
                return;
            }

            state.RelativeWidth = Math.Clamp(relativeWidth, 0.1, 1.0);
            state.RelativeHeight = Math.Clamp(relativeHeight, 0.1, 1.0);
            _logger?.LogDebug("Set panel {PanelId} size to {W}x{H}", panelId, relativeWidth, relativeHeight);
        }
    }

    public void CollapsePanel(string panelId)
    {
        lock (_lock)
        {
            if (_layoutState.TryGetValue(panelId, out var state))
            {
                state.IsCollapsed = true;
                _logger?.LogDebug("Collapsed panel: {PanelId}", panelId);
            }
        }
    }

    public void ExpandPanel(string panelId)
    {
        lock (_lock)
        {
            if (_layoutState.TryGetValue(panelId, out var state))
            {
                state.IsCollapsed = false;
                _logger?.LogDebug("Expanded panel: {PanelId}", panelId);
            }
        }
    }

    public bool IsPanelCollapsed(string panelId)
    {
        lock (_lock)
        {
            return _layoutState.TryGetValue(panelId, out var state) && state.IsCollapsed;
        }
    }

    #endregion

    #region Layout Capture

    public IReadOnlyList<PanelPlacement> CaptureCurrentLayout()
    {
        lock (_lock)
        {
            return _layoutState.Values
                .Select(state => new PanelPlacement
                {
                    PanelId = state.PanelId,
                    Region = state.Region,
                    Order = state.Order,
                    IsVisible = state.IsVisible,
                    IsCollapsed = state.IsCollapsed,
                    RelativeWidth = state.RelativeWidth,
                    RelativeHeight = state.RelativeHeight
                })
                .OrderBy(p => p.Region)
                .ThenBy(p => p.Order)
                .ToList();
        }
    }

    public Task ApplyLayoutAsync(IReadOnlyList<PanelPlacement> placements)
    {
        lock (_lock)
        {
            // First, hide all panels not in the new layout
            var placementIds = new HashSet<string>(placements.Select(p => p.PanelId));
            foreach (var state in _layoutState.Values)
            {
                if (!placementIds.Contains(state.PanelId))
                {
                    state.IsVisible = false;
                }
            }

            // Apply each placement
            foreach (var placement in placements)
            {
                if (!_layoutState.TryGetValue(placement.PanelId, out var state))
                {
                    // Create state for unknown panel
                    state = new PanelLayoutState { PanelId = placement.PanelId };
                    _layoutState[placement.PanelId] = state;
                }

                var oldRegion = state.Region;
                var wasVisible = state.IsVisible;

                state.Region = placement.Region;
                state.Order = placement.Order;
                state.IsVisible = placement.IsVisible;
                state.IsCollapsed = placement.IsCollapsed;
                state.RelativeWidth = placement.RelativeWidth ?? 1.0;
                state.RelativeHeight = placement.RelativeHeight ?? 1.0;

                // Fire events for changes
                if (state.IsVisible != wasVisible)
                {
                    OnPanelVisibilityChanged(new PanelVisibilityChangedEventArgs(placement.PanelId, state.IsVisible));
                }

                if (state.Region != oldRegion)
                {
                    OnPanelRegionChanged(new PanelRegionChangedEventArgs(placement.PanelId, oldRegion, state.Region));
                }
            }

            _logger?.LogInformation("Applied layout with {Count} panel placements", placements.Count);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Events

    public event EventHandler<PanelVisibilityChangedEventArgs>? PanelVisibilityChanged;
    public event EventHandler<PanelRegionChangedEventArgs>? PanelRegionChanged;

    private void OnPanelVisibilityChanged(PanelVisibilityChangedEventArgs args)
    {
        PanelVisibilityChanged?.Invoke(this, args);
    }

    private void OnPanelRegionChanged(PanelRegionChangedEventArgs args)
    {
        PanelRegionChanged?.Invoke(this, args);
    }

    #endregion
}
