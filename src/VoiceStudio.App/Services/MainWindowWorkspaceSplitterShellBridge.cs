using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 33: workspace <see cref="Grid"/> splitter pointer drag (WinUI 3 has no built-in <c>GridSplitter</c>).
/// <see cref="MainWindow"/> supplies name resolution and debounced layout save on pointer release.
/// </summary>
public sealed class MainWindowWorkspaceSplitterShellBridge
{
    private const double MinStarValue = 0.5;

    private enum SplitterKind
    {
        None,
        Vertical1,
        Vertical2,
        Horizontal
    }

    private readonly Func<string, object?> _findNameOnContent;
    private readonly Action _requestLayoutSaveOnPointerRelease;
    private SplitterKind _activeSplitter;
    private double _splitterStartX;
    private double _splitterStartY;
    private double _splitterStartLeft;
    private double _splitterStartCenter;
    private double _splitterStartRight;
    private double _splitterStartTop;
    private double _splitterStartBottom;

    public MainWindowWorkspaceSplitterShellBridge(
        Func<string, object?> findNameOnContent,
        Action requestLayoutSaveOnPointerRelease)
    {
        ArgumentNullException.ThrowIfNull(findNameOnContent);
        ArgumentNullException.ThrowIfNull(requestLayoutSaveOnPointerRelease);
        _findNameOnContent = findNameOnContent;
        _requestLayoutSaveOnPointerRelease = requestLayoutSaveOnPointerRelease;
    }

    public void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (sender is not FrameworkElement splitter)
        {
            return;
        }

        var workspaceGrid = _findNameOnContent("WorkspaceGrid") as Grid;
        var leftCol = _findNameOnContent("LeftColumn") as ColumnDefinition;
        var centerCol = _findNameOnContent("CenterColumn") as ColumnDefinition;
        var rightCol = _findNameOnContent("RightColumn") as ColumnDefinition;
        var topRow = _findNameOnContent("TopRow") as RowDefinition;
        var bottomRow = _findNameOnContent("BottomRow") as RowDefinition;
        if (workspaceGrid == null || leftCol == null || centerCol == null || rightCol == null || topRow == null || bottomRow == null)
        {
            return;
        }

        var pt = e.GetCurrentPoint(workspaceGrid);
        _splitterStartX = pt.Position.X;
        _splitterStartY = pt.Position.Y;
        _splitterStartLeft = leftCol.Width.IsStar ? leftCol.Width.Value : 20;
        _splitterStartCenter = centerCol.Width.IsStar ? centerCol.Width.Value : 55;
        _splitterStartRight = rightCol.Width.IsStar ? rightCol.Width.Value : 25;
        _splitterStartTop = topRow.Height.IsStar ? topRow.Height.Value : 4;
        _splitterStartBottom = bottomRow.Height.IsStar ? bottomRow.Height.Value : 1;

        var name = splitter.Name;
        if (StringEqualsOrdinal(name, "VerticalSplitter1"))
        {
            _activeSplitter = SplitterKind.Vertical1;
        }
        else if (StringEqualsOrdinal(name, "VerticalSplitter2"))
        {
            _activeSplitter = SplitterKind.Vertical2;
        }
        else if (StringEqualsOrdinal(name, "HorizontalSplitter"))
        {
            _activeSplitter = SplitterKind.Horizontal;
        }
        else
        {
            _activeSplitter = SplitterKind.None;
        }

        if (_activeSplitter != SplitterKind.None)
        {
            splitter.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    public void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_activeSplitter == SplitterKind.None)
        {
            return;
        }

        var workspaceGrid = _findNameOnContent("WorkspaceGrid") as Grid;
        var leftCol = _findNameOnContent("LeftColumn") as ColumnDefinition;
        var centerCol = _findNameOnContent("CenterColumn") as ColumnDefinition;
        var rightCol = _findNameOnContent("RightColumn") as ColumnDefinition;
        var topRow = _findNameOnContent("TopRow") as RowDefinition;
        var bottomRow = _findNameOnContent("BottomRow") as RowDefinition;
        if (workspaceGrid == null || leftCol == null || centerCol == null || rightCol == null || topRow == null || bottomRow == null)
        {
            return;
        }

        var pt = e.GetCurrentPoint(workspaceGrid);
        var deltaX = pt.Position.X - _splitterStartX;
        var deltaY = pt.Position.Y - _splitterStartY;

        // Scale: ~100px drag ≈ 1 star unit
        const double scale = 100.0;
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

    public void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (sender is FrameworkElement splitter && _activeSplitter != SplitterKind.None)
        {
            splitter.ReleasePointerCapture(e.Pointer);
            _activeSplitter = SplitterKind.None;
            e.Handled = true;
            _requestLayoutSaveOnPointerRelease();
        }
    }

    private static bool StringEqualsOrdinal(string? a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal);
}
