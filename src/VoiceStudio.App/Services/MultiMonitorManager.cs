// Phase 5.1: Advanced Workspace System
// Task 5.1.3: Multi-monitor support using WinUI 3 DisplayArea APIs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace VoiceStudio.App.Services;

/// <summary>
/// Represents a display/monitor in the system.
/// </summary>
public class DisplayInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public RectInt32 WorkArea { get; set; }
    public RectInt32 OuterBounds { get; set; }
    public double ScaleFactor { get; set; } = 1.0;
    
    public int Width => WorkArea.Width;
    public int Height => WorkArea.Height;
    public int X => WorkArea.X;
    public int Y => WorkArea.Y;
}

/// <summary>
/// Represents a panel's position on a specific monitor.
/// </summary>
public class PanelMonitorPosition
{
    public string PanelId { get; set; } = string.Empty;
    public string DisplayId { get; set; } = string.Empty;
    public RectInt32 Bounds { get; set; }
    public bool IsFloating { get; set; }
}

/// <summary>
/// Event args for display configuration changes.
/// </summary>
public class DisplayConfigurationChangedEventArgs : EventArgs
{
    public IReadOnlyList<DisplayInfo> Displays { get; set; } = Array.Empty<DisplayInfo>();
    public DisplayInfo? PrimaryDisplay { get; set; }
    public int DisplayCount { get; set; }
}

/// <summary>
/// Manages multi-monitor support for workspace layouts.
/// </summary>
public class MultiMonitorManager
{
    private readonly List<DisplayInfo> _displays = new();
    private readonly Dictionary<string, PanelMonitorPosition> _panelPositions = new();
    private DisplayArea? _primaryDisplay;

    public event EventHandler<DisplayConfigurationChangedEventArgs>? DisplayConfigurationChanged;

    /// <summary>
    /// Gets all available displays.
    /// </summary>
    public IReadOnlyList<DisplayInfo> Displays => _displays;

    /// <summary>
    /// Gets the primary display.
    /// </summary>
    public DisplayInfo? PrimaryDisplay => _displays.FirstOrDefault(d => d.IsPrimary);

    /// <summary>
    /// Gets the number of displays.
    /// </summary>
    public int DisplayCount => _displays.Count;

    /// <summary>
    /// Gets whether multi-monitor is available.
    /// </summary>
    public bool HasMultipleMonitors => _displays.Count > 1;

    /// <summary>
    /// Initializes the multi-monitor manager and detects displays.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshDisplaysAsync();
    }

    /// <summary>
    /// Refreshes the list of available displays.
    /// </summary>
    public Task RefreshDisplaysAsync()
    {
        _displays.Clear();

        try
        {
            var displayAreas = DisplayArea.FindAll();
            _primaryDisplay = DisplayArea.Primary;

            foreach (var area in displayAreas)
            {
                var display = new DisplayInfo
                {
                    Id = area.DisplayId.Value.ToString(),
                    Name = $"Display {_displays.Count + 1}",
                    IsPrimary = area.DisplayId.Value == _primaryDisplay?.DisplayId.Value,
                    WorkArea = area.WorkArea,
                    OuterBounds = area.OuterBounds,
                };

                _displays.Add(display);
            }

            // Update display names
            for (int i = 0; i < _displays.Count; i++)
            {
                _displays[i].Name = _displays[i].IsPrimary
                    ? "Primary Display"
                    : $"Display {i + 1}";
            }

            DisplayConfigurationChanged?.Invoke(this, new DisplayConfigurationChangedEventArgs
            {
                Displays = _displays,
                PrimaryDisplay = PrimaryDisplay,
                DisplayCount = DisplayCount
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MultiMonitor] Failed to detect displays: {ex.Message}");
            
            // Add a default display entry
            _displays.Add(new DisplayInfo
            {
                Id = "default",
                Name = "Primary Display",
                IsPrimary = true,
                WorkArea = new RectInt32(0, 0, 1920, 1080)
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the display at a specific screen position.
    /// </summary>
    public DisplayInfo? GetDisplayAtPosition(int x, int y)
    {
        return _displays.FirstOrDefault(d =>
            x >= d.WorkArea.X &&
            x < d.WorkArea.X + d.WorkArea.Width &&
            y >= d.WorkArea.Y &&
            y < d.WorkArea.Y + d.WorkArea.Height);
    }

    /// <summary>
    /// Gets the display by ID.
    /// </summary>
    public DisplayInfo? GetDisplay(string displayId)
    {
        return _displays.FirstOrDefault(d => d.Id == displayId);
    }

    /// <summary>
    /// Saves a panel's position on a monitor.
    /// </summary>
    public void SavePanelPosition(string panelId, string displayId, RectInt32 bounds, bool isFloating = true)
    {
        _panelPositions[panelId] = new PanelMonitorPosition
        {
            PanelId = panelId,
            DisplayId = displayId,
            Bounds = bounds,
            IsFloating = isFloating
        };
    }

    /// <summary>
    /// Gets a panel's saved position.
    /// </summary>
    public PanelMonitorPosition? GetPanelPosition(string panelId)
    {
        return _panelPositions.TryGetValue(panelId, out var position) ? position : null;
    }

    /// <summary>
    /// Clears all saved panel positions.
    /// </summary>
    public void ClearPanelPositions()
    {
        _panelPositions.Clear();
    }

    /// <summary>
    /// Gets all panel positions for a specific display.
    /// </summary>
    public IEnumerable<PanelMonitorPosition> GetPanelPositionsForDisplay(string displayId)
    {
        return _panelPositions.Values.Where(p => p.DisplayId == displayId);
    }

    /// <summary>
    /// Centers a window on a specific display.
    /// </summary>
    public RectInt32 CenterOnDisplay(string displayId, int windowWidth, int windowHeight)
    {
        var display = GetDisplay(displayId) ?? PrimaryDisplay;
        if (display == null)
        {
            return new RectInt32(100, 100, windowWidth, windowHeight);
        }

        int x = display.WorkArea.X + (display.WorkArea.Width - windowWidth) / 2;
        int y = display.WorkArea.Y + (display.WorkArea.Height - windowHeight) / 2;

        return new RectInt32(x, y, windowWidth, windowHeight);
    }

    /// <summary>
    /// Ensures a rectangle is within the visible work area of any display.
    /// </summary>
    public RectInt32 EnsureOnScreen(RectInt32 bounds)
    {
        // Find the display that contains the center of the window
        int centerX = bounds.X + bounds.Width / 2;
        int centerY = bounds.Y + bounds.Height / 2;
        var display = GetDisplayAtPosition(centerX, centerY) ?? PrimaryDisplay;

        if (display == null)
            return bounds;

        // Clamp to work area
        int x = Math.Max(display.WorkArea.X, Math.Min(bounds.X, display.WorkArea.X + display.WorkArea.Width - bounds.Width));
        int y = Math.Max(display.WorkArea.Y, Math.Min(bounds.Y, display.WorkArea.Y + display.WorkArea.Height - bounds.Height));

        return new RectInt32(x, y, bounds.Width, bounds.Height);
    }

    /// <summary>
    /// Gets the combined work area across all displays.
    /// </summary>
    public RectInt32 GetCombinedWorkArea()
    {
        if (_displays.Count == 0)
            return new RectInt32(0, 0, 1920, 1080);

        int minX = _displays.Min(d => d.WorkArea.X);
        int minY = _displays.Min(d => d.WorkArea.Y);
        int maxX = _displays.Max(d => d.WorkArea.X + d.WorkArea.Width);
        int maxY = _displays.Max(d => d.WorkArea.Y + d.WorkArea.Height);

        return new RectInt32(minX, minY, maxX - minX, maxY - minY);
    }
}
