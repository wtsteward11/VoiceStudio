using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// JobProgressView panel for monitoring all job types.
  /// </summary>
  public sealed partial class JobProgressView : UserControl
  {
    public JobProgressViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public JobProgressView()
    {
      this.InitializeComponent();
      ViewModel = new JobProgressViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IJobProgressApiClient>(),
          AppServices.GetService<VoiceStudio.Core.Services.IWebSocketService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(JobProgressViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Job Progress Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(JobProgressViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Job Progress", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += JobProgressView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void JobProgressView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Job Progress Help";
      HelpOverlay.HelpText = "The Job Progress panel allows you to monitor all running jobs in VoiceStudio. View job status, progress, and details. Use filters to find specific jobs, pause/resume running jobs, cancel pending jobs, and delete completed jobs.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh job list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Auto-refresh automatically updates the job list every few seconds");
      HelpOverlay.Tips.Add("Use filters to find jobs by type (synthesis, training, etc.) or status");
      HelpOverlay.Tips.Add("Running jobs show progress bars and estimated time remaining");
      HelpOverlay.Tips.Add("Failed jobs display error messages to help diagnose issues");
      HelpOverlay.Tips.Add("Clear completed jobs to keep the list manageable");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Job_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var job = element.DataContext ?? listView.SelectedItem;
        if (job != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var pauseItem = new MenuFlyoutItem { Text = "Pause" };
            pauseItem.Click += async (_, __) => await HandleJobMenuClick("Pause", job);
            menu.Items.Add(pauseItem);

            var resumeItem = new MenuFlyoutItem { Text = "Resume" };
            resumeItem.Click += async (_, __) => await HandleJobMenuClick("Resume", job);
            menu.Items.Add(resumeItem);

            var cancelItem = new MenuFlyoutItem { Text = "Cancel" };
            cancelItem.Click += async (_, __) => await HandleJobMenuClick("Cancel", job);
            menu.Items.Add(cancelItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleJobMenuClick("Delete", job);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private System.Threading.Tasks.Task HandleJobMenuClick(string action, object job)
    {
      try
      {
        switch (action.ToLower())
        {
          case "pause":
            _toastService?.ShowToast(ToastType.Info, "Pause", "Pausing job...");
            break;
          case "resume":
            _toastService?.ShowToast(ToastType.Info, "Resume", "Resuming job...");
            break;
          case "cancel":
            _toastService?.ShowToast(ToastType.Info, "Cancel", "Cancelling job...");
            break;
          case "delete":
            _toastService?.ShowToast(ToastType.Info, "Delete", "Deleting job...");
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }

      return System.Threading.Tasks.Task.CompletedTask;
    }
  }
}