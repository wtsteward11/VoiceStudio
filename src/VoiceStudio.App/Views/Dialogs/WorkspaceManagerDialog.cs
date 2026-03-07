using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VoiceStudio.App.Views.Dialogs
{
  /// <summary>
  /// Dialog for managing workspace profiles: save, rename, delete, import, export.
  /// Code-behind only (no XAML) per Phase D plan.
  /// </summary>
  public sealed class WorkspaceManagerDialog : ContentDialog
  {
    private readonly PanelStateService _panelStateService;
    private ListView _profileListView = null!;
    private List<string> _profileNames = new();

    public WorkspaceManagerDialog(XamlRoot? xamlRoot = null)
    {
      Title = "Manage Workspaces";
      PrimaryButtonText = "Close";
      DefaultButton = ContentDialogButton.Primary;
      XamlRoot = xamlRoot;

      _panelStateService = ServiceProvider.GetPanelStateService();

      var mainStack = new StackPanel { Spacing = 12 };

      var listLabel = new TextBlock { Text = "Workspace profiles:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
      mainStack.Children.Add(listLabel);

      _profileListView = new ListView
      {
        SelectionMode = ListViewSelectionMode.Single,
        MinHeight = 120,
        MaxHeight = 200
      };
      mainStack.Children.Add(_profileListView);

      var buttonStack = new StackPanel
      {
        Orientation = Orientation.Horizontal,
        Spacing = 8
      };

      var loadButton = new Button { Content = "Load", Margin = new Thickness(0, 0, 8, 0) };
      loadButton.Click += Load_Click;

      var saveAsButton = new Button { Content = "Save Current As...", Margin = new Thickness(0, 0, 8, 0) };
      saveAsButton.Click += SaveCurrentAs_Click;

      var duplicateButton = new Button { Content = "Duplicate", Margin = new Thickness(0, 0, 8, 0) };
      duplicateButton.Click += Duplicate_Click;

      var renameButton = new Button { Content = "Rename", Margin = new Thickness(0, 0, 8, 0) };
      renameButton.Click += Rename_Click;

      var resetButton = new Button { Content = "Reset", Margin = new Thickness(0, 0, 8, 0) };
      resetButton.Click += Reset_Click;

      var deleteButton = new Button { Content = "Delete", Margin = new Thickness(0, 0, 8, 0) };
      deleteButton.Click += Delete_Click;

      var exportButton = new Button { Content = "Export", Margin = new Thickness(0, 0, 8, 0) };
      exportButton.Click += Export_Click;

      var importButton = new Button { Content = "Import" };
      importButton.Click += Import_Click;

      buttonStack.Children.Add(loadButton);
      buttonStack.Children.Add(saveAsButton);
      buttonStack.Children.Add(duplicateButton);
      buttonStack.Children.Add(renameButton);
      buttonStack.Children.Add(resetButton);
      buttonStack.Children.Add(deleteButton);
      buttonStack.Children.Add(exportButton);
      buttonStack.Children.Add(importButton);

      mainStack.Children.Add(buttonStack);

      Content = mainStack;

      Loaded += async (_, _) => await RefreshProfileListAsync();
    }

    private async Task RefreshProfileListAsync()
    {
      var profiles = await _panelStateService.ListWorkspaceProfilesAsync();
      _profileNames = profiles.Select(p => p.Name).ToList();
      _profileListView.Items.Clear();
      foreach (var name in _profileNames)
        _profileListView.Items.Add(name);
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
      var selected = _profileListView.SelectedItem as string;
      if (string.IsNullOrEmpty(selected))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowInfo("Load", "Select a workspace to load.");
        return;
      }

      try
      {
        var switched = await _panelStateService.SwitchWorkspaceProfileAsync(selected);
        if (switched)
        {
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowSuccess("Workspace", $"Switched to '{selected}'");
          Hide();
        }
        else
        {
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowError("Load", $"Failed to switch to '{selected}'");
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Load failed: {ex.Message}");
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowError("Load", ex.Message);
      }
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
      var selected = _profileListView.SelectedItem as string;
      if (string.IsNullOrEmpty(selected))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowInfo("Duplicate", "Select a workspace to duplicate.");
        return;
      }

      var inputDialog = new TextInputDialog("Duplicate Workspace", "Enter name for the copy:", $"{selected} (Copy)");
      inputDialog.XamlRoot = XamlRoot;
      var result = await inputDialog.ShowAsync();

      if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputDialog.InputText))
      {
        try
        {
          var duplicate = await _panelStateService.DuplicateWorkspaceProfileAsync(selected, inputDialog.InputText);
          if (duplicate != null)
          {
            await RefreshProfileListAsync();
            var toast = ServiceProvider.TryGetToastNotificationService();
            toast?.ShowSuccess("Workspace", $"Duplicated as '{inputDialog.InputText}'");
          }
          else
          {
            var toast = ServiceProvider.TryGetToastNotificationService();
            toast?.ShowError("Duplicate", $"Source workspace '{selected}' not found.");
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Duplicate failed: {ex.Message}");
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowError("Duplicate", ex.Message);
        }
      }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
      var selected = _profileListView.SelectedItem as string;
      if (string.IsNullOrEmpty(selected))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowInfo("Rename", "Select a workspace to rename.");
        return;
      }

      if (string.Equals(selected, "studio", StringComparison.OrdinalIgnoreCase))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowWarning("Rename", "The 'studio' workspace cannot be renamed.");
        return;
      }

      var inputDialog = new TextInputDialog("Rename Workspace", "Enter new name:", selected);
      inputDialog.XamlRoot = XamlRoot;
      var result = await inputDialog.ShowAsync();

      if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputDialog.InputText) &&
          !string.Equals(inputDialog.InputText, selected, StringComparison.OrdinalIgnoreCase))
      {
        try
        {
          var renamed = await _panelStateService.RenameWorkspaceProfileAsync(selected, inputDialog.InputText);
          if (renamed)
          {
            await RefreshProfileListAsync();
            var toast = ServiceProvider.TryGetToastNotificationService();
            toast?.ShowSuccess("Workspace", $"Renamed to '{inputDialog.InputText}'");
          }
          else
          {
            var toast = ServiceProvider.TryGetToastNotificationService();
            toast?.ShowError("Rename", "Rename failed. Name may already exist.");
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Rename failed: {ex.Message}");
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowError("Rename", ex.Message);
        }
      }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
      var selected = _profileListView.SelectedItem as string;
      if (string.IsNullOrEmpty(selected))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowInfo("Reset", "Select a workspace to reset.");
        return;
      }

      var confirmed = await ConfirmationDialog.ShowAsync(
        "Reset Workspace",
        $"Reset '{selected}' to its default layout? This cannot be undone.",
        "Reset",
        "Cancel",
        ContentDialogPlacement.Popup,
        XamlRoot);
      if (!confirmed) return;

      try
      {
        var reset = await _panelStateService.ResetWorkspaceProfileAsync(selected);
        if (reset)
        {
          await RefreshProfileListAsync();
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowSuccess("Workspace", $"Reset '{selected}' to default");
        }
        else
        {
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowWarning("Reset", $"No default template for '{selected}'. Only built-in workspaces can be reset.");
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Reset failed: {ex.Message}");
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowError("Reset", ex.Message);
      }
    }

    private async void SaveCurrentAs_Click(object sender, RoutedEventArgs e)
    {
      var inputDialog = new TextInputDialog("Save Current As...", "Enter workspace name:", "My Workspace");
      inputDialog.XamlRoot = XamlRoot;
      var result = await inputDialog.ShowAsync();

      if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputDialog.InputText))
      {
        try
        {
          await _panelStateService.CreateWorkspaceProfileAsync(inputDialog.InputText);
          await RefreshProfileListAsync();
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowSuccess("Workspace", $"Saved as '{inputDialog.InputText}'");
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Save failed: {ex.Message}");
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowError("Failed", ex.Message);
        }
      }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
      var selected = _profileListView.SelectedItem as string;
      if (string.IsNullOrEmpty(selected))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowInfo("Delete", "Select a workspace to delete.");
        return;
      }

      if (string.Equals(selected, "studio", StringComparison.OrdinalIgnoreCase))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowWarning("Delete", "The 'studio' workspace cannot be deleted.");
        return;
      }

      var confirmed = await ConfirmationDialog.ShowDeleteConfirmationAsync(selected, "workspace", XamlRoot);
      if (!confirmed) return;

      try
      {
        await _panelStateService.DeleteWorkspaceProfileAsync(selected);
        await RefreshProfileListAsync();
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowSuccess("Workspace", $"Deleted '{selected}'");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Delete failed: {ex.Message}");
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowError("Failed", ex.Message);
      }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
      var selected = _profileListView.SelectedItem as string;
      if (string.IsNullOrEmpty(selected))
      {
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowInfo("Export", "Select a workspace to export.");
        return;
      }

      try
      {
        var json = await _panelStateService.ExportWorkspaceAsync(selected);

        var savePicker = new FileSavePicker();
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        savePicker.SuggestedFileName = $"{selected}_workspace.json";

        var file = await savePicker.PickSaveFileAsync();
        if (file != null)
        {
          await FileIO.WriteTextAsync(file, json);
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowSuccess("Export", $"Exported '{selected}' to {file.Name}");
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Export failed: {ex.Message}");
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowError("Failed", ex.Message);
      }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add(".json");

        var file = await openPicker.PickSingleFileAsync();
        if (file == null) return;

        var json = await FileIO.ReadTextAsync(file);
        var profile = await _panelStateService.ImportWorkspaceAsync(json);
        if (profile != null)
        {
          await RefreshProfileListAsync();
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowSuccess("Import", $"Imported '{profile.Name}'");
        }
        else
        {
          var toast = ServiceProvider.TryGetToastNotificationService();
          toast?.ShowError("Import", "Invalid workspace JSON file.");
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[WorkspaceManagerDialog] Import failed: {ex.Message}");
        var toast = ServiceProvider.TryGetToastNotificationService();
        toast?.ShowError("Failed", ex.Message);
      }
    }
  }
}
