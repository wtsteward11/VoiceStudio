using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Utilities
{
  /// <summary>
  /// Utility for showing confirmation dialogs consistently across the application.
  /// </summary>
  public static class ConfirmationDialog
  {
    /// <summary>
    /// Shows a confirmation dialog and returns true if the user confirmed.
    /// </summary>
    public static async Task<bool> ShowAsync(
        string title,
        string message,
        string primaryButtonText = "Yes",
        string closeButtonText = "Cancel",
        ContentDialogPlacement placement = ContentDialogPlacement.Popup,
        XamlRoot? xamlRoot = null)
    {
      var root = xamlRoot ?? GetXamlRoot();
      if (root == null)
      {
        System.Diagnostics.Debug.WriteLine("[ConfirmationDialog] GetXamlRoot returned null; cannot show dialog (treating as cancelled)");
        return false;
      }

      var dialog = new ContentDialog
      {
        Title = title,
        Content = message,
        PrimaryButtonText = primaryButtonText,
        CloseButtonText = closeButtonText,
        DefaultButton = ContentDialogButton.Close,
        XamlRoot = root
      };

      var result = await dialog.ShowAsync(placement);
      return result == ContentDialogResult.Primary;
    }

    private static XamlRoot? GetXamlRoot()
    {
      if (ErrorDialogService.Root != null)
        return ErrorDialogService.Root;
      if (App.MainWindowInstance?.Content is Microsoft.UI.Xaml.FrameworkElement fe)
        return fe.XamlRoot;
      return null;
    }

    /// <summary>
    /// Shows a confirmation dialog for deleting an item.
    /// </summary>
    public static async Task<bool> ShowDeleteConfirmationAsync(
        string itemName,
        string itemType = "item",
        XamlRoot? xamlRoot = null)
    {
      return await ShowAsync(
          title: $"Delete {itemType}?",
          message: $"Are you sure you want to delete '{itemName}'? This action cannot be undone.",
          primaryButtonText: "Delete",
          closeButtonText: "Cancel",
          xamlRoot: xamlRoot
      );
    }

    /// <summary>
    /// Shows a confirmation dialog for a destructive action.
    /// </summary>
    public static async Task<bool> ShowDestructiveActionConfirmationAsync(
        string actionName,
        string message,
        XamlRoot? xamlRoot = null)
    {
      return await ShowAsync(
          title: $"Confirm {actionName}",
          message: message,
          primaryButtonText: actionName,
          closeButtonText: "Cancel",
          xamlRoot: xamlRoot
      );
    }
  }
}