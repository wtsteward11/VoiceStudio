using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using Windows.Storage;
using Windows.Storage.Pickers;
using System;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class ImageGenView : UserControl
  {
    public ImageGenViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public ImageGenView()
    {
      this.InitializeComponent();
      var backendClient = VoiceStudio.App.Services.ServiceProvider.GetBackendClient();
      ViewModel = new ImageGenViewModel(AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(), backendClient);
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ImageGenViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Image Generation Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(ImageGenViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Image Generation", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += ImageGenView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Hide();
        }
      });

      // Setup Enter key to generate image (when prompt is focused)
      KeyboardNavigationHelper.SetupEnterKeyHandling(this, () =>
      {
        if (ViewModel.GenerateCommand.CanExecute(null))
        {
          ViewModel.GenerateCommand.Execute(null);
        }
      });
    }

    private void ImageGenView_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Image Generation Help";
      HelpOverlay.HelpText = "The Image Generation panel allows you to create images from text prompts using various AI engines. Select an engine, enter your prompt, adjust parameters (width, height, steps, CFG scale), and generate. You can also upscale generated images using Real-ESRGAN.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Generate image" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "New line in prompt" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Enter a detailed prompt describing the image you want to generate.");
      HelpOverlay.Tips.Add("Use negative prompts to exclude unwanted elements.");
      HelpOverlay.Tips.Add("Higher step counts produce better quality but take longer.");
      HelpOverlay.Tips.Add("CFG scale controls how closely the engine follows your prompt.");
      HelpOverlay.Tips.Add("Set a seed value to reproduce the same image.");
      HelpOverlay.Tips.Add("Click 'Upscale Selected' to enhance image resolution using Real-ESRGAN.");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Image_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var image = element.DataContext ?? listView.SelectedItem;
        if (image != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var viewItem = new MenuFlyoutItem { Text = "View Full Size" };
            viewItem.Click += async (_, __) => await HandleImageMenuClick("View", image);
            menu.Items.Add(viewItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, __) => await HandleImageMenuClick("Export", image);
            menu.Items.Add(exportItem);

            var upscaleItem = new MenuFlyoutItem { Text = "Upscale" };
            upscaleItem.Click += async (_, __) => await HandleImageMenuClick("Upscale", image);
            menu.Items.Add(upscaleItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleImageMenuClick("Duplicate", image);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleImageMenuClick("Delete", image);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async void QualitySettings_Click(object _, RoutedEventArgs __)
    {
      try
      {
        var dialog = new ContentDialog
        {
          Title = "Quality Settings",
          Content = $"Current Settings:\n\nSteps: {ViewModel.Steps}\nCFG Scale: {ViewModel.CfgScale:F1}\nWidth: {ViewModel.Width}\nHeight: {ViewModel.Height}\n\nQuality Preset: {ViewModel.SelectedQualityPreset?.Name ?? "Custom"}\n\nImage Clarity: {ViewModel.ImageClarity:F1}%\nImage Detail: {ViewModel.ImageDetail:F1}%\nStyle Fidelity: {ViewModel.ImageStyleFidelity:F1}%\nOverall Quality: {ViewModel.ImageOverallQuality:F1}%",
          PrimaryButtonText = "OK",
          XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleImageMenuClick(string action, object image)
    {
      try
      {
        switch (action.ToLower())
        {
          case "view":
            ViewModel.SelectedImage = (image as GeneratedImage);
            _toastService?.ShowToast(ToastType.Info, "View Image", "Viewing full size image");
            break;
          case "export":
            await ExportImageAsync(image);
            break;
          case "upscale":
            if (ViewModel.UpscaleCommand.CanExecute(null))
            {
              await ViewModel.UpscaleCommand.ExecuteAsync(null);
            }
            else
            {
              _toastService?.ShowToast(ToastType.Warning, "Upscale", "Please select an image to upscale");
            }
            break;
          case "duplicate":
            DuplicateImage(image);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Image",
              Content = "Are you sure you want to delete this generated image? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var imageToDelete = image as GeneratedImage;
              if (imageToDelete == null) break;
              
              var imageIndex = ViewModel.GeneratedImages.IndexOf(imageToDelete);
              ViewModel.GeneratedImages.Remove(imageToDelete);

              // Register undo action
              if (_undoRedoService != null && imageIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Generated Image",
                    () => ViewModel.GeneratedImages.Insert(imageIndex, imageToDelete),
                    () => ViewModel.GeneratedImages.Remove(imageToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Image deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task ExportImageAsync(object image)
    {
      if (image is not GeneratedImage generatedImage)
        return;

      try
      {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });
        picker.FileTypeChoices.Add("JPEG", new List<string> { ".jpg", ".jpeg" });
        picker.SuggestedFileName = $"generated_image_{DateTime.Now:yyyyMMdd_HHmmss}";

        // Required for WinUI 3 unpackaged desktop apps
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          if (!string.IsNullOrEmpty(generatedImage.ImageBase64))
          {
            var bytes = Convert.FromBase64String(generatedImage.ImageBase64);
            await FileIO.WriteBytesAsync(file, bytes);
            _toastService?.ShowToast(ToastType.Success, "Exported", $"Image saved to {file.Name}");
          }
          else if (!string.IsNullOrEmpty(generatedImage.ImageUrl))
          {
            // Download from URL
            using (var client = new System.Net.Http.HttpClient())
            {
              var imageBytes = await client.GetByteArrayAsync(generatedImage.ImageUrl);
              await FileIO.WriteBytesAsync(file, imageBytes);
              _toastService?.ShowToast(ToastType.Success, "Exported", $"Image saved to {file.Name}");
            }
          }
          else
          {
            _toastService?.ShowToast(ToastType.Warning, "Export", "Image data not available");
          }
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    private void DuplicateImage(object image)
    {
      if (image is GeneratedImage generatedImage)
      {
        var duplicate = new GeneratedImage
        {
          ImageId = Guid.NewGuid().ToString(),
          ImageUrl = generatedImage.ImageUrl,
          ImageBase64 = generatedImage.ImageBase64,
          Width = generatedImage.Width,
          Height = generatedImage.Height,
          Format = generatedImage.Format,
          Prompt = $"{generatedImage.Prompt} (Copy)",
          Engine = generatedImage.Engine,
          GeneratedAt = DateTime.Now,
          QualityMetrics = generatedImage.QualityMetrics
        };
        ViewModel.GeneratedImages.Insert(0, duplicate);
        ViewModel.SelectedImage = duplicate;
        _toastService?.ShowToast(ToastType.Success, "Duplicated", "Image duplicated");
      }
    }
  }
}