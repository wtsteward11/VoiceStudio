using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

// Type aliases to resolve ambiguity with local types in VoiceStudio.App.Services namespace
using Macro = VoiceStudio.Core.Models.Macro;
using AutomationCurve = VoiceStudio.Core.Models.AutomationCurve;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class MacroView : UserControl
  {
    public MacroViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private IErrorLoggingService? _errorLoggingService;

    public MacroView()
    {
      this.InitializeComponent();
      // Wire DataContext with BackendClient
      ViewModel = new MacroViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _errorLoggingService = ServiceProvider.TryGetErrorLoggingService();

      // Setup keyboard navigation
      this.Loaded += MacroView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Hide();
        }
      });
    }

    private void MacroView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);

      // Configure virtualization for ListViews to optimize large macro lists
      Controls.VirtualizedListHelper.ConfigureListView(MacrosListView);
      Controls.VirtualizedListHelper.ConfigureListView(AutomationCurvesListView);
    }

    private async void NewMacroButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      try
      {
        var textBox = new TextBox
        {
          PlaceholderText = "Macro name",
          Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };

        var dialog = new ContentDialog
        {
          Title = "Create New Macro",
          PrimaryButtonText = "Create",
          SecondaryButtonText = "Cancel",
          DefaultButton = ContentDialogButton.Primary,
          Content = textBox,
          XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
          await ViewModel.CreateMacroCommand.ExecuteAsync(textBox.Text);
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "NewMacroButton_Click");
      }
    }

    private void AutomationToggleButton_Checked(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      ViewModel.ShowMacrosView = false;
    }

    private void AutomationToggleButton_Unchecked(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      ViewModel.ShowMacrosView = true;
    }

    private void EditMacroButton_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is Macro macro)
      {
        ViewModel.SelectedMacro = macro;
      }
    }

    private async void CreateCurveButton_Click(object _, RoutedEventArgs __)
    {
      try
      {
        // Get selected parameter from ComboBox
        var parameterCombo = this.FindName("ParameterCombo") as ComboBox;
        var selectedItem = parameterCombo?.SelectedItem as ComboBoxItem;
        var parameterId = selectedItem?.Tag?.ToString() ?? "volume";

        await ViewModel.CreateAutomationCurveCommand.ExecuteAsync(parameterId);

        // Refresh the curve editor to show the new curve
        if (CurveEditor != null)
        {
          _ = CurveEditor.LoadCurvesAsync();
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "CreateCurveButton_Click");
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Macros & Automation Help";
      HelpOverlay.HelpText = "The Macros & Automation panel allows you to create and manage automation macros and curves. Macros are sequences of actions that can be executed to automate tasks. Automation curves let you control parameter changes over time, such as volume fades, pan sweeps, or effect parameter automation.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Execute selected macro" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new macro" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected macro or curve" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Macros can automate complex sequences of operations");
      HelpOverlay.Tips.Add("Automation curves allow smooth parameter transitions over time");
      HelpOverlay.Tips.Add("Curves can be edited by dragging control points");
      HelpOverlay.Tips.Add("Use automation to create dynamic audio effects");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Macro_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var macro = element.DataContext as Macro ?? listView.SelectedItem as Macro;
        if (macro != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var executeItem = new MenuFlyoutItem { Text = "Execute" };
            executeItem.Click += async (_, __) => await HandleMacroMenuClick("Execute", macro);
            menu.Items.Add(executeItem);

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, __) => await HandleMacroMenuClick("Edit", macro);
            menu.Items.Add(editItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleMacroMenuClick("Duplicate", macro);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, __) => await HandleMacroMenuClick("Export", macro);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleMacroMenuClick("Delete", macro);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void AutomationCurve_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var curve = element.DataContext as AutomationCurve ?? listView.SelectedItem as AutomationCurve;
        if (curve != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleAutomationCurveMenuClick("Duplicate", curve);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, __) => await HandleAutomationCurveMenuClick("Export", curve);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleAutomationCurveMenuClick("Delete", curve);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleMacroMenuClick(string action, Macro macro)
    {
      try
      {
        switch (action.ToLower())
        {
          case "execute":
            if (ViewModel.ExecuteMacroCommand.CanExecute(macro.Id))
            {
              await ViewModel.ExecuteMacroCommand.ExecuteAsync(macro.Id);
              _toastService?.ShowToast(ToastType.Success, "Macro Executed", $"Executing macro '{macro.Name}'");
            }
            break;
          case "edit":
            ViewModel.SelectedMacro = macro;
            _toastService?.ShowToast(ToastType.Info, "Edit Macro", $"Editing macro '{macro.Name}'");
            break;
          case "duplicate":
            await DuplicateMacroAsync(macro);
            break;
          case "export":
            await ExportMacroAsync(macro);
            break;
          case "delete":
            if (ViewModel.DeleteMacroCommand.CanExecute(macro.Id))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Macro",
                Content = $"Are you sure you want to delete macro '{macro.Name}'? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                var macroToDelete = macro;
                var macroIndex = ViewModel.Macros.IndexOf(macro);

                await ViewModel.DeleteMacroCommand.ExecuteAsync(macro.Id);

                // Undo/redo is handled by DeleteMacroCommand via DeleteMacroAction
                _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted macro '{macro.Name}'");
              }
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleAutomationCurveMenuClick(string action, AutomationCurve curve)
    {
      try
      {
        switch (action.ToLower())
        {
          case "duplicate":
            await DuplicateAutomationCurveAsync(curve);
            break;
          case "export":
            await ExportAutomationCurveAsync(curve);
            break;
          case "delete":
            if (ViewModel.DeleteAutomationCurveCommand.CanExecute(curve.Id))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Automation Curve",
                Content = $"Are you sure you want to delete automation curve '{curve.Name}'? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                var curveToDelete = curve;
                var curveIndex = ViewModel.AutomationCurves.IndexOf(curve);

                await ViewModel.DeleteAutomationCurveCommand.ExecuteAsync(curve.Id);

                // Undo/redo is handled by DeleteAutomationCurveCommand via DeleteAutomationCurveAction
                _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted automation curve '{curve.Name}'");
              }
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private System.Threading.Tasks.Task DuplicateMacroAsync(Macro macro)
    {
      try
      {
        var duplicatedMacro = new Macro
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{macro.Name} Copy",
          Description = macro.Description,
          ProjectId = macro.ProjectId,
          IsEnabled = macro.IsEnabled,
          Nodes = new System.Collections.Generic.List<MacroNode>(macro.Nodes.Select(n => new MacroNode
          {
            Id = Guid.NewGuid().ToString(),
            Type = n.Type,
            Name = n.Name,
            X = n.X,
            Y = n.Y,
            Properties = new System.Collections.Generic.Dictionary<string, object>(n.Properties),
            InputPorts = new System.Collections.Generic.List<MacroPort>(n.InputPorts.Select(p => new MacroPort
            {
              Id = Guid.NewGuid().ToString(),
              Name = p.Name,
              Type = p.Type,
              IsRequired = p.IsRequired
            })),
            OutputPorts = new System.Collections.Generic.List<MacroPort>(n.OutputPorts.Select(p => new MacroPort
            {
              Id = Guid.NewGuid().ToString(),
              Name = p.Name,
              Type = p.Type,
              IsRequired = p.IsRequired
            }))
          })),
          Connections = new System.Collections.Generic.List<MacroConnection>(macro.Connections.Select(c => new MacroConnection
          {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = c.SourceNodeId,
            SourcePortId = c.SourcePortId,
            TargetNodeId = c.TargetNodeId,
            TargetPortId = c.TargetPortId
          })),
          Created = System.DateTime.UtcNow,
          Modified = System.DateTime.UtcNow
        };

        ViewModel.Macros.Add(duplicatedMacro);

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Macro: {macro.Name}",
              () => ViewModel.Macros.Remove(duplicatedMacro),
              () => ViewModel.Macros.Add(duplicatedMacro));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated macro '{macro.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate macro: {ex.Message}");
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }

    private System.Threading.Tasks.Task DuplicateAutomationCurveAsync(AutomationCurve curve)
    {
      try
      {
        var duplicatedCurve = new AutomationCurve
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{curve.Name} Copy",
          ParameterId = curve.ParameterId,
          TrackId = curve.TrackId,
          Interpolation = curve.Interpolation,
          Points = new System.Collections.Generic.List<AutomationPoint>(curve.Points.Select(p => new AutomationPoint
          {
            Time = p.Time,
            Value = p.Value,
            BezierHandleInX = p.BezierHandleInX,
            BezierHandleInY = p.BezierHandleInY,
            BezierHandleOutX = p.BezierHandleOutX,
            BezierHandleOutY = p.BezierHandleOutY
          }))
        };

        ViewModel.AutomationCurves.Add(duplicatedCurve);

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Automation Curve: {curve.Name}",
              () => ViewModel.AutomationCurves.Remove(duplicatedCurve),
              () => ViewModel.AutomationCurves.Add(duplicatedCurve));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated automation curve '{curve.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate automation curve: {ex.Message}");
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task ExportMacroAsync(Macro macro)
    {
      var savePicker = new Windows.Storage.Pickers.FileSavePicker();
      var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
      WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

      savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
      savePicker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
      savePicker.SuggestedFileName = $"macro_{macro.Name}_{DateTime.Now:yyyyMMdd}";

      var file = await savePicker.PickSaveFileAsync();
      if (file != null)
      {
        var json = System.Text.Json.JsonSerializer.Serialize(macro,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await Windows.Storage.FileIO.WriteTextAsync(file, json);
        _toastService?.ShowToast(ToastType.Success, "Export Complete",
            $"Exported macro '{macro.Name}' to {file.Name}");
      }
    }

    private async System.Threading.Tasks.Task ExportAutomationCurveAsync(AutomationCurve curve)
    {
      var savePicker = new Windows.Storage.Pickers.FileSavePicker();
      var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
      WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

      savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
      savePicker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
      savePicker.SuggestedFileName = $"automation_curve_{curve.Name}_{DateTime.Now:yyyyMMdd}";

      var file = await savePicker.PickSaveFileAsync();
      if (file != null)
      {
        var json = System.Text.Json.JsonSerializer.Serialize(curve,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await Windows.Storage.FileIO.WriteTextAsync(file, json);
        _toastService?.ShowToast(ToastType.Success, "Export Complete",
            $"Exported automation curve '{curve.Name}' to {file.Name}");
      }
    }
  }
}