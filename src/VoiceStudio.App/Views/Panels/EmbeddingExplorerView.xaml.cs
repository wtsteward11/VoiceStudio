using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// EmbeddingExplorerView panel for speaker embedding visualization.
  /// </summary>
  public sealed partial class EmbeddingExplorerView : UserControl
  {
    public EmbeddingExplorerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private KeyboardShortcutService? _keyboardShortcutService;

    public EmbeddingExplorerView()
    {
      this.InitializeComponent();
      ViewModel = new EmbeddingExplorerViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          AppServices.GetProjectsClient(),
          ServiceProvider.GetProfilesClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _keyboardShortcutService = ServiceProvider.TryGetKeyboardShortcutService();

      // Register keyboard shortcuts
      if (_keyboardShortcutService != null)
      {
        RegisterKeyboardShortcuts();
      }

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(EmbeddingExplorerViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Embedding Explorer Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(EmbeddingExplorerViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Embedding Explorer", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += EmbeddingExplorerView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });

      // Register keyboard shortcuts for Enter key handling
      this.KeyDown += EmbeddingExplorerView_KeyDown;
    }

    private void RegisterKeyboardShortcuts()
    {
      if (_keyboardShortcutService == null) return;

      _keyboardShortcutService.RegisterShortcut(
          "embedding_extract",
          VirtualKey.E,
          VirtualKeyModifiers.Control,
          () => { if (ViewModel.ExtractEmbeddingCommand.CanExecute(null)) ViewModel.ExtractEmbeddingCommand.Execute(null); },
          "Extract speaker embedding"
      );

      _keyboardShortcutService.RegisterShortcut(
          "embedding_refresh",
          VirtualKey.F5,
          VirtualKeyModifiers.None,
          () => { if (ViewModel.RefreshCommand.CanExecute(null)) ViewModel.RefreshCommand.Execute(null); },
          "Refresh embeddings list"
      );

      _keyboardShortcutService.RegisterShortcut(
          "embedding_delete",
          VirtualKey.Delete,
          VirtualKeyModifiers.None,
          () => { if (ViewModel.DeleteSelectedEmbeddingsCommand.CanExecute(null)) ViewModel.DeleteSelectedEmbeddingsCommand.Execute(null); },
          "Delete selected embeddings"
      );
    }

    private void EmbeddingExplorerView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      // Handle Enter key in ComboBoxes to trigger Extract
      if (e.Key == Windows.System.VirtualKey.Enter)
      {
        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
        if (focusedElement is ComboBox comboBox && comboBox.Name == "SourceAudioComboBox" && ViewModel.ExtractEmbeddingCommand.CanExecute(null))
        {
          ViewModel.ExtractEmbeddingCommand.Execute(null);
          e.Handled = true;
          return;
        }
      }

      // Handle F5 for refresh
      if (e.Key == Windows.System.VirtualKey.F5 && ViewModel.RefreshCommand.CanExecute(null))
      {
        ViewModel.RefreshCommand.Execute(null);
        e.Handled = true;
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Speaker Embedding Explorer Help";
      HelpOverlay.HelpText = "The Speaker Embedding Explorer allows you to extract, compare, visualize, and cluster speaker embeddings from audio. Speaker embeddings are numerical representations of voice characteristics that can be used for voice similarity analysis, clustering similar voices, and visualizing voice relationships. Extract embeddings from audio files, compare them to find similar voices, visualize embeddings in 2D/3D space, and cluster them to identify voice groups.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+E", Description = "Extract embedding" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+C", Description = "Compare embeddings" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+V", Description = "Visualize embeddings" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Speaker embeddings capture voice characteristics like timbre, pitch, and speaking style");
      HelpOverlay.Tips.Add("Extract embeddings from audio files to analyze voice properties");
      HelpOverlay.Tips.Add("Compare embeddings to find similar voices or verify voice matches");
      HelpOverlay.Tips.Add("Visualize embeddings in 2D or 3D to see voice relationships");
      HelpOverlay.Tips.Add("Cluster embeddings to group similar voices together");
      HelpOverlay.Tips.Add("Use embeddings for voice similarity search and matching");
      HelpOverlay.Tips.Add("Higher similarity scores indicate more similar voices");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Embedding_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var embedding = element.DataContext ?? listView.SelectedItem;
        if (embedding != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var compareItem = new MenuFlyoutItem { Text = "Compare" };
            compareItem.Click += async (_, _) => await HandleEmbeddingMenuClick("Compare", embedding);
            menu.Items.Add(compareItem);

            var visualizeItem = new MenuFlyoutItem { Text = "Visualize" };
            visualizeItem.Click += async (_, _) => await HandleEmbeddingMenuClick("Visualize", embedding);
            menu.Items.Add(visualizeItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleEmbeddingMenuClick("Export", embedding);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleEmbeddingMenuClick("Delete", embedding);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void Cluster_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var cluster = element.DataContext ?? listView.SelectedItem;
        if (cluster != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var visualizeItem = new MenuFlyoutItem { Text = "Visualize Cluster" };
            visualizeItem.Click += async (_, _) => await HandleClusterMenuClick("Visualize", cluster);
            menu.Items.Add(visualizeItem);

            var exportItem = new MenuFlyoutItem { Text = "Export Cluster" };
            exportItem.Click += async (_, _) => await HandleClusterMenuClick("Export", cluster);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete Cluster" };
            deleteItem.Click += async (_, _) => await HandleClusterMenuClick("Delete", cluster);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleEmbeddingMenuClick(string action, object embedding)
    {
      try
      {
        switch (action.ToLower())
        {
          case "compare":
            await CompareEmbeddingsAsync(embedding);
            break;
          case "visualize":
            _toastService?.ShowToast(ToastType.Info, "Visualize", "Visualizing embedding");
            break;
          case "export":
            await ExportEmbeddingAsync(embedding);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Embedding",
              Content = "Are you sure you want to delete this embedding? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var embeddingToDelete = (VoiceStudio.App.ViewModels.EmbeddingItem)embedding;
              var embeddingIndex = ViewModel.Embeddings.IndexOf(embeddingToDelete);

              ViewModel.Embeddings.Remove(embeddingToDelete);

              // Register undo action
              if (_undoRedoService != null && embeddingIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Embedding",
                    () => ViewModel.Embeddings.Insert(embeddingIndex, embeddingToDelete),
                    () => ViewModel.Embeddings.Remove(embeddingToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Embedding deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleClusterMenuClick(string action, object cluster)
    {
      try
      {
        switch (action.ToLower())
        {
          case "visualize":
            _toastService?.ShowToast(ToastType.Info, "Visualize Cluster", "Visualizing cluster");
            break;
          case "export":
            await ExportClusterAsync(cluster);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Cluster",
              Content = "Are you sure you want to delete this cluster? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var clusterToDelete = (VoiceStudio.App.ViewModels.EmbeddingClusterItem)cluster;
              var clusterIndex = ViewModel.Clusters.IndexOf(clusterToDelete);

              ViewModel.Clusters.Remove(clusterToDelete);

              // Register undo action
              if (_undoRedoService != null && clusterIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Cluster",
                    () => ViewModel.Clusters.Insert(clusterIndex, clusterToDelete),
                    () => ViewModel.Clusters.Remove(clusterToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Cluster deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private System.Threading.Tasks.Task CompareEmbeddingsAsync(object embedding)
    {
      try
      {
        var embeddingType = embedding.GetType();
        var embeddingId = embeddingType.GetProperty("Id")?.GetValue(embedding)?.ToString() ?? "unknown";
        _toastService?.ShowToast(ToastType.Info, "Compare", $"Comparing embedding '{embeddingId}' with selected embeddings");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Compare Failed", ex.Message);
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task ExportEmbeddingAsync(object embedding)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "embedding_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var embeddingType = embedding.GetType();
          var jsonData = new
          {
            Id = embeddingType.GetProperty("Id")?.GetValue(embedding)?.ToString() ?? "unknown",
            Name = embeddingType.GetProperty("Name")?.GetValue(embedding)?.ToString() ?? "unknown",
            Created = embeddingType.GetProperty("Created")?.GetValue(embedding)?.ToString() ?? DateTime.UtcNow.ToString()
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Embedding exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    private async System.Threading.Tasks.Task ExportClusterAsync(object cluster)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "cluster_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var clusterType = cluster.GetType();
          var jsonData = new
          {
            Id = clusterType.GetProperty("Id")?.GetValue(cluster)?.ToString() ?? "unknown",
            Name = clusterType.GetProperty("Name")?.GetValue(cluster)?.ToString() ?? "unknown",
            Size = clusterType.GetProperty("Size")?.GetValue(cluster)?.ToString() ?? "0"
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Cluster exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    private void EmbeddingExplorerView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}