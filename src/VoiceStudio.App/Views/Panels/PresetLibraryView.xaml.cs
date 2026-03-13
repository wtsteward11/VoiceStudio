using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using System;
using System.Threading;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// PresetLibraryView panel for preset management.
  /// </summary>
  public sealed partial class PresetLibraryView : UserControl
  {
    public PresetLibraryViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public PresetLibraryView()
    {
      this.InitializeComponent();
      ViewModel = new PresetLibraryViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IPresetLibraryClient>(),
          AppServices.GetRequiredService<IDialogService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(PresetLibraryViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Preset Library Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(PresetLibraryViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Preset Library", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += PresetLibraryView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void PresetLibraryView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= PresetLibraryView_Loaded;
      ViewModel.XamlRoot = this.XamlRoot;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Preset Library Help";
      HelpOverlay.HelpText = "The Preset Library allows you to create, manage, and apply presets for various settings and configurations. Presets can be used for voice synthesis parameters, effect chains, automation curves, and more. Create presets to save time and maintain consistency across projects.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new preset" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+F", Description = "Focus search" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh library" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Presets can be organized by category and tagged for easy searching");
      HelpOverlay.Tips.Add("Usage count shows how often each preset has been applied");
      HelpOverlay.Tips.Add("Apply presets to projects, tracks, or specific targets");
      HelpOverlay.Tips.Add("Edit presets to update their settings or metadata");
      HelpOverlay.Tips.Add("Presets can be shared between projects and users");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Preset_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var preset = element.DataContext ?? listView.SelectedItem;
        if (preset != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var applyItem = new MenuFlyoutItem { Text = "Apply" };
            applyItem.Click += async (_, __) => await HandlePresetMenuClick("Apply", preset);
            menu.Items.Add(applyItem);

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, __) => await HandlePresetMenuClick("Edit", preset);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandlePresetMenuClick("Duplicate", preset);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, __) => await HandlePresetMenuClick("Export", preset);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandlePresetMenuClick("Delete", preset);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandlePresetMenuClick(string action, object preset)
    {
      try
      {
        switch (action.ToLower())
        {
          case "apply":
            ViewModel.SelectedPreset = (Preset)preset;
            _toastService?.ShowToast(ToastType.Info, "Apply Preset", "Preset applied");
            break;
          case "edit":
            ViewModel.SelectedPreset = (Preset)preset;
            _toastService?.ShowToast(ToastType.Info, "Edit Preset", "Preset selected for editing");
            break;
          case "duplicate":
            DuplicatePreset((Preset)preset);
            break;
          case "export":
            await ExportPresetAsync(preset);
            break;
          case "delete":
            if (preset is Preset delPreset)
            {
              await ViewModel.DeletePresetWithConfirmationAsync(delPreset, CancellationToken.None);
              _toastService?.ShowToast(ToastType.Success, "Deleted", "Preset deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicatePreset(object preset)
    {
      try
      {
        var presetType = preset.GetType();
        var presetName = presetType.GetProperty("Name")?.GetValue(preset)?.ToString() ?? "preset";
        var duplicatedPreset = Activator.CreateInstance(presetType);
        if (duplicatedPreset != null)
        {
          var nameProp = presetType.GetProperty("Name");
          if (nameProp?.CanWrite == true)
          {
            nameProp.SetValue(duplicatedPreset, $"{presetName} (Copy)");
          }
          var index = ViewModel.Presets.IndexOf((Preset)preset);
          ViewModel.Presets.Insert(index + 1, (Preset)duplicatedPreset);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Preset duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }

    private async System.Threading.Tasks.Task ExportPresetAsync(object preset)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "preset_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var presetType = preset.GetType();
          var jsonData = new
          {
            Name = presetType.GetProperty("Name")?.GetValue(preset)?.ToString() ?? "unknown",
            Id = presetType.GetProperty("Id")?.GetValue(preset)?.ToString() ?? "unknown"
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Preset exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}