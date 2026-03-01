using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;
using SelectionChangedEventArgsAlias = Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;
using System.Runtime.InteropServices.WindowsRuntime;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Tag-Based Organization view.
  /// Implements IDEA 32: Tag-Based Organization UI.
  /// </summary>
  public sealed partial class TagOrganizationView : UserControl
  {
    public TagOrganizationViewModel ViewModel { get; }

    public TagOrganizationView()
    {
      this.InitializeComponent();
      ViewModel = new TagOrganizationViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      Loaded += TagOrganizationView_Loaded;

      // Setup keyboard navigation
      this.Loaded += TagOrganizationView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Tag Organization Help";
      HelpOverlay.HelpText = "The Tag Organization view lets you explore and manage tags in three different views: Cloud (visual overview with size by frequency), Hierarchy (grouped by category), and List (detailed sortable list). Click on any tag to filter by it.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh tags" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Switch between Cloud, Hierarchy, and List views for different perspectives");
      HelpOverlay.Tips.Add("Tag cloud shows larger tags for more frequently used ones");
      HelpOverlay.Tips.Add("Click a tag to filter and see only that tag's items");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }

    private void TagOrganizationView_Loaded(object _, RoutedEventArgs __)
    {
      _ = ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void TagOrganizationView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void ViewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgsAlias e)
    {
      if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
      {
        ViewModel.ViewMode = item.Content?.ToString() ?? "Cloud";
      }
    }

    private void TagCloudItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is FrameworkElement element && element.Tag is TagCloudItem item)
      {
        ViewModel.FilterByTag(item.Name);
      }
    }

    private void FilterByTag_Click(object sender, RoutedEventArgs e)
    {
      if (sender is Button button && button.CommandParameter is TagListItem item)
      {
        ViewModel.FilterByTag(item.Name);
      }
    }

    private async void EditTag_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is Button button && button.CommandParameter is TagListItem item)
        {
          var textBox = new TextBox
          {
            Text = item.Name,
            PlaceholderText = "Tag name",
            Width = 300
          };
          var dialog = new ContentDialog
          {
            Title = "Edit Tag",
            Content = textBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
          };
          var result = await dialog.ShowAsync();
          if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
          {
            await ViewModel.UpdateTag(item.Name, textBox.Text);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Unhandled error in event handler: {ex.Message}", "TagOrganizationView.xaml");
      }
    }
  }
}