using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// MCPDashboardView panel - MCP server dashboard and management.
  /// </summary>
  public sealed partial class MCPDashboardView : UserControl
  {
    public MCPDashboardViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public MCPDashboardView()
    {
      this.InitializeComponent();
      ViewModel = new MCPDashboardViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetMCPDashboardClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(MCPDashboardViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "MCP Dashboard Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(MCPDashboardViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "MCP Dashboard", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += MCPDashboardView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void MCPDashboardView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "MCP Dashboard Help";
      HelpOverlay.HelpText = "The MCP Dashboard allows you to manage MCP (Model Context Protocol) servers that extend VoiceStudio Quantum+ functionality. Add MCP servers by providing a name, type, and optional endpoint. Connect to servers to enable their operations and capabilities. Each server type (Figma, TTS, Analysis, etc.) provides different operations that can be used throughout the application. The dashboard shows server status (connected/disconnected/error), available operations, and connection history. Use connect/disconnect to manage server connections, and delete to remove servers you no longer need.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Add new server" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+C", Description = "Connect selected server" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+D", Description = "Disconnect selected server" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected server" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh dashboard" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("MCP servers extend VoiceStudio functionality with external capabilities");
      HelpOverlay.Tips.Add("Server types include: Figma, TTS, Analysis, Design, Code, Database, Custom");
      HelpOverlay.Tips.Add("Connect to servers to enable their operations");
      HelpOverlay.Tips.Add("Operations are only available when the server is connected");
      HelpOverlay.Tips.Add("Server status shows connection health (connected/disconnected/error)");
      HelpOverlay.Tips.Add("Last connected timestamp tracks when servers were successfully connected");
      HelpOverlay.Tips.Add("Error messages appear if connection fails");
      HelpOverlay.Tips.Add("Summary cards show overall MCP server statistics");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}