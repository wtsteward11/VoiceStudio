using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace VoiceStudio.App.Services;

/// <summary>
/// Accessibility service for WCAG compliance.
/// 
/// Phase 15.2: Accessibility
/// Phase 5.0: Service Unification - Now implements IUnifiedAccessibilityService.
/// Provides comprehensive accessibility support including screen reader,
/// high contrast, reduced motion, and focus management.
/// </summary>
public class AccessibilityService : IUnifiedAccessibilityService
{
    private readonly UISettings _uiSettings;
    private readonly AccessibilitySettings _accessibilitySettings;
    private bool _isHighContrastEnabled;
    private bool _isReducedMotionEnabled;
    private bool _isScreenReaderActive;
    private UnifiedAccessibilitySettings _settings = new();

    public event EventHandler<AccessibilitySettingsChangedEventArgs>? SettingsChanged;

    public AccessibilityService()
    {
        _uiSettings = new UISettings();
        _accessibilitySettings = new AccessibilitySettings();

        // Initialize settings
        _isHighContrastEnabled = _accessibilitySettings.HighContrast;
        _isReducedMotionEnabled = !_uiSettings.AnimationsEnabled;
        _isScreenReaderActive = CheckScreenReaderActive();

        // Listen for changes
        _accessibilitySettings.HighContrastChanged += OnHighContrastChanged;
        _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
    }

    #region Properties

    /// <summary>
    /// Gets the current accessibility settings.
    /// </summary>
    public UnifiedAccessibilitySettings Settings => _settings;

    /// <summary>
    /// Gets whether high contrast mode is enabled.
    /// </summary>
    public bool IsHighContrastEnabled => _isHighContrastEnabled || _settings.HighContrastMode;

    /// <summary>
    /// Gets whether reduced motion is preferred.
    /// </summary>
    public bool IsReducedMotionEnabled => _isReducedMotionEnabled || _settings.ReduceMotion;

    /// <summary>
    /// Gets whether a screen reader is likely active.
    /// </summary>
    public bool IsScreenReaderActive => _isScreenReaderActive;

    /// <summary>
    /// Gets whether animations should be used.
    /// </summary>
    public bool ShouldUseAnimations => !IsReducedMotionEnabled;

    /// <summary>
    /// Gets the current text scale factor.
    /// </summary>
    public double TextScaleFactor => _uiSettings.TextScaleFactor * _settings.TextScaling;

    #endregion

    #region Screen Reader Support

    /// <summary>
    /// Checks if a screen reader is likely active.
    /// </summary>
    private static bool CheckScreenReaderActive()
    {
        // Check if automation listeners exist (heuristic for screen reader presence)
        return AutomationPeer.ListenerExists(AutomationEvents.AutomationFocusChanged) ||
               AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>
    /// Announce a message to screen readers.
    /// </summary>
    public void Announce(string message, AutomationNotificationKind kind = AutomationNotificationKind.Other)
    {
        // Create a temporary TextBlock for announcement
        var announcer = new TextBlock
        {
            Text = message,
        };

        AutomationProperties.SetLiveSetting(announcer, AutomationLiveSetting.Assertive);

        // Raise notification
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(announcer);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    /// <summary>
    /// Announce a polite message (non-interrupting).
    /// </summary>
    public void AnnouncePolite(string message)
    {
        var announcer = new TextBlock
        {
            Text = message,
        };

        AutomationProperties.SetLiveSetting(announcer, AutomationLiveSetting.Polite);

        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(announcer);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    /// <summary>
    /// Set accessible name for an element (static helper).
    /// </summary>
    public static void SetAccessibleNameStatic(UIElement element, string name)
    {
        AutomationProperties.SetName(element, name);
    }

    /// <summary>
    /// Set accessible description for an element (static helper).
    /// </summary>
    public static void SetAccessibleDescriptionStatic(UIElement element, string description)
    {
        AutomationProperties.SetHelpText(element, description);
    }

    /// <summary>
    /// Set accessible label for an element (static helper).
    /// </summary>
    public static void SetLabeledByStatic(UIElement element, UIElement label)
    {
        AutomationProperties.SetLabeledBy(element, label);
    }

    /// <summary>
    /// Set live region settings for dynamic content (static helper).
    /// </summary>
    public static void SetLiveRegionStatic(UIElement element, AutomationLiveSetting setting)
    {
        AutomationProperties.SetLiveSetting(element, setting);
    }

    #endregion

    #region Focus Management

    /// <summary>
    /// Set focus to an element with optional announcement.
    /// </summary>
    public void SetFocusWithAnnouncement(Control control, string? announcement = null)
    {
        control.Focus(FocusState.Programmatic);
        if (!string.IsNullOrEmpty(announcement))
        {
            Announce(announcement, AnnouncePriority.Polite);
        }
    }

    /// <summary>
    /// Create a focus trap for modal dialogs.
    /// </summary>
    public FocusTrap CreateFocusTrap(UIElement container)
    {
        return new FocusTrap(container);
    }

    /// <summary>
    /// Get focusable elements in a container (static helper).
    /// </summary>
    public static IEnumerable<Control> GetFocusableElementsStatic(DependencyObject container)
    {
        var focusable = new List<Control>();
        GetFocusableElementsRecursive(container, focusable);
        return focusable;
    }

    private static void GetFocusableElementsRecursive(DependencyObject parent, List<Control> focusable)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is Control control &&
                control.IsEnabled &&
                control.IsTabStop &&
                control.Visibility == Visibility.Visible)
            {
                focusable.Add(control);
            }

            GetFocusableElementsRecursive(child, focusable);
        }
    }

    #endregion

    #region Contrast and Colors

    /// <summary>
    /// Get high contrast safe color.
    /// </summary>
    public Windows.UI.Color GetAccessibleColor(Windows.UI.Color normalColor, Windows.UI.Color highContrastColor)
    {
        return _isHighContrastEnabled ? highContrastColor : normalColor;
    }

    /// <summary>
    /// Check if two colors have sufficient contrast (WCAG AA) - static helper.
    /// </summary>
    public static bool HasSufficientContrastStatic(Windows.UI.Color foreground, Windows.UI.Color background, double minimumRatio = 4.5)
    {
        var ratio = CalculateContrastRatioStatic(foreground, background);
        return ratio >= minimumRatio;
    }

    /// <summary>
    /// Calculate contrast ratio between two colors - static helper.
    /// </summary>
    public static double CalculateContrastRatioStatic(Windows.UI.Color foreground, Windows.UI.Color background)
    {
        var l1 = GetRelativeLuminance(foreground);
        var l2 = GetRelativeLuminance(background);

        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double GetRelativeLuminance(Windows.UI.Color color)
    {
        double r = GetLuminanceComponent(color.R / 255.0);
        double g = GetLuminanceComponent(color.G / 255.0);
        double b = GetLuminanceComponent(color.B / 255.0);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double GetLuminanceComponent(double value)
    {
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    #endregion

    #region Motion and Animation

    /// <summary>
    /// Get animation duration respecting reduced motion preference.
    /// </summary>
    public TimeSpan GetAnimationDuration(TimeSpan normalDuration)
    {
        return _isReducedMotionEnabled ? TimeSpan.Zero : normalDuration;
    }

    /// <summary>
    /// Check if animations should be skipped.
    /// </summary>
    public bool ShouldSkipAnimation() => _isReducedMotionEnabled;

    #endregion

    #region Text Scaling

    /// <summary>
    /// Get scaled font size.
    /// </summary>
    public double GetScaledFontSize(double baseFontSize)
    {
        var textScaleFactor = _uiSettings.TextScaleFactor;
        return baseFontSize * textScaleFactor;
    }

    // Note: TextScaleFactor property is defined in Properties region (line 76)

    #endregion

    #region Event Handlers

    private void OnHighContrastChanged(AccessibilitySettings sender, object args)
    {
        var oldValue = _isHighContrastEnabled;
        _isHighContrastEnabled = sender.HighContrast;
        _settings.HighContrastMode = _isHighContrastEnabled;
        SettingsChanged?.Invoke(this, new AccessibilitySettingsChangedEventArgs
        {
            SettingName = "HighContrast",
            OldValue = oldValue,
            NewValue = _isHighContrastEnabled,
        });
    }

    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        var oldValue = _isReducedMotionEnabled;
        _isReducedMotionEnabled = !_uiSettings.AnimationsEnabled;
        _settings.ReduceMotion = _isReducedMotionEnabled;
        SettingsChanged?.Invoke(this, new AccessibilitySettingsChangedEventArgs
        {
            SettingName = "ReducedMotion",
            OldValue = oldValue,
            NewValue = _isReducedMotionEnabled,
        });
    }

    #endregion

    #region IUnifiedAccessibilityService Implementation

    /// <summary>
    /// Initialize the accessibility service and detect system settings.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Detect system settings
        _isHighContrastEnabled = _accessibilitySettings.HighContrast;
        _isReducedMotionEnabled = !_uiSettings.AnimationsEnabled;
        _isScreenReaderActive = CheckScreenReaderActive();

        // Load persisted user preferences
        await LoadSettingsAsync();

        // Sync settings
        _settings.HighContrastMode = _isHighContrastEnabled;
        _settings.ReduceMotion = _isReducedMotionEnabled;
        _settings.TextScaling = _uiSettings.TextScaleFactor;
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            if (localSettings.Values.TryGetValue("Accessibility_ScreenReaderAnnouncements", out var sr))
                _settings.ScreenReaderAnnouncements = (bool)sr;

            if (localSettings.Values.TryGetValue("Accessibility_VerboseDescriptions", out var vd))
                _settings.VerboseDescriptions = (bool)vd;

            if (localSettings.Values.TryGetValue("Accessibility_ColorBlindSupport", out var cbs))
                _settings.ColorBlindSupport = (bool)cbs;

            if (localSettings.Values.TryGetValue("Accessibility_ColorBlindType", out var cbt))
            {
                if (Enum.TryParse<ColorBlindnessType>((string)cbt, out var type))
                    _settings.ColorBlindType = type;
            }

            if (localSettings.Values.TryGetValue("Accessibility_LargeText", out var lt))
                _settings.LargeText = (bool)lt;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Accessibility] Failed to load settings: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Saves settings to persistent storage.
    /// </summary>
    public async Task SaveSettingsAsync()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            localSettings.Values["Accessibility_ScreenReaderAnnouncements"] = _settings.ScreenReaderAnnouncements;
            localSettings.Values["Accessibility_VerboseDescriptions"] = _settings.VerboseDescriptions;
            localSettings.Values["Accessibility_ColorBlindSupport"] = _settings.ColorBlindSupport;
            localSettings.Values["Accessibility_ColorBlindType"] = _settings.ColorBlindType.ToString();
            localSettings.Values["Accessibility_LargeText"] = _settings.LargeText;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Accessibility] Failed to save settings: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Announces a message with the specified priority.
    /// </summary>
    public void Announce(string message, AnnouncePriority priority = AnnouncePriority.Polite)
    {
        if (!_settings.ScreenReaderAnnouncements || priority == AnnouncePriority.Off)
            return;

        var kind = priority == AnnouncePriority.Assertive
            ? AutomationNotificationKind.Other
            : AutomationNotificationKind.Other;

        Announce(message, kind);
    }

    /// <summary>
    /// Announces a status update.
    /// </summary>
    public void AnnounceStatus(string status)
    {
        Announce(status, AnnouncePriority.Polite);
    }

    /// <summary>
    /// Announces an error message.
    /// </summary>
    public void AnnounceError(string error)
    {
        var message = _settings.VerboseDescriptions
            ? $"Error: {error}. Please review and try again."
            : $"Error: {error}";

        Announce(message, AnnouncePriority.Assertive);
    }

    /// <summary>
    /// Announces a success message.
    /// </summary>
    public void AnnounceSuccess(string message)
    {
        var prefix = _settings.VerboseDescriptions ? "Success: " : "";
        Announce($"{prefix}{message}", AnnouncePriority.Polite);
    }

    /// <summary>
    /// Sets automation name for an element.
    /// </summary>
    public void SetAccessibleName(UIElement element, string name)
    {
        AutomationProperties.SetName(element, name);
    }

    /// <summary>
    /// Sets automation help text for an element.
    /// </summary>
    public void SetAccessibleDescription(UIElement element, string description)
    {
        AutomationProperties.SetHelpText(element, description);
    }

    /// <summary>
    /// Sets the labeled-by relationship (IUnifiedAccessibilityService).
    /// </summary>
    public void SetLabeledBy(UIElement element, UIElement label)
    {
        SetLabeledByStatic(element, label);
    }

    /// <summary>
    /// Sets live region settings for dynamic content (IUnifiedAccessibilityService).
    /// </summary>
    public void SetLiveRegion(UIElement element, AutomationLiveSetting setting)
    {
        SetLiveRegionStatic(element, setting);
    }

    /// <summary>
    /// Gets focusable elements in a container (IUnifiedAccessibilityService).
    /// </summary>
    public IEnumerable<Control> GetFocusableElements(DependencyObject container)
    {
        return GetFocusableElementsStatic(container);
    }

    /// <summary>
    /// Checks if two colors have sufficient contrast (IUnifiedAccessibilityService).
    /// </summary>
    public bool HasSufficientContrast(Windows.UI.Color foreground, Windows.UI.Color background, double minimumRatio = 4.5)
    {
        return HasSufficientContrastStatic(foreground, background, minimumRatio);
    }

    /// <summary>
    /// Calculate contrast ratio between two colors (IUnifiedAccessibilityService).
    /// </summary>
    public double CalculateContrastRatio(Windows.UI.Color foreground, Windows.UI.Color background)
    {
        return CalculateContrastRatioStatic(foreground, background);
    }

    /// <summary>
    /// Gets a color adjusted for color blindness.
    /// </summary>
    public Windows.UI.Color GetAccessibleColor(Windows.UI.Color originalColor)
    {
        if (!_settings.ColorBlindSupport)
            return originalColor;

        return _settings.ColorBlindType switch
        {
            ColorBlindnessType.Protanopia => TransformForProtanopia(originalColor),
            ColorBlindnessType.Deuteranopia => TransformForDeuteranopia(originalColor),
            ColorBlindnessType.Tritanopia => TransformForTritanopia(originalColor),
            ColorBlindnessType.Achromatopsia => TransformToGrayscale(originalColor),
            _ => originalColor
        };
    }

    private static Windows.UI.Color TransformForProtanopia(Windows.UI.Color color)
    {
        var r = (byte)(color.R * 0.567 + color.G * 0.433);
        var g = (byte)(color.R * 0.558 + color.G * 0.442);
        return Windows.UI.Color.FromArgb(color.A, r, g, color.B);
    }

    private static Windows.UI.Color TransformForDeuteranopia(Windows.UI.Color color)
    {
        var r = (byte)(color.R * 0.625 + color.G * 0.375);
        var g = (byte)(color.R * 0.7 + color.G * 0.3);
        return Windows.UI.Color.FromArgb(color.A, r, g, color.B);
    }

    private static Windows.UI.Color TransformForTritanopia(Windows.UI.Color color)
    {
        var g = (byte)(color.G * 0.95 + color.B * 0.05);
        var b = (byte)(color.G * 0.433 + color.B * 0.567);
        return Windows.UI.Color.FromArgb(color.A, color.R, g, b);
    }

    private static Windows.UI.Color TransformToGrayscale(Windows.UI.Color color)
    {
        var gray = (byte)(color.R * 0.299 + color.G * 0.587 + color.B * 0.114);
        return Windows.UI.Color.FromArgb(color.A, gray, gray, gray);
    }

    // Note: GetAnimationDuration and ShouldSkipAnimation are defined in Motion and Animation region

    /// <summary>
    /// Updates accessibility settings.
    /// </summary>
    public void UpdateSettings(Action<UnifiedAccessibilitySettings> update)
    {
        update(_settings);
        _ = SaveSettingsAsync();
    }

    #endregion

    #region Cleanup

    public void Dispose()
    {
        _accessibilitySettings.HighContrastChanged -= OnHighContrastChanged;
        _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
    }

    #endregion
}

/// <summary>
/// Focus trap for modal dialogs to keep focus within container.
/// </summary>
public class FocusTrap : IDisposable
{
    private readonly UIElement _container;
    private readonly List<Control> _focusableElements;
    private bool _isActive;

    public FocusTrap(UIElement container)
    {
        _container = container;
        _focusableElements = new List<Control>(AccessibilityService.GetFocusableElementsStatic(container));
    }

    public void Activate()
    {
        _isActive = true;
        if (_focusableElements.Count > 0)
        {
            _focusableElements[0].Focus(FocusState.Programmatic);
        }
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    public void HandleTabNavigation(bool isShiftPressed)
    {
        if (!_isActive || _focusableElements.Count == 0)
            return;

        var currentFocus = FocusManager.GetFocusedElement() as Control;
        var currentIndex = _focusableElements.IndexOf(currentFocus!);

        int nextIndex;
        if (isShiftPressed)
        {
            nextIndex = currentIndex <= 0 ? _focusableElements.Count - 1 : currentIndex - 1;
        }
        else
        {
            nextIndex = currentIndex >= _focusableElements.Count - 1 ? 0 : currentIndex + 1;
        }

        _focusableElements[nextIndex].Focus(FocusState.Keyboard);
    }

    public void Dispose()
    {
        Deactivate();
    }
}

/// <summary>
/// Event args for accessibility setting changes.
/// </summary>
public class AccessibilityChangedEventArgs : EventArgs
{
    public string SettingName { get; set; } = string.Empty;
    public object? NewValue { get; set; }
}

/// <summary>
/// Phase 5.3.5: ARIA-style live region manager for dynamic content announcements.
/// Creates and manages live region elements for screen reader announcements.
/// </summary>
public class LiveRegionManager
{
    private readonly Dictionary<string, TextBlock> _regions = new();
    private readonly Panel _container;

    public LiveRegionManager(Panel container)
    {
        _container = container;
    }

    /// <summary>
    /// Creates a named live region for status announcements.
    /// </summary>
    public void CreateRegion(string id, AutomationLiveSetting setting = AutomationLiveSetting.Polite)
    {
        if (_regions.ContainsKey(id))
            return;

        var region = new TextBlock
        {
            Name = $"LiveRegion_{id}",
            Opacity = 0,
            IsHitTestVisible = false,
            Width = 1,
            Height = 1
        };

        AutomationProperties.SetLiveSetting(region, setting);
        AutomationProperties.SetName(region, id);

        _regions[id] = region;
        _container.Children.Add(region);
    }

    /// <summary>
    /// Updates a live region with new content, triggering a screen reader announcement.
    /// </summary>
    public void Update(string regionId, string message)
    {
        if (!_regions.TryGetValue(regionId, out var region))
            return;

        // Clear and set text to trigger change notification
        region.Text = string.Empty;
        region.Text = message;

        // Raise automation event
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(region);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    /// <summary>
    /// Updates a live region with assertive (interrupting) priority.
    /// </summary>
    public void UpdateAssertive(string regionId, string message)
    {
        if (_regions.TryGetValue(regionId, out var region))
        {
            AutomationProperties.SetLiveSetting(region, AutomationLiveSetting.Assertive);
        }
        Update(regionId, message);
    }

    /// <summary>
    /// Removes a live region.
    /// </summary>
    public void RemoveRegion(string id)
    {
        if (_regions.TryGetValue(id, out var region))
        {
            _container.Children.Remove(region);
            _regions.Remove(id);
        }
    }

    /// <summary>
    /// Gets all registered region IDs.
    /// </summary>
    public IEnumerable<string> GetRegionIds() => _regions.Keys;

    /// <summary>
    /// Clears all live regions.
    /// </summary>
    public void Clear()
    {
        foreach (var region in _regions.Values)
        {
            _container.Children.Remove(region);
        }
        _regions.Clear();
    }
}

/// <summary>
/// Phase 5.3.1: Text scaling support utilities.
/// </summary>
public static class TextScalingExtensions
{
    /// <summary>
    /// Applies text scaling to a TextBlock based on accessibility settings.
    /// </summary>
    public static void ApplyTextScaling(this TextBlock textBlock, AccessibilityService accessibilityService)
    {
        var baseFontSize = textBlock.FontSize;
        textBlock.FontSize = accessibilityService.GetScaledFontSize(baseFontSize);
    }

    /// <summary>
    /// Gets the scaled font size for a given base size.
    /// </summary>
    public static double ScaleFontSize(double baseFontSize, double scaleFactor)
    {
        return baseFontSize * scaleFactor;
    }

    /// <summary>
    /// Gets a scaled size for UI elements (margins, paddings, etc.).
    /// </summary>
    public static double ScaleSize(double baseSize, double scaleFactor)
    {
        return baseSize * Math.Max(1.0, (scaleFactor - 1.0) * 0.5 + 1.0);
    }
}

/// <summary>
/// Phase 5.3.2: Reduced motion preference helpers.
/// </summary>
public static class ReducedMotionExtensions
{
    /// <summary>
    /// Gets an appropriate animation duration respecting user preferences.
    /// </summary>
    public static TimeSpan GetDuration(AccessibilityService accessibility, TimeSpan normalDuration)
    {
        return accessibility.IsReducedMotionEnabled ? TimeSpan.Zero : normalDuration;
    }

    /// <summary>
    /// Gets a simplified transition for reduced motion mode.
    /// </summary>
    public static TimeSpan GetTransitionDuration(bool reduceMotion, TimeSpan normalDuration)
    {
        if (reduceMotion)
            return TimeSpan.FromMilliseconds(1); // Instant but visible

        return normalDuration;
    }
}
