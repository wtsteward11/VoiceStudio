using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels;

/// <summary>
/// Theme Editor panel for customizing application appearance.
/// </summary>
public sealed partial class ThemeEditorView : UserControl
{
    private IUnifiedThemeService? _themeService;
    private bool _isInitializing = true;
    private readonly string _customThemesPath;

    public ThemeEditorView()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        
        // Setup custom themes storage path
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _customThemesPath = Path.Combine(appData, "VoiceStudio", "themes");
        Directory.CreateDirectory(_customThemesPath);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _themeService = AppServices.TryGetThemeService();
        InitializeControls();
        LoadSavedThemesList();
        _isInitializing = false;
    }

    private void InitializeControls()
    {
        if (_themeService == null) return;

        // Set current theme in ComboBox
        var currentTheme = _themeService.CurrentThemeName;
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag?.ToString() == currentTheme)
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }

        // Set accent colors
        AccentColorGrid.ItemsSource = _themeService.GetPredefinedAccents();
        var currentAccent = _themeService.CurrentAccent;
        var accents = _themeService.GetPredefinedAccents();
        for (int i = 0; i < accents.Count; i++)
        {
            if (accents[i].Name == currentAccent.Name)
            {
                AccentColorGrid.SelectedIndex = i;
                break;
            }
        }

        // Set current density in ComboBox
        var currentDensity = _themeService.CurrentDensity.ToString();
        foreach (ComboBoxItem item in DensityComboBox.Items)
        {
            if (item.Tag?.ToString() == currentDensity)
            {
                DensityComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || _themeService == null) return;

        if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var themeName = selectedItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(themeName))
            {
                _themeService.ApplyTheme(themeName);
            }
        }
    }

    private void AccentColorGrid_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || _themeService == null) return;

        if (AccentColorGrid.SelectedItem is ThemeAccent accent)
        {
            _themeService.SetAccent(accent);
        }
    }

    private void DensityComboBox_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || _themeService == null) return;

        if (DensityComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var densityName = selectedItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(densityName) && 
                System.Enum.TryParse<LayoutDensity>(densityName, out var density))
            {
                _themeService.SetDensity(density);
            }
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_themeService == null) return;

        _isInitializing = true;
        
        // Reset to defaults
        _themeService.ApplyTheme("SciFi");
        _themeService.SetDensity(LayoutDensity.Compact);
        var accents = _themeService.GetPredefinedAccents();
        if (accents.Count > 0)
        {
            _themeService.SetAccent(accents[0]); // Blue
        }

        // Update UI
        InitializeControls();
        
        _isInitializing = false;
    }

    private void LoadSavedThemesList()
    {
        try
        {
            SavedThemesComboBox.Items.Clear();
            var themeFiles = Directory.GetFiles(_customThemesPath, "*.json");
            foreach (var file in themeFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                SavedThemesComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = file });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Failed to load saved themes: {ex.Message}");
        }
    }

    private void SaveThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_themeService == null) return;

        var themeName = CustomThemeNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(themeName))
        {
            // Show error or use default name
            themeName = $"Custom_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        try
        {
            var themeData = new Dictionary<string, string>
            {
                ["theme"] = _themeService.CurrentThemeName,
                ["density"] = _themeService.CurrentDensity.ToString(),
                ["accentName"] = _themeService.CurrentAccent.Name
            };

            var json = JsonSerializer.Serialize(themeData, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(_customThemesPath, $"{themeName}.json");
            File.WriteAllText(filePath, json);

            CustomThemeNameBox.Text = string.Empty;
            LoadSavedThemesList();

            System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Saved custom theme: {themeName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Failed to save theme: {ex.Message}");
        }
    }

    private void LoadThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_themeService == null || SavedThemesComboBox.SelectedItem is not ComboBoxItem selectedItem)
            return;

        var filePath = selectedItem.Tag?.ToString();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var json = File.ReadAllText(filePath);
            var themeData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (themeData != null)
            {
                _isInitializing = true;

                if (themeData.TryGetValue("theme", out var themeName))
                    _themeService.ApplyTheme(themeName);

                if (themeData.TryGetValue("density", out var densityStr) &&
                    Enum.TryParse<LayoutDensity>(densityStr, out var density))
                    _themeService.SetDensity(density);

                if (themeData.TryGetValue("accentName", out var accentName))
                {
                    var accents = _themeService.GetPredefinedAccents();
                    var accent = accents.FirstOrDefault(a => a.Name == accentName);
                    if (accent != null)
                        _themeService.SetAccent(accent);
                }

                InitializeControls();
                _isInitializing = false;

                System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Loaded custom theme from: {filePath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Failed to load theme: {ex.Message}");
        }
    }

    private void SavedThemesComboBox_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        // Could add preview on hover in future
    }

    private void CustomColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        // Update the preview border with the selected color
        if (CustomColorPreview != null)
        {
            CustomColorPreview.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(args.NewColor);
        }
    }

    private void ApplyCustomColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_themeService == null || CustomColorPicker == null) return;

        var color = CustomColorPicker.Color;
        var customAccent = new ThemeAccent
        {
            Name = "Custom",
            Primary = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B)
        };

        _themeService.SetAccent(customAccent);
        System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Applied custom accent color: #{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private void DeleteThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedThemesComboBox.SelectedItem is not ComboBoxItem selectedItem)
            return;

        var filePath = selectedItem.Tag?.ToString();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            File.Delete(filePath);
            LoadSavedThemesList();
            System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Deleted custom theme: {filePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Failed to delete theme: {ex.Message}");
        }
    }
}
