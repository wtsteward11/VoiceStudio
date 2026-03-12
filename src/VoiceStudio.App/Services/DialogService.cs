using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// WinUI 3 implementation of the dialog service.
  /// All ContentDialog creation routes through GetXamlRoot(); failures log and fail soft.
  /// </summary>
  public class DialogService : IDialogService
  {
    private readonly Window _window;

    public DialogService(Window window)
    {
      _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// Returns XamlRoot from window content. Logs and returns null if unavailable; never throws.
    /// </summary>
    private XamlRoot? GetXamlRoot()
    {
      if (_window?.Content is FrameworkElement fe && fe.XamlRoot != null)
        return fe.XamlRoot;
      System.Diagnostics.Debug.WriteLine("[DialogService] GetXamlRoot: window content or XamlRoot is null");
      return null;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
      var root = GetXamlRoot();
      if (root == null) return;

      var dialog = new ContentDialog
      {
        Title = title,
        Content = message,
        CloseButtonText = "OK",
        XamlRoot = root
      };

      await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Yes", string cancelText = "No")
    {
      var root = GetXamlRoot();
      if (root == null) return false;

      var dialog = new ContentDialog
      {
        Title = title,
        Content = message,
        PrimaryButtonText = confirmText,
        CloseButtonText = cancelText,
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = root
      };

      var result = await dialog.ShowAsync();
      return result == ContentDialogResult.Primary;
    }

    public async Task<string?> ShowInputAsync(string title, string prompt, string? defaultValue = null, string? placeholder = null)
    {
      var root = GetXamlRoot();
      if (root == null) return null;

      var textBox = new TextBox
      {
        Text = defaultValue ?? "",
        PlaceholderText = placeholder ?? "",
        Width = 300
      };

      var dialog = new ContentDialog
      {
        Title = title,
        Content = new StackPanel
        {
          Spacing = 8,
          Children =
          {
            new TextBlock { Text = prompt },
            textBox
          }
        },
        PrimaryButtonText = "OK",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = root
      };

      var result = await dialog.ShowAsync();
      
      if (result == ContentDialogResult.Primary)
      {
        return textBox.Text;
      }

      return null;
    }

    public async Task<string?> ShowOpenFileAsync(string title, params string[] fileTypes)
    {
      try
      {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        
        foreach (var type in fileTypes)
        {
          picker.FileTypeFilter.Add(type.StartsWith(".") ? type : $".{type}");
        }

        if (fileTypes.Length == 0)
        {
          picker.FileTypeFilter.Add("*");
        }

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
      }
      catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80004005))
      {
        // WinRT FileOpenPicker fails on some systems - use native Win32 dialog as fallback
        System.Diagnostics.Debug.WriteLine($"[DialogService] WinRT FileOpenPicker failed (0x80004005), using native fallback");
        var hwnd = WindowNative.GetWindowHandle(_window);
        return await NativeFileDialog.ShowOpenFileDialogAsync(hwnd, title, fileTypes);
      }
    }

    public async Task<string[]?> ShowOpenFilesAsync(string title, params string[] fileTypes)
    {
      try
      {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        
        foreach (var type in fileTypes)
        {
          picker.FileTypeFilter.Add(type.StartsWith(".") ? type : $".{type}");
        }

        if (fileTypes.Length == 0)
        {
          picker.FileTypeFilter.Add("*");
        }

        var files = await picker.PickMultipleFilesAsync();
        
        if (files == null || files.Count == 0)
          return null;

        var paths = new List<string>();
        foreach (var file in files)
        {
          paths.Add(file.Path);
        }

        return paths.ToArray();
      }
      catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80004005))
      {
        // WinRT FileOpenPicker fails on some systems - use native Win32 dialog as fallback
        System.Diagnostics.Debug.WriteLine($"[DialogService] WinRT FileOpenPicker failed (0x80004005), using native fallback");
        var hwnd = WindowNative.GetWindowHandle(_window);
        return await NativeFileDialog.ShowOpenFilesDialogAsync(hwnd, title, fileTypes);
      }
    }

    public async Task<string?> ShowSaveFileAsync(string title, string suggestedFileName, params string[] fileTypes)
    {
      var picker = new FileSavePicker();
      InitializePicker(picker);
      
      picker.SuggestedFileName = suggestedFileName;

      var typeList = new List<string>();
      foreach (var type in fileTypes)
      {
        typeList.Add(type.StartsWith(".") ? type : $".{type}");
      }

      if (typeList.Count == 0)
      {
        typeList.Add(".txt");
      }

      picker.FileTypeChoices.Add("Files", typeList);

      var file = await picker.PickSaveFileAsync();
      return file?.Path;
    }

    public async Task<string?> ShowFolderPickerAsync(string title)
    {
      var picker = new FolderPicker();
      InitializePicker(picker);
      picker.FileTypeFilter.Add("*");

      var folder = await picker.PickSingleFolderAsync();
      return folder?.Path;
    }

    public Task<IProgressDialog> ShowProgressAsync(string title, string message, bool cancellable = true)
    {
      var root = GetXamlRoot();
      if (root == null)
        return Task.FromResult<IProgressDialog>(new NoOpProgressDialog());

      var progressRing = new ProgressRing { IsActive = true };
      var messageText = new TextBlock { Text = message };
      
      var dialog = new ContentDialog
      {
        Title = title,
        Content = new StackPanel
        {
          Spacing = 16,
          Children = { progressRing, messageText }
        },
        CloseButtonText = cancellable ? "Cancel" : null,
        XamlRoot = root
      };

      var progressDialog = new ProgressDialogImpl(dialog, progressRing, messageText);
      
      // Show dialog without awaiting (let caller control when to close)
      _ = dialog.ShowAsync();
      
      return Task.FromResult<IProgressDialog>(progressDialog);
    }

    public async Task ShowErrorAsync(string title, string message, string? details = null)
    {
      var root = GetXamlRoot();
      if (root == null) return;

      var content = new StackPanel { Spacing = 8 };
      content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
      
      if (!string.IsNullOrEmpty(details))
      {
        var expander = new Expander
        {
          Header = "Details",
          Content = new ScrollViewer
          {
            MaxHeight = 200,
            Content = new TextBlock
            {
              Text = details,
              TextWrapping = TextWrapping.Wrap,
              FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
              FontSize = 12
            }
          }
        };
        content.Children.Add(expander);
      }

      var dialog = new ContentDialog
      {
        Title = title,
        Content = content,
        CloseButtonText = "OK",
        XamlRoot = root
      };

      await dialog.ShowAsync();
    }

    public async Task<bool> ShowContentAsync(string title, object content, string primaryText = "OK", string cancelText = "Cancel")
    {
      var root = GetXamlRoot();
      if (root == null) return false;

      var dialog = new ContentDialog
      {
        Title = title,
        Content = content,
        PrimaryButtonText = primaryText,
        CloseButtonText = cancelText,
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = root
      };

      var result = await dialog.ShowAsync();
      return result == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Shows a profile edit dialog with name, language, emotion, tags.
    /// Returns (Name, Language, Emotion, Tags) if user confirmed, null if cancelled or root unavailable.
    /// </summary>
    public async Task<(string Name, string Language, string Emotion, string Tags)?> ShowProfileEditAsync(string name, string language, string emotion, string tags)
    {
      var root = GetXamlRoot();
      if (root == null) return null;

      var nameBox = new TextBox { Header = "Name", Text = name ?? string.Empty };
      var languageBox = new TextBox { Header = "Language", Text = language ?? string.Empty };
      var emotionBox = new TextBox { Header = "Emotion", Text = emotion ?? string.Empty };
      var tagsBox = new TextBox { Header = "Tags (comma-separated)", Text = tags ?? string.Empty };

      var editPanel = new StackPanel
      {
        Spacing = 8,
        Children = { nameBox, languageBox, emotionBox, tagsBox }
      };

      var dialog = new ContentDialog
      {
        Title = "Edit Profile",
        Content = editPanel,
        PrimaryButtonText = "Save",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = root
      };

      var result = await dialog.ShowAsync();
      if (result == ContentDialogResult.Primary)
        return (nameBox.Text ?? string.Empty, languageBox.Text ?? string.Empty, emotionBox.Text ?? string.Empty, tagsBox.Text ?? string.Empty);
      return null;
    }

    private void InitializePicker(object picker)
    {
      var hwnd = WindowNative.GetWindowHandle(_window);
      InitializeWithWindow.Initialize(picker, hwnd);
    }

    private class ProgressDialogImpl : IProgressDialog
    {
      private readonly ContentDialog _dialog;
      private readonly ProgressRing _progressRing;
      private readonly TextBlock _messageText;
      private bool _isCancelled;

      public ProgressDialogImpl(ContentDialog dialog, ProgressRing progressRing, TextBlock messageText)
      {
        _dialog = dialog;
        _progressRing = progressRing;
        _messageText = messageText;
        
        _dialog.CloseButtonClick += (s, e) => _isCancelled = true;
      }

      public bool IsCancellationRequested => _isCancelled;

      public void SetProgress(double value)
      {
        if (_progressRing.IsIndeterminate)
        {
          _progressRing.IsIndeterminate = false;
        }
        _progressRing.Value = value * 100;
      }

      public void SetMessage(string message)
      {
        _messageText.Text = message;
      }

      public void Close()
      {
        _dialog.Hide();
      }
    }

    private sealed class NoOpProgressDialog : IProgressDialog
    {
      public bool IsCancellationRequested => false;
      public void SetProgress(double value) { }
      public void SetMessage(string message) { }
      public void Close() { }
    }
  }
}
