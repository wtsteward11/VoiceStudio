// Phase 5.3: Accessibility Features
// Task 5.3.1-5.3.5: Full accessibility support
//
// DEPRECATED (Phase 5.0): This class is deprecated.
// Use VoiceStudio.App.Services.AccessibilityService which implements IUnifiedAccessibilityService.
// This file is kept for reference and will be removed in a future version.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.UI.Accessibility;

namespace VoiceStudio.App.Features.Accessibility;

/// <summary>
/// Accessibility settings.
/// </summary>
public class AccessibilitySettings
{
    // Screen reader support
    public bool ScreenReaderAnnouncements { get; set; } = true;
    public bool VerboseDescriptions { get; set; } = false;
    
    // Visual accessibility
    public bool HighContrastMode { get; set; } = false;
    public bool ReduceMotion { get; set; } = false;
    public double TextScaling { get; set; } = 1.0;
    public bool LargeText { get; set; } = false;
    
    // Color accessibility
    public bool ColorBlindSupport { get; set; } = false;
    public string ColorBlindType { get; set; } = "None"; // None, Protanopia, Deuteranopia, Tritanopia
    
    // Keyboard accessibility
    public bool KeyboardNavigationIndicator { get; set; } = true;
    public bool FocusHighlight { get; set; } = true;
    
    // Audio accessibility
    public bool CaptionsEnabled { get; set; } = false;
    public bool VisualAudioIndicators { get; set; } = true;
    
    // Motor accessibility
    public int ClickDelayMs { get; set; } = 0;
    public bool StickyKeys { get; set; } = false;
}

/// <summary>
/// Types of accessibility announcements.
/// </summary>
public enum AnnouncementType
{
    Polite,     // Wait for idle
    Assertive,  // Interrupt
    Status,     // Status update
    Error,      // Error message
    Success,    // Success message
}

/// <summary>
/// Service for accessibility features.
/// </summary>
public class AccessibilityService
{
    private AccessibilitySettings _settings = new();
    private readonly List<string> _announcementQueue = new();
    private bool _isProcessing;

    public event EventHandler<AccessibilitySettings>? SettingsChanged;
    public event EventHandler<string>? Announcement;

    public AccessibilitySettings Settings => _settings;

    /// <summary>
    /// Initialize accessibility service.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Detect system accessibility settings
        await DetectSystemSettingsAsync();
        
        // Load user preferences
        await LoadSettingsAsync();
    }

    /// <summary>
    /// Update accessibility settings.
    /// </summary>
    public void UpdateSettings(Action<AccessibilitySettings> update)
    {
        update(_settings);
        SettingsChanged?.Invoke(this, _settings);
    }

    /// <summary>
    /// Announce a message to screen readers.
    /// </summary>
    public void Announce(string message, AnnouncementType type = AnnouncementType.Polite)
    {
        if (!_settings.ScreenReaderAnnouncements)
        {
            return;
        }
        
        if (type == AnnouncementType.Assertive)
        {
            // Interrupt queue and announce immediately
            ProcessAnnouncementImmediate(message);
        }
        else
        {
            _announcementQueue.Add(message);
            ProcessAnnouncementQueue();
        }
        
        Announcement?.Invoke(this, message);
    }

    /// <summary>
    /// Announce a status update.
    /// </summary>
    public void AnnounceStatus(string status)
    {
        Announce(status, AnnouncementType.Status);
    }

    /// <summary>
    /// Announce an error.
    /// </summary>
    public void AnnounceError(string error)
    {
        var message = _settings.VerboseDescriptions
            ? $"Error: {error}. Please review and try again."
            : $"Error: {error}";
        
        Announce(message, AnnouncementType.Assertive);
    }

    /// <summary>
    /// Announce a success message.
    /// </summary>
    public void AnnounceSuccess(string message)
    {
        Announce(message, AnnouncementType.Success);
    }

    /// <summary>
    /// Set automation properties on a UI element.
    /// </summary>
    public static void SetAutomationProperties(
        UIElement element,
        string name,
        string? helpText = null,
        string? itemStatus = null)
    {
        AutomationProperties.SetName(element, name);
        
        if (helpText != null)
        {
            AutomationProperties.SetHelpText(element, helpText);
        }
        
        if (itemStatus != null)
        {
            AutomationProperties.SetItemStatus(element, itemStatus);
        }
    }

    /// <summary>
    /// Check if high contrast mode is enabled.
    /// </summary>
    public bool IsHighContrastEnabled()
    {
        var accessibilitySettings = new Windows.UI.ViewManagement.AccessibilitySettings();
        return accessibilitySettings.HighContrast || _settings.HighContrastMode;
    }

    /// <summary>
    /// Check if reduced motion is preferred.
    /// </summary>
    public bool ShouldReduceMotion()
    {
        var uiSettings = new Windows.UI.ViewManagement.UISettings();
        return !uiSettings.AnimationsEnabled || _settings.ReduceMotion;
    }

    /// <summary>
    /// Get text scaling factor.
    /// </summary>
    public double GetTextScaling()
    {
        var uiSettings = new Windows.UI.ViewManagement.UISettings();
        var systemScaling = uiSettings.TextScaleFactor;
        
        return systemScaling * _settings.TextScaling;
    }

    /// <summary>
    /// Get color for color-blind friendly display.
    /// </summary>
    public Windows.UI.Color GetAccessibleColor(
        Windows.UI.Color originalColor,
        string purpose)
    {
        if (!_settings.ColorBlindSupport)
        {
            return originalColor;
        }
        
        // Apply color transformation based on color blind type
        return _settings.ColorBlindType switch
        {
            "Protanopia" => TransformForProtanopia(originalColor),
            "Deuteranopia" => TransformForDeuteranopia(originalColor),
            "Tritanopia" => TransformForTritanopia(originalColor),
            _ => originalColor,
        };
    }

    private async Task DetectSystemSettingsAsync()
    {
        try
        {
            var accessibilitySettings = new Windows.UI.ViewManagement.AccessibilitySettings();
            _settings.HighContrastMode = accessibilitySettings.HighContrast;
            
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            _settings.ReduceMotion = !uiSettings.AnimationsEnabled;
            _settings.TextScaling = uiSettings.TextScaleFactor;
        }
        catch (Exception ex)
        {
            // Use defaults if detection fails - this can happen on some Windows versions
            System.Diagnostics.Debug.WriteLine($"[Accessibility] Failed to detect system settings: {ex.Message}");
        }
        
        await Task.CompletedTask;
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            // Attempt to load from local settings
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            
            if (localSettings.Values.TryGetValue("Accessibility_ScreenReaderAnnouncements", out var srValue))
            {
                _settings.ScreenReaderAnnouncements = (bool)srValue;
            }
            
            if (localSettings.Values.TryGetValue("Accessibility_VerboseDescriptions", out var vdValue))
            {
                _settings.VerboseDescriptions = (bool)vdValue;
            }
            
            if (localSettings.Values.TryGetValue("Accessibility_ColorBlindSupport", out var cbValue))
            {
                _settings.ColorBlindSupport = (bool)cbValue;
            }
            
            if (localSettings.Values.TryGetValue("Accessibility_ColorBlindType", out var cbtValue))
            {
                _settings.ColorBlindType = (string)cbtValue;
            }
            
            System.Diagnostics.Debug.WriteLine("[Accessibility] Settings loaded successfully");
        }
        catch (Exception ex)
        {
            // Settings storage may not be available in all contexts
            System.Diagnostics.Debug.WriteLine($"[Accessibility] Failed to load settings: {ex.Message}");
        }
        
        await Task.CompletedTask;
    }

    private void ProcessAnnouncementQueue()
    {
        if (_isProcessing || _announcementQueue.Count == 0)
        {
            return;
        }
        
        _isProcessing = true;
        
        while (_announcementQueue.Count > 0)
        {
            var message = _announcementQueue[0];
            _announcementQueue.RemoveAt(0);
            
            ProcessAnnouncementImmediate(message);
        }
        
        _isProcessing = false;
    }

    private void ProcessAnnouncementImmediate(string message)
    {
        // In a real implementation, this would use UIA notifications
        // For now, we just log and raise the event
        System.Diagnostics.Debug.WriteLine($"[Accessibility] {message}");
    }

    private Windows.UI.Color TransformForProtanopia(Windows.UI.Color color)
    {
        // Simplified protanopia simulation
        // Real implementation would use proper color matrix
        var r = (byte)(color.R * 0.567 + color.G * 0.433);
        var g = (byte)(color.R * 0.558 + color.G * 0.442);
        var b = color.B;
        
        return Windows.UI.Color.FromArgb(color.A, r, g, b);
    }

    private Windows.UI.Color TransformForDeuteranopia(Windows.UI.Color color)
    {
        var r = (byte)(color.R * 0.625 + color.G * 0.375);
        var g = (byte)(color.R * 0.7 + color.G * 0.3);
        var b = color.B;
        
        return Windows.UI.Color.FromArgb(color.A, r, g, b);
    }

    private Windows.UI.Color TransformForTritanopia(Windows.UI.Color color)
    {
        var r = color.R;
        var g = (byte)(color.G * 0.95 + color.B * 0.05);
        var b = (byte)(color.G * 0.433 + color.B * 0.567);
        
        return Windows.UI.Color.FromArgb(color.A, r, g, b);
    }
}
