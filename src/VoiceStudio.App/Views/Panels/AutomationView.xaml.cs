using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// AutomationView panel for editing automation curves.
  /// </summary>
  public sealed partial class AutomationView : UserControl
  {
    public AutomationViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public AutomationView()
    {
      this.InitializeComponent();
      ViewModel = new AutomationViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetAutomationClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Update curve editor when selected curve changes
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(ViewModel.SelectedCurve))
        {
          UpdateCurveEditor();
        }
        else if (e.PropertyName == nameof(AutomationViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Automation Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(AutomationViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Automation", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += AutomationView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Hide();
        }
      });
    }

    private void AutomationView_Loaded(object sender, RoutedEventArgs e)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void UpdateCurveEditor()
    {
      if (ViewModel.SelectedCurve != null && CurveEditor != null)
      {
        // Convert AutomationCurveItem to VoiceStudio.Core.Models.AutomationCurve model
        CurveEditor.Curve = new VoiceStudio.Core.Models.AutomationCurve
        {
          Id = ViewModel.SelectedCurve.Id,
          Name = ViewModel.SelectedCurve.Name,
          ParameterId = ViewModel.SelectedCurve.ParameterId,
          TrackId = ViewModel.SelectedCurve.TrackId,
          Points = ViewModel.SelectedCurve.Points.Select(p => new VoiceStudio.Core.Models.AutomationPoint
          {
            Time = p.Time,
            Value = p.Value,
            BezierHandleInX = p.BezierHandleInX,
            BezierHandleInY = p.BezierHandleInY,
            BezierHandleOutX = p.BezierHandleOutX,
            BezierHandleOutY = p.BezierHandleOutY
          }).ToList(),
          Interpolation = ViewModel.SelectedCurve.Interpolation
        };
      }
      else if (CurveEditor != null)
      {
        CurveEditor.Curve = null;
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Automation Help";
      HelpOverlay.HelpText = "The Automation panel allows you to create and edit automation curves for parameters like volume, pan, and effect parameters. Draw curves with different interpolation modes, adjust bezier handles for smooth transitions, and automate any parameter over time.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Click", Description = "Add point" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected point" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Click and drag to create automation points");
      HelpOverlay.Tips.Add("Adjust bezier handles for smooth or sharp transitions");
      HelpOverlay.Tips.Add("Different interpolation modes (linear, bezier, step) create different curve shapes");
      HelpOverlay.Tips.Add("Automation curves can be copied and pasted between parameters");
      HelpOverlay.Tips.Add("Use automation to create dynamic effects like fades, sweeps, and modulation");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();

      // Focus the help overlay when shown
      HelpOverlay.Focus(FocusState.Programmatic);
    }

    private void Curve_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var curve = element.DataContext ?? listView.SelectedItem;
        if (curve != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleCurveMenuClick("Edit", curve);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleCurveMenuClick("Duplicate", curve);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleCurveMenuClick("Export", curve);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleCurveMenuClick("Delete", curve);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleCurveMenuClick(string action, object curve)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedCurve = (AutomationCurveItem)curve;
            _toastService?.ShowToast(ToastType.Info, "Edit Curve", "Curve selected for editing");
            break;
          case "duplicate":
            await DuplicateCurve(curve);
            break;
          case "export":
            await ExportCurveAsync(curve);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Automation Curve",
              Content = "Are you sure you want to delete this automation curve? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var curveToDelete = (AutomationCurveItem)curve;
              var curveIndex = ViewModel.Curves.IndexOf(curveToDelete);

              ViewModel.Curves.Remove(curveToDelete);

              // Register undo action
              if (_undoRedoService != null && curveIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Automation Curve",
                    () => ViewModel.Curves.Insert(curveIndex, curveToDelete),
                    () => ViewModel.Curves.Remove(curveToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Curve deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task DuplicateCurve(object curve)
    {
      try
      {
        if (curve is AutomationCurveItem originalCurve)
        {
          // Create a new curve with the same properties
          var newCurve = new AutomationCurveItem(new VoiceStudio.App.ViewModels.AutomationCurve
          {
            Id = Guid.NewGuid().ToString(),
            Name = $"{originalCurve.Name} (Copy)",
            ParameterId = originalCurve.ParameterId,
            TrackId = originalCurve.TrackId,
            Points = originalCurve.Points,
            Interpolation = originalCurve.Interpolation
          });

          // Use ViewModel's CreateCurveAsync to properly create it
          ViewModel.SelectedTrackId = newCurve.TrackId;
          ViewModel.SelectedParameterId = newCurve.ParameterId;
          await ViewModel.CreateCurveCommand.ExecuteAsync(null);

          // Update the newly created curve with the copied points
          if (ViewModel.SelectedCurve != null)
          {
            ViewModel.SelectedCurve.Points = newCurve.Points;
            await ViewModel.UpdateCurveCommand.ExecuteAsync(ViewModel.SelectedCurve);
          }
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }

    private async System.Threading.Tasks.Task ExportCurveAsync(object curve)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "automation_curve_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var curveType = curve.GetType();
          var jsonData = new
          {
            Id = curveType.GetProperty("Id")?.GetValue(curve)?.ToString() ?? "unknown",
            Name = curveType.GetProperty("Name")?.GetValue(curve)?.ToString() ?? "unknown",
            ParameterId = curveType.GetProperty("ParameterId")?.GetValue(curve)?.ToString() ?? "unknown"
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Automation curve exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}