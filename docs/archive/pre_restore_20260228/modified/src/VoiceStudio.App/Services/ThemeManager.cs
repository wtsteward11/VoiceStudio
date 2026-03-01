using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Unified theme manager implementing IUnifiedThemeService.
  /// Phase 5.0: Service unification - combines ThemeManager and Features/Theming/ThemeService.
  /// </summary>
  public sealed class ThemeManager : IUnifiedThemeService
  {
    private FrameworkElement? _rootElement;
    private AppTheme _currentTheme = AppTheme.Dark;
    private LayoutDensity _currentDensity = LayoutDensity.Compact;
    private ThemeAccent _currentAccent;

    private static readonly List<string> _availableThemes = new()
    {
      "SciFi", "Default", "Dark", "Light", "HighContrast"
    };

    private static readonly List<ThemeAccent> _predefinedAccents = new()
    {
      new() { Name = "Blue", Primary = Windows.UI.Color.FromArgb(255, 0, 120, 212) },
      new() { Name = "Purple", Primary = Windows.UI.Color.FromArgb(255, 128, 57, 123) },
      new() { Name = "Pink", Primary = Windows.UI.Color.FromArgb(255, 231, 72, 86) },
      new() { Name = "Red", Primary = Windows.UI.Color.FromArgb(255, 232, 17, 35) },
      new() { Name = "Orange", Primary = Windows.UI.Color.FromArgb(255, 247, 99, 12) },
      new() { Name = "Yellow", Primary = Windows.UI.Color.FromArgb(255, 255, 185, 0) },
      new() { Name = "Green", Primary = Windows.UI.Color.FromArgb(255, 16, 137, 62) },
      new() { Name = "Teal", Primary = Windows.UI.Color.FromArgb(255, 0, 178, 148) },
    };

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public ThemeManager()
    {
      _currentAccent = _predefinedAccents[0]; // Default to blue
    }

    // Legacy properties
    public string CurrentThemeName { get; private set; } = "SciFi";
    public string Density { get; private set; } = "Compact";

    // IUnifiedThemeService properties
    public AppTheme CurrentTheme => _currentTheme;
    public LayoutDensity CurrentDensity => _currentDensity;
    public ThemeAccent CurrentAccent => _currentAccent;
    public bool IsDarkMode => CurrentThemeName == "SciFi" || CurrentThemeName == "Dark" || _currentTheme == AppTheme.Dark;
    public ElementTheme EffectiveTheme => IsDarkMode ? ElementTheme.Dark : ElementTheme.Light;

    /// <summary>
    /// Initialize the theme service.
    /// </summary>
    public async Task InitializeAsync(FrameworkElement rootElement)
    {
      _rootElement = rootElement;
      await LoadPersistedSettings();
      ApplyTheme(CurrentThemeName);
      
      // Apply persisted accent color
      if (_currentAccent != null)
      {
        ApplyAccentToResources(_currentAccent);
      }
    }
    
    /// <summary>
    /// Applies accent color to application resources.
    /// </summary>
    private void ApplyAccentToResources(ThemeAccent accent)
    {
      // Guard: Skip resource manipulation in test context
      if (Application.Current?.Resources == null)
      {
        return;
      }

      if (Application.Current.Resources.ContainsKey("SystemAccentColor"))
      {
        Application.Current.Resources["SystemAccentColor"] = accent.Primary;
      }
      
      // Also set the accent brushes if they exist
      if (Application.Current.Resources.ContainsKey("VSQ.Accent.PrimaryBrush"))
      {
        Application.Current.Resources["VSQ.Accent.PrimaryBrush"] = 
            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(accent.Primary.A, accent.Primary.R, accent.Primary.G, accent.Primary.B));
      }
    }

    private async Task LoadPersistedSettings()
    {
      try
      {
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceStudio"
        );
        var path = Path.Combine(settingsDir, "settings.json");
        
        if (File.Exists(path))
        {
          var json = await File.ReadAllTextAsync(path);
          using var doc = JsonDocument.Parse(json);
          
          if (doc.RootElement.TryGetProperty("theme", out var themeProp))
            CurrentThemeName = themeProp.GetString() ?? "SciFi";
          
          if (doc.RootElement.TryGetProperty("density", out var densityProp))
            Density = densityProp.GetString() ?? "Compact";
          
          // Load accent color by name
          if (doc.RootElement.TryGetProperty("accent", out var accentProp))
          {
            var accentName = accentProp.GetString();
            if (!string.IsNullOrEmpty(accentName))
            {
              var accent = _predefinedAccents.Find(a => a.Name == accentName);
              if (accent != null)
              {
                _currentAccent = accent;
              }
            }
          }
        }
      }
      // ALLOWED: empty catch - using defaults is acceptable fallback
      catch
      {
      }
    }

    public void ApplyTheme(string name)
    {
      CurrentThemeName = name;
      
      // Update enum based on theme name
      _currentTheme = name.ToLowerInvariant() switch
      {
        "light" => AppTheme.Light,
        "dark" or "scifi" => AppTheme.Dark,
        "highcontrast" => AppTheme.HighContrast,
        _ => AppTheme.Dark
      };

      // Guard: Skip resource dictionary manipulation in test context
      if (Application.Current?.Resources?.MergedDictionaries == null)
      {
        Persist();
        RaiseThemeChanged();
        return;
      }

      // Remove existing theme dictionaries
      var toRemove = new System.Collections.Generic.List<ResourceDictionary>();
      foreach (var dict in Application.Current.Resources.MergedDictionaries)
      {
        if (dict.Source?.ToString().Contains("Theme.") == true)
        {
          toRemove.Add(dict);
        }
      }
      foreach (var dict in toRemove)
      {
        Application.Current.Resources.MergedDictionaries.Remove(dict);
      }

      // Add new theme
      var themeDict = new ResourceDictionary
      {
        Source = new Uri($"ms-appx:///Resources/Theme.{name}.xaml")
      };
      Application.Current.Resources.MergedDictionaries.Add(themeDict);

      Persist();
      RaiseThemeChanged();
    }

    public void ApplyLayoutDensity(string density)
    {
      Density = density;

      // Guard: Skip resource manipulation in test context
      if (Application.Current?.Resources?.MergedDictionaries == null)
      {
        RaiseThemeChanged();
        return;
      }

      // Remove existing density dictionaries
      var toRemove = new System.Collections.Generic.List<ResourceDictionary>();
      foreach (var dict in Application.Current.Resources.MergedDictionaries)
      {
        if (dict.Source?.ToString().Contains("Density.") == true)
        {
          toRemove.Add(dict);
        }
      }
      foreach (var dict in toRemove)
      {
        Application.Current.Resources.MergedDictionaries.Remove(dict);
      }

      // Add new density
      var densityDict = new ResourceDictionary
      {
        Source = new Uri($"ms-appx:///Resources/Density.{density}.xaml")
      };
      Application.Current.Resources.MergedDictionaries.Add(densityDict);

      Persist();
    }

    private void Persist()
    {
      var settingsDir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
          "VoiceStudio"
      );
      Directory.CreateDirectory(settingsDir);
      var path = Path.Combine(settingsDir, "settings.json");
      File.WriteAllText(path, JsonSerializer.Serialize(new
      {
        theme = CurrentThemeName,
        density = Density,
        accent = _currentAccent?.Name ?? "Blue"
      }));
    }

    #region IUnifiedThemeService Implementation

    /// <summary>
    /// Sets the application theme by enum.
    /// </summary>
    public void SetTheme(AppTheme theme)
    {
      _currentTheme = theme;
      
      var themeName = theme switch
      {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        AppTheme.HighContrast => "HighContrast",
        AppTheme.System => GetSystemTheme(),
        _ => "SciFi"
      };
      
      ApplyTheme(themeName);
    }

    /// <summary>
    /// Toggles between light and dark mode.
    /// </summary>
    public void ToggleDarkMode()
    {
      var newTheme = IsDarkMode ? AppTheme.Light : AppTheme.Dark;
      SetTheme(newTheme);
    }

    /// <summary>
    /// Sets the layout density.
    /// </summary>
    public void SetDensity(LayoutDensity density)
    {
      _currentDensity = density;
      var densityName = density switch
      {
        LayoutDensity.Compact => "Compact",
        LayoutDensity.Normal => "Normal",
        LayoutDensity.Comfortable => "Comfortable",
        LayoutDensity.Touch => "Touch",
        _ => "Compact"
      };
      ApplyLayoutDensity(densityName);
    }

    /// <summary>
    /// Sets the accent color.
    /// </summary>
    public void SetAccent(ThemeAccent accent)
    {
      _currentAccent = accent;
      ApplyAccentToResources(accent);
      Persist();
      RaiseThemeChanged();
    }

    /// <summary>
    /// Gets available theme names.
    /// </summary>
    public IReadOnlyList<string> GetAvailableThemes() => _availableThemes;

    /// <summary>
    /// Gets predefined accent colors.
    /// </summary>
    public IReadOnlyList<ThemeAccent> GetPredefinedAccents() => _predefinedAccents;

    private string GetSystemTheme()
    {
      try
      {
        var uiSettings = new Windows.UI.ViewManagement.UISettings();
        var foreground = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
        return foreground.R > 128 ? "Dark" : "Light";
      }
      catch
      {
        return "Dark";
      }
    }

    private void RaiseThemeChanged()
    {
      ThemeChanged?.Invoke(this, new ThemeChangedEventArgs
      {
        Theme = _currentTheme,
        ThemeName = CurrentThemeName,
        EffectiveTheme = EffectiveTheme
      });
    }

    #endregion
  }
}