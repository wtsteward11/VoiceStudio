using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using Windows.System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// AssistantView panel for AI production assistant.
  /// </summary>
  public sealed partial class AssistantView : UserControl
  {
    public AssistantViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private KeyboardShortcutService? _keyboardShortcutService;

    public AssistantView()
    {
      this.InitializeComponent();
      ViewModel = new AssistantViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetAssistantClient(),
          ServiceProvider.GetProjectsClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _keyboardShortcutService = ServiceProvider.TryGetKeyboardShortcutService();

      // Register keyboard shortcuts
      if (_keyboardShortcutService != null)
      {
        RegisterKeyboardShortcuts();
      }

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(AssistantViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Assistant Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(AssistantViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Assistant", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += AssistantView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void AssistantView_KeyboardNavigation_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void RegisterKeyboardShortcuts()
    {
      if (_keyboardShortcutService == null) return;

      _keyboardShortcutService.RegisterShortcut(
          "assistant_send",
          VirtualKey.Enter,
          VirtualKeyModifiers.Control,
          () => { if (ViewModel.SendMessageCommand.CanExecute(null)) ViewModel.SendMessageCommand.Execute(null); },
          "Send message to assistant"
      );

      _keyboardShortcutService.RegisterShortcut(
          "assistant_delete_conversation",
          VirtualKey.Delete,
          VirtualKeyModifiers.None,
          () => { if (ViewModel.DeleteConversationCommand.CanExecute(null)) ViewModel.DeleteConversationCommand.Execute(null); },
          "Delete selected conversation"
      );

      _keyboardShortcutService.RegisterShortcut(
          "assistant_suggest_tasks",
          VirtualKey.T,
          VirtualKeyModifiers.Control,
          () => { if (ViewModel.SuggestTasksCommand.CanExecute(null)) ViewModel.SuggestTasksCommand.Execute(null); },
          "Suggest tasks for selected project"
      );
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "AI Production Assistant Help";
      HelpOverlay.HelpText = "The AI Production Assistant helps automate and optimize your voice production workflow. Use AI-powered suggestions to improve voice synthesis quality, optimize settings, generate content, and streamline your production process. The assistant can analyze your projects, suggest improvements, generate scripts, and automate repetitive tasks. Configure assistant behavior and preferences to customize how it helps you work.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Shift+A", Description = "Open assistant" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Apply suggestion" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Esc", Description = "Close assistant" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("The assistant learns from your workflow to provide better suggestions");
      HelpOverlay.Tips.Add("Use AI suggestions to optimize voice synthesis settings automatically");
      HelpOverlay.Tips.Add("Generate scripts and content using AI-powered assistance");
      HelpOverlay.Tips.Add("Automate repetitive tasks to save time");
      HelpOverlay.Tips.Add("Configure assistant preferences to match your workflow");
      HelpOverlay.Tips.Add("Review suggestions before applying to maintain control");
      HelpOverlay.Tips.Add("The assistant can help with quality optimization and workflow efficiency");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void ChatInput_KeyDown(object _, KeyRoutedEventArgs e)
    {
      // Handle Ctrl+Enter for new line, Enter to send
      var modifiers = VirtualKeyModifiers.None;
      var keyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
      if (keyState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        modifiers |= VirtualKeyModifiers.Control;

      if (e.Key == Windows.System.VirtualKey.Enter)
      {
        if (modifiers.HasFlag(VirtualKeyModifiers.Control))
        {
          // Ctrl+Enter - allow new line (don't handle)
          return;
        }
        // Enter - send message
        if (ViewModel.SendMessageCommand.CanExecute(null))
        {
          ViewModel.SendMessageCommand.Execute(null);
        }

        e.Handled = true;
      }
    }
  }
}