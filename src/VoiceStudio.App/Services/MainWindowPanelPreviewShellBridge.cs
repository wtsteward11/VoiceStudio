// VoiceStudio — GAP-008 Slice 23: Nav rail panel preview popup on pointer hover (IDEA 20).

using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Controls;

namespace VoiceStudio.App.Services;

/// <summary>
/// Shell for left-rail nav button hover: <see cref="PanelPreviewPopup"/> with static preview text;
/// does not execute navigation, command palette, or search (other bridges OUT).
/// </summary>
public sealed class MainWindowPanelPreviewShellBridge
{
    private readonly DispatcherQueue _dispatcher;
    private PanelPreviewPopup? _panelPreviewPopup;
    private System.Threading.Timer? _previewHideTimer;

    public MainWindowPanelPreviewShellBridge(DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public void OnNavButtonPointerEntered(object sender, PointerRoutedEventArgs _)
    {
        if (sender is not ToggleButton button)
        {
            return;
        }

        _previewHideTimer?.Dispose();
        _previewHideTimer = null;

        var panelInfo = GetPanelInfoForButton(button.Name);
        if (panelInfo == null)
        {
            return;
        }

        if (_panelPreviewPopup == null)
        {
            _panelPreviewPopup = new PanelPreviewPopup();
        }

        var previewContent = CreatePreviewContent(panelInfo.Value.PanelId);
        _panelPreviewPopup.Show(
            button,
            panelInfo.Value.Title,
            panelInfo.Value.Description,
            panelInfo.Value.IconGlyph,
            previewContent);
    }

    public void OnNavButtonPointerExited(object _, PointerRoutedEventArgs __)
    {
        _previewHideTimer?.Dispose();
        _previewHideTimer = new System.Threading.Timer(
            _ => { _dispatcher.TryEnqueue(() => _panelPreviewPopup?.Hide()); },
            null,
            TimeSpan.FromMilliseconds(300),
            System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>Stops the delayed-hide timer (e.g. window <see cref="Window.Closed"/> path).</summary>
    public void DisposePreviewHideTimer()
    {
        _previewHideTimer?.Dispose();
        _previewHideTimer = null;
    }

    public static (string PanelId, string Title, string Description, string IconGlyph)? GetPanelInfoForButton(string buttonName)
    {
        return buttonName switch
        {
            "NavStudio" => ("Timeline", "Studio", "Main workspace for voice synthesis and editing. Access timeline, mixer, and all core tools.", "\uE8A5"),
            "NavProfiles" => ("Profiles", "Profiles", "Manage voice profiles and voice cloning models. Create, edit, and organize your voice library.", "\uE77B"),
            "NavLibrary" => ("Library", "Library", "Browse and organize your audio files, voice samples, and project assets.", "\uE8F1"),
            "NavEffects" => ("EffectsMixer", "Effects & Mixer", "Apply audio effects, adjust mixing parameters, and fine-tune your voice output.", "\uE8F5"),
            "NavTrain" => ("Training", "Voice Training", "Train custom voice models and improve voice cloning quality.", "\uE8F6"),
            "NavAnalyze" => ("Analyzer", "Analyzer", "Analyze audio quality, waveforms, spectral analysis, and voice characteristics.", "\uE890"),
            "NavSettings" => ("Settings", "Settings", "Configure application settings, preferences, and system options.", "\uE713"),
            "NavLogs" => ("Diagnostics", "Diagnostics", "View system logs, diagnostics, and debugging information.", "\uE8F7"),
            _ => null
        };
    }

    public static UIElement? CreatePreviewContent(string panelId)
    {
        // Must not allocate WinUI controls for unknown panel IDs (thread + GC); unknown → null with zero XAML.
        if (panelId is not (
            "Timeline" or "Profiles" or "Library" or "EffectsMixer" or "Training" or "Analyzer" or "Settings" or
            "Diagnostics"))
        {
            return null;
        }

        var stackPanel = new StackPanel { Spacing = 8 };

        switch (panelId)
        {
            case "Timeline":
                stackPanel.Children.Add(new TextBlock { Text = "• Main workspace", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Timeline and mixer", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Core synthesis tools", FontSize = 12 });
                break;

            case "Profiles":
                stackPanel.Children.Add(new TextBlock { Text = "• Voice profile management", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Quality score tracking", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Profile organization", FontSize = 12 });
                break;

            case "Library":
                stackPanel.Children.Add(new TextBlock { Text = "• Audio file browser", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Asset organization", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Quick preview", FontSize = 12 });
                break;

            case "EffectsMixer":
                stackPanel.Children.Add(new TextBlock { Text = "• Audio effects chain", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Mixing controls", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Real-time processing", FontSize = 12 });
                break;

            case "Training":
                stackPanel.Children.Add(new TextBlock { Text = "• Model training interface", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Training progress tracking", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Quality metrics", FontSize = 12 });
                break;

            case "Analyzer":
                stackPanel.Children.Add(new TextBlock { Text = "• Waveform visualization", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Spectral analysis", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Quality metrics", FontSize = 12 });
                break;

            case "Settings":
                stackPanel.Children.Add(new TextBlock { Text = "• Application preferences", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Engine configuration", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• System settings", FontSize = 12 });
                break;

            case "Diagnostics":
                stackPanel.Children.Add(new TextBlock { Text = "• System diagnostics", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Error logs", FontSize = 12 });
                stackPanel.Children.Add(new TextBlock { Text = "• Performance metrics", FontSize = 12 });
                break;
        }

        return stackPanel;
    }
}
