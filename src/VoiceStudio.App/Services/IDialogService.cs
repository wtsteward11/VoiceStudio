using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for displaying dialogs in a testable way.
  /// Abstracts dialog creation from ViewModels to enable unit testing.
  /// </summary>
  public interface IDialogService
  {
    /// <summary>
    /// Show a simple message dialog.
    /// </summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// Show a confirmation dialog with Yes/No options.
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Yes", string cancelText = "No");

    /// <summary>
    /// Show an input dialog for text entry.
    /// </summary>
    Task<string?> ShowInputAsync(string title, string prompt, string? defaultValue = null, string? placeholder = null);

    /// <summary>
    /// Show a file picker for opening files.
    /// </summary>
    Task<string?> ShowOpenFileAsync(string title, params string[] fileTypes);

    /// <summary>
    /// Show a file picker for opening multiple files.
    /// </summary>
    Task<string[]?> ShowOpenFilesAsync(string title, params string[] fileTypes);

    /// <summary>
    /// Show a file picker for saving files.
    /// </summary>
    Task<string?> ShowSaveFileAsync(string title, string suggestedFileName, params string[] fileTypes);

    /// <summary>
    /// Show a folder picker.
    /// </summary>
    Task<string?> ShowFolderPickerAsync(string title);

    /// <summary>
    /// Show a progress dialog for long-running operations.
    /// </summary>
    Task<IProgressDialog> ShowProgressAsync(string title, string message, bool cancellable = true);

    /// <summary>
    /// Show an error dialog with optional details.
    /// </summary>
    Task ShowErrorAsync(string title, string message, string? details = null);

    /// <summary>
    /// Show a dialog with arbitrary XAML content. Returns true if user confirmed (Primary), false if cancelled.
    /// Caller builds content (e.g. StackPanel with controls) and reads values from controls after true.
    /// </summary>
    Task<bool> ShowContentAsync(string title, object content, string primaryText = "OK", string cancelText = "Cancel");
  }

  /// <summary>
  /// Interface for controlling a progress dialog.
  /// </summary>
  public interface IProgressDialog
  {
    /// <summary>
    /// Update the progress value (0.0 to 1.0).
    /// </summary>
    void SetProgress(double value);

    /// <summary>
    /// Update the message.
    /// </summary>
    void SetMessage(string message);

    /// <summary>
    /// Check if cancellation was requested.
    /// </summary>
    bool IsCancellationRequested { get; }

    /// <summary>
    /// Close the dialog.
    /// </summary>
    void Close();
  }
}
