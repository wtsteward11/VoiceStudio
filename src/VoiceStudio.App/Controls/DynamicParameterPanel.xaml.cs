using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.Core.Gateways;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Controls
{
  /// <summary>
  /// Dynamic parameter panel that renders controls based on an engine's config schema.
  /// Implements capability-driven UI for engine configuration.
  /// </summary>
  public sealed partial class DynamicParameterPanel : UserControl
  {
    private EngineParameterSchema? _schema;
    private Dictionary<string, object?> _values = new();
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Event raised when a parameter value changes.
    /// </summary>
    public event EventHandler<ParameterChangedEventArgs>? ParameterChanged;

    #region Dependency Properties

    public static readonly DependencyProperty EngineIdProperty =
        DependencyProperty.Register(
            nameof(EngineId),
            typeof(string),
            typeof(DynamicParameterPanel),
            new PropertyMetadata(null, OnEngineIdChanged));

    public static readonly DependencyProperty SchemaProperty =
        DependencyProperty.Register(
            nameof(Schema),
            typeof(EngineParameterSchema),
            typeof(DynamicParameterPanel),
            new PropertyMetadata(null, OnSchemaChanged));

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(Dictionary<string, object?>),
            typeof(DynamicParameterPanel),
            new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty ShowAdvancedProperty =
        DependencyProperty.Register(
            nameof(ShowAdvanced),
            typeof(bool),
            typeof(DynamicParameterPanel),
            new PropertyMetadata(false, OnShowAdvancedChanged));

    public static readonly DependencyProperty EngineGatewayProperty =
        DependencyProperty.Register(
            nameof(EngineGateway),
            typeof(IEngineGateway),
            typeof(DynamicParameterPanel),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the engine ID to load schema for.
    /// </summary>
    public string? EngineId
    {
      get => (string?)GetValue(EngineIdProperty);
      set => SetValue(EngineIdProperty, value);
    }

    /// <summary>
    /// Gets or sets the schema to render.
    /// </summary>
    public EngineParameterSchema? Schema
    {
      get => (EngineParameterSchema?)GetValue(SchemaProperty);
      set => SetValue(SchemaProperty, value);
    }

    /// <summary>
    /// Gets or sets the current parameter values.
    /// </summary>
    public Dictionary<string, object?>? Values
    {
      get => (Dictionary<string, object?>?)GetValue(ValuesProperty);
      set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show advanced parameters.
    /// </summary>
    public bool ShowAdvanced
    {
      get => (bool)GetValue(ShowAdvancedProperty);
      set => SetValue(ShowAdvancedProperty, value);
    }

    /// <summary>
    /// Gets or sets the engine gateway for loading schemas.
    /// </summary>
    public IEngineGateway? EngineGateway
    {
      get => (IEngineGateway?)GetValue(EngineGatewayProperty);
      set => SetValue(EngineGatewayProperty, value);
    }

    #endregion

    public DynamicParameterPanel()
    {
      this.InitializeComponent();
    }

    private static void OnEngineIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is DynamicParameterPanel panel && e.NewValue is string engineId && !string.IsNullOrEmpty(engineId))
      {
        _ = panel.LoadSchemaAsync(engineId);
      }
    }

    private static void OnSchemaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is DynamicParameterPanel panel)
      {
        panel._schema = e.NewValue as EngineParameterSchema;
        panel.RenderParameters();
      }
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is DynamicParameterPanel panel)
      {
        panel._values = e.NewValue as Dictionary<string, object?> ?? new();
        panel.RenderParameters();
      }
    }

    private static void OnShowAdvancedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is DynamicParameterPanel panel)
      {
        panel.RenderParameters();
      }
    }

    /// <summary>
    /// Loads the schema for the specified engine.
    /// </summary>
    public async Task LoadSchemaAsync(string engineId)
    {
      _loadCts?.Cancel();
      _loadCts = new CancellationTokenSource();

      SetState(PanelState.Loading);

      try
      {
        if (EngineGateway == null)
        {
          SetState(PanelState.Error, "Engine gateway not configured");
          return;
        }

        var result = await EngineGateway.GetSchemaAsync(engineId, _loadCts.Token);

        if (!result.Success)
        {
          SetState(PanelState.Error, result.Error?.Message ?? "Failed to load schema");
          return;
        }

        _schema = result.Data;
        Schema = result.Data;

        RenderParameters();
      }
      catch (OperationCanceledException)
      {
        System.Diagnostics.Debug.WriteLine("DynamicParameterPanel: LoadSchemaAsync cancelled");
      }
      catch (Exception ex)
      {
        SetState(PanelState.Error, ex.Message);
      }
    }

    /// <summary>
    /// Gets the current parameter values.
    /// </summary>
    public Dictionary<string, object?> GetValues()
    {
      return new Dictionary<string, object?>(_values);
    }

    /// <summary>
    /// Sets parameter values.
    /// </summary>
    public void SetValues(Dictionary<string, object?> values)
    {
      _values = new Dictionary<string, object?>(values);
      Values = _values;
      RenderParameters();
    }

    /// <summary>
    /// Resets all parameters to their default values.
    /// </summary>
    public void ResetToDefaults()
    {
      _values.Clear();
      Values = _values;
      RenderParameters();

      ParameterChanged?.Invoke(this, new ParameterChangedEventArgs("*", null, isReset: true));
    }

    private void RenderParameters()
    {
      ParametersContainer.Children.Clear();

      if (_schema == null || _schema.Parameters.Count == 0)
      {
        SetState(PanelState.Empty);
        return;
      }

      SetState(PanelState.Content);

      // Group parameters
      var groups = _schema.Groups?.ToDictionary(g => g.Id) ?? new();
      var paramsByGroup = new Dictionary<string, List<ParameterDefinition>>();
      var ungroupedParams = new List<ParameterDefinition>();

      foreach (var param in _schema.Parameters.OrderBy(p => p.Order))
      {
        // Skip advanced if not showing
        if (param.IsAdvanced && !ShowAdvanced)
          continue;

        if (!string.IsNullOrEmpty(param.GroupId))
        {
          if (!paramsByGroup.ContainsKey(param.GroupId))
            paramsByGroup[param.GroupId] = new List<ParameterDefinition>();
          paramsByGroup[param.GroupId].Add(param);
        }
        else
        {
          ungroupedParams.Add(param);
        }
      }

      // Render ungrouped parameters first
      foreach (var param in ungroupedParams)
      {
        var control = SchemaToControlMapper.CreateLabeledControl(
            param,
            _values.GetValueOrDefault(param.Name),
            OnParameterValueChanged);
        ParametersContainer.Children.Add(control);
      }

      // Render grouped parameters in expanders
      foreach (var (groupId, @params) in paramsByGroup.OrderBy(kvp => groups.GetValueOrDefault(kvp.Key)?.Order ?? 999))
      {
        var group = groups.GetValueOrDefault(groupId);
        var expander = new Expander
        {
          Header = group?.DisplayName ?? groupId,
          HorizontalAlignment = HorizontalAlignment.Stretch,
          IsExpanded = !(group?.IsCollapsed ?? false),
          Margin = new Thickness(0, 8, 0, 8)
        };

        if (!string.IsNullOrEmpty(group?.Description))
        {
          ToolTipService.SetToolTip(expander, group.Description);
        }

        var groupPanel = new StackPanel { Spacing = 8, Padding = new Thickness(0, 8, 0, 0) };

        foreach (var param in @params)
        {
          var control = SchemaToControlMapper.CreateLabeledControl(
              param,
              _values.GetValueOrDefault(param.Name),
              OnParameterValueChanged);
          groupPanel.Children.Add(control);
        }

        expander.Content = groupPanel;
        ParametersContainer.Children.Add(expander);
      }

      // Add "Show Advanced" toggle if there are advanced parameters
      if (_schema.Parameters.Any(p => p.IsAdvanced) && !ShowAdvanced)
      {
        var advancedToggle = new CheckBox
        {
          Content = "Show advanced parameters",
          IsChecked = ShowAdvanced,
          Margin = new Thickness(0, 12, 0, 0)
        };
        advancedToggle.Checked += (s, e) => ShowAdvanced = true;
        advancedToggle.Unchecked += (s, e) => ShowAdvanced = false;
        ParametersContainer.Children.Add(advancedToggle);
      }
    }

    private void OnParameterValueChanged(string name, object? value)
    {
      _values[name] = value;
      ParameterChanged?.Invoke(this, new ParameterChangedEventArgs(name, value));
    }

    private void SetState(PanelState state, string? message = null)
    {
      LoadingPanel.Visibility = state == PanelState.Loading ? Visibility.Visible : Visibility.Collapsed;
      ErrorPanel.Visibility = state == PanelState.Error ? Visibility.Visible : Visibility.Collapsed;
      EmptyPanel.Visibility = state == PanelState.Empty ? Visibility.Visible : Visibility.Collapsed;
      ContentScrollViewer.Visibility = state == PanelState.Content ? Visibility.Visible : Visibility.Collapsed;

      if (state == PanelState.Error && message != null)
      {
        ErrorText.Text = message;
      }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
      if (!string.IsNullOrEmpty(EngineId))
      {
        _ = LoadSchemaAsync(EngineId);
      }
    }

    private enum PanelState
    {
      Loading,
      Error,
      Empty,
      Content
    }
  }

  /// <summary>
  /// Event arguments for parameter changes.
  /// </summary>
  public sealed class ParameterChangedEventArgs : EventArgs
  {
    /// <summary>
    /// Gets the name of the changed parameter.
    /// </summary>
    public string ParameterName { get; }

    /// <summary>
    /// Gets the new value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets whether this is a reset-to-defaults operation.
    /// </summary>
    public bool IsReset { get; }

    public ParameterChangedEventArgs(string parameterName, object? value, bool isReset = false)
    {
      ParameterName = parameterName;
      Value = value;
      IsReset = isReset;
    }
  }
}
