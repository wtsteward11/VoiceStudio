using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using VoiceStudio.Core.Gateways;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Controls
{
  /// <summary>
  /// Maps engine parameter schemas to WinUI controls for dynamic UI generation.
  /// Supports capability-driven UI rendering based on engine manifests.
  /// </summary>
  public static class SchemaToControlMapper
  {
    /// <summary>
    /// Creates a control for the given parameter definition.
    /// </summary>
    /// <param name="parameter">The parameter definition.</param>
    /// <param name="currentValue">Optional current value.</param>
    /// <param name="valueChanged">Optional callback when value changes.</param>
    /// <returns>The created control.</returns>
    public static FrameworkElement CreateControl(
        ParameterDefinition parameter,
        object? currentValue = null,
        Action<string, object?>? valueChanged = null)
    {
      return parameter.Type switch
      {
        ParameterType.String => CreateTextBox(parameter, currentValue, valueChanged),
        ParameterType.Integer => CreateNumberBox(parameter, currentValue, valueChanged, isInteger: true),
        ParameterType.Number => CreateNumberBox(parameter, currentValue, valueChanged, isInteger: false),
        ParameterType.Boolean => CreateToggleSwitch(parameter, currentValue, valueChanged),
        ParameterType.Enum => CreateComboBox(parameter, currentValue, valueChanged),
        ParameterType.FilePath => CreateFilePathPicker(parameter, currentValue, valueChanged),
        ParameterType.Array => CreateArrayControl(parameter, currentValue, valueChanged),
        ParameterType.Object => CreateObjectControl(parameter, currentValue, valueChanged),
        _ => CreateTextBox(parameter, currentValue, valueChanged)
      };
    }

    /// <summary>
    /// Creates a labeled control with description tooltip.
    /// </summary>
    public static FrameworkElement CreateLabeledControl(
        ParameterDefinition parameter,
        object? currentValue = null,
        Action<string, object?>? valueChanged = null)
    {
      var control = CreateControl(parameter, currentValue, valueChanged);

      var panel = new StackPanel
      {
        Spacing = 4,
        Margin = new Thickness(0, 0, 0, 12)
      };

      // Label
      var label = new TextBlock
      {
        Text = parameter.DisplayName,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
      };

      // Required indicator
      if (parameter.IsRequired)
      {
        var requiredLabel = new StackPanel { Orientation = Orientation.Horizontal };
        requiredLabel.Children.Add(label);
        requiredLabel.Children.Add(new TextBlock
        {
          Text = " *",
          Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red)
        });
        panel.Children.Add(requiredLabel);
      }
      else
      {
        panel.Children.Add(label);
      }

      // Description
      if (!string.IsNullOrEmpty(parameter.Description))
      {
        var description = new TextBlock
        {
          Text = parameter.Description,
          Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
          Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
          TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(description);
      }

      panel.Children.Add(control);

      return panel;
    }

    private static TextBox CreateTextBox(
        ParameterDefinition parameter,
        object? currentValue,
        Action<string, object?>? valueChanged)
    {
      var textBox = new TextBox
      {
        PlaceholderText = $"Enter {parameter.DisplayName.ToLowerInvariant()}",
        Text = GetStringValue(currentValue, parameter.DefaultValue),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      if (valueChanged != null)
      {
        textBox.TextChanged += (s, e) =>
        {
          valueChanged(parameter.Name, textBox.Text);
        };
      }

      return textBox;
    }

    private static NumberBox CreateNumberBox(
        ParameterDefinition parameter,
        object? currentValue,
        Action<string, object?>? valueChanged,
        bool isInteger)
    {
      var numberBox = new NumberBox
      {
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      // Set min/max if available
      if (parameter.MinValue.HasValue)
      {
        var minVal = parameter.MinValue.Value;
        if (minVal.ValueKind == JsonValueKind.Number)
        {
          numberBox.Minimum = minVal.GetDouble();
        }
      }

      if (parameter.MaxValue.HasValue)
      {
        var maxVal = parameter.MaxValue.Value;
        if (maxVal.ValueKind == JsonValueKind.Number)
        {
          numberBox.Maximum = maxVal.GetDouble();
        }
      }

      // Set step
      if (parameter.Step.HasValue)
      {
        numberBox.SmallChange = parameter.Step.Value;
        numberBox.LargeChange = parameter.Step.Value * 10;
      }
      else
      {
        numberBox.SmallChange = isInteger ? 1 : 0.1;
      }

      // Set current value
      numberBox.Value = GetDoubleValue(currentValue, parameter.DefaultValue);

      if (valueChanged != null)
      {
        numberBox.ValueChanged += (s, e) =>
        {
          var value = isInteger ? (object)(int)numberBox.Value : numberBox.Value;
          valueChanged(parameter.Name, value);
        };
      }

      return numberBox;
    }

    private static ToggleSwitch CreateToggleSwitch(
        ParameterDefinition parameter,
        object? currentValue,
        Action<string, object?>? valueChanged)
    {
      var toggle = new ToggleSwitch
      {
        Header = parameter.DisplayName,
        OnContent = "On",
        OffContent = "Off",
        IsOn = GetBoolValue(currentValue, parameter.DefaultValue)
      };

      if (valueChanged != null)
      {
        toggle.Toggled += (s, e) =>
        {
          valueChanged(parameter.Name, toggle.IsOn);
        };
      }

      return toggle;
    }

    private static ComboBox CreateComboBox(
        ParameterDefinition parameter,
        object? currentValue,
        Action<string, object?>? valueChanged)
    {
      var comboBox = new ComboBox
      {
        PlaceholderText = $"Select {parameter.DisplayName.ToLowerInvariant()}",
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      if (parameter.EnumOptions != null)
      {
        foreach (var option in parameter.EnumOptions)
        {
          var item = new ComboBoxItem
          {
            Content = option.DisplayName,
            Tag = option.Value
          };

          if (!string.IsNullOrEmpty(option.Description))
          {
            ToolTipService.SetToolTip(item, option.Description);
          }

          comboBox.Items.Add(item);
        }
      }

      // Set current selection
      var currentString = GetStringValue(currentValue, parameter.DefaultValue);
      if (!string.IsNullOrEmpty(currentString))
      {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
          if (comboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == currentString)
          {
            comboBox.SelectedIndex = i;
            break;
          }
        }
      }

      if (valueChanged != null)
      {
        comboBox.SelectionChanged += (s, e) =>
        {
          if (comboBox.SelectedItem is ComboBoxItem selected)
          {
            valueChanged(parameter.Name, selected.Tag);
          }
        };
      }

      return comboBox;
    }

    private static FrameworkElement CreateFilePathPicker(
        ParameterDefinition parameter,
        object? currentValue,
        Action<string, object?>? valueChanged)
    {
      var panel = new Grid
      {
        ColumnSpacing = 8
      };
      panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

      var textBox = new TextBox
      {
        PlaceholderText = "Select a file...",
        Text = GetStringValue(currentValue, parameter.DefaultValue),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };
      Grid.SetColumn(textBox, 0);

      var browseButton = new Button
      {
        Content = "Browse..."
      };
      Grid.SetColumn(browseButton, 1);

      browseButton.Click += async (s, e) =>
      {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");

        // Get the window handle
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
          textBox.Text = file.Path;
          valueChanged?.Invoke(parameter.Name, file.Path);
        }
      };

      if (valueChanged != null)
      {
        textBox.TextChanged += (s, e) =>
        {
          valueChanged(parameter.Name, textBox.Text);
        };
      }

      panel.Children.Add(textBox);
      panel.Children.Add(browseButton);

      return panel;
    }

    private static FrameworkElement CreateArrayControl(
        ParameterDefinition parameter,
        object? currentValue,
        Action<string, object?>? valueChanged)
    {
      // For arrays, create a simple text area with JSON editing
      var textBox = new TextBox
      {
        PlaceholderText = "Enter JSON array (e.g., [1, 2, 3])",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 60,
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      if (currentValue != null)
      {
        textBox.Text = JsonSerializer.Serialize(currentValue);
      }
      else if (parameter.DefaultValue.HasValue)
      {
        textBox.Text = parameter.DefaultValue.Value.GetRawText();
      }

      if (valueChanged != null)
      {
        textBox.TextChanged += (s, e) =>
        {
          try
          {
            var parsed = JsonSerializer.Deserialize<object[]>(textBox.Text);
            valueChanged(parameter.Name, parsed);
          }
          // ALLOWED: empty catch - invalid JSON is expected during typing
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SchemaToControlMapper.Unknown");
      }
        };
      }

      return textBox;
    }

    private static FrameworkElement CreateObjectControl(
        ParameterDefinition parameter,
        object? currentValue,
        Action<string, object?>? valueChanged)
    {
      // For objects, create an expander with JSON editing
      var expander = new Expander
      {
        Header = parameter.DisplayName,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        IsExpanded = false
      };

      var textBox = new TextBox
      {
        PlaceholderText = "Enter JSON object (e.g., {\"key\": \"value\"})",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 100,
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      if (currentValue != null)
      {
        textBox.Text = JsonSerializer.Serialize(currentValue, new JsonSerializerOptions { WriteIndented = true });
      }
      else if (parameter.DefaultValue.HasValue)
      {
        textBox.Text = parameter.DefaultValue.Value.GetRawText();
      }

      if (valueChanged != null)
      {
        textBox.TextChanged += (s, e) =>
        {
          try
          {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(textBox.Text);
            valueChanged(parameter.Name, parsed);
          }
          // ALLOWED: empty catch - invalid JSON is expected during typing
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SchemaToControlMapper.Unknown");
      }
        };
      }

      expander.Content = textBox;
      return expander;
    }

    #region Value Helpers

    private static string GetStringValue(object? value, JsonElement? defaultValue)
    {
      if (value is string s) return s;
      if (value != null) return value.ToString() ?? "";
      if (defaultValue.HasValue && defaultValue.Value.ValueKind == JsonValueKind.String)
        return defaultValue.Value.GetString() ?? "";
      return "";
    }

    private static double GetDoubleValue(object? value, JsonElement? defaultValue)
    {
      if (value is double d) return d;
      if (value is int i) return i;
      if (value is float f) return f;
      if (value != null && double.TryParse(value.ToString(), out var parsed)) return parsed;
      if (defaultValue.HasValue && defaultValue.Value.ValueKind == JsonValueKind.Number)
        return defaultValue.Value.GetDouble();
      return 0;
    }

    private static bool GetBoolValue(object? value, JsonElement? defaultValue)
    {
      if (value is bool b) return b;
      if (value != null && bool.TryParse(value.ToString(), out var parsed)) return parsed;
      if (defaultValue.HasValue && (defaultValue.Value.ValueKind == JsonValueKind.True || defaultValue.Value.ValueKind == JsonValueKind.False))
        return defaultValue.Value.GetBoolean();
      return false;
    }

    #endregion
  }
}
