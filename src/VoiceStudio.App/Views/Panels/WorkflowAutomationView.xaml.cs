using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Models;
using System.Threading.Tasks;
using Windows.System;
using System.Runtime.InteropServices.WindowsRuntime;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Workflow Automation UI view.
  /// Implements IDEA 33: Workflow Automation UI.
  /// </summary>
  public sealed partial class WorkflowAutomationView : UserControl
  {
    public WorkflowAutomationViewModel ViewModel { get; }
    private KeyboardShortcutService? _keyboardShortcutService;

    public WorkflowAutomationView()
    {
      this.InitializeComponent();
      ViewModel = new WorkflowAutomationViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Register keyboard shortcuts
      this.KeyDown += WorkflowAutomationView_KeyDown;

      // Get keyboard shortcut service
      _keyboardShortcutService = ServiceProvider.TryGetKeyboardShortcutService();
      if (_keyboardShortcutService != null)
      {
        RegisterKeyboardShortcuts();
      }

      // Setup keyboard navigation
      this.Loaded += WorkflowAutomationView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void WorkflowAutomationView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void RegisterKeyboardShortcuts()
    {
      if (_keyboardShortcutService == null) return;

      _keyboardShortcutService.RegisterShortcut(
          "workflow_new",
          VirtualKey.N,
          VirtualKeyModifiers.Control,
          () => ViewModel.CreateWorkflowCommand.Execute(null),
          "Create new workflow"
      );

      _keyboardShortcutService.RegisterShortcut(
          "workflow_save",
          VirtualKey.S,
          VirtualKeyModifiers.Control,
          async () => await ViewModel.SaveWorkflowCommand.ExecuteAsync(null),
          "Save workflow"
      );

      _keyboardShortcutService.RegisterShortcut(
          "workflow_test",
          VirtualKey.T,
          VirtualKeyModifiers.Control,
          async () => await ViewModel.TestWorkflowCommand.ExecuteAsync(null),
          "Test workflow"
      );

      _keyboardShortcutService.RegisterShortcut(
          "workflow_run",
          VirtualKey.R,
          VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
          async () => await ViewModel.RunWorkflowCommand.ExecuteAsync(null),
          "Run workflow"
      );
    }

    private void WorkflowAutomationView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var modifiers = VirtualKeyModifiers.None;
      var keyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
      if (keyState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        modifiers |= VirtualKeyModifiers.Control;

      keyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
      if (keyState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        modifiers |= VirtualKeyModifiers.Shift;

      keyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
      if (keyState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        modifiers |= VirtualKeyModifiers.Menu;

      // Handle Ctrl+Enter for multi-line text boxes
      if (e.Key == VirtualKey.Enter && modifiers.HasFlag(VirtualKeyModifiers.Control))
      {
        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
        if (focusedElement is TextBox textBox && textBox.AcceptsReturn)
        {
          // Ctrl+Enter saves workflow
          if (ViewModel.SaveWorkflowCommand.CanExecute(null))
          {
            _ = ViewModel.SaveWorkflowCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
          }
        }
      }

      if (_keyboardShortcutService?.TryHandleKeyDown((VirtualKey)e.Key, modifiers) == true)
      {
        e.Handled = true;
      }
    }

    private void ActionItem_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs _)
    {
      if (sender is FrameworkElement element && element.Tag is string actionType)
      {
        var actionName = GetActionName(actionType);
        ViewModel.AddStep(actionType, actionName);
      }
    }

    private void TemplateItem_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs _)
    {
      if (sender is FrameworkElement element && element.Tag is WorkflowTemplate template)
      {
        LoadTemplateIntoWorkflow(template);
      }
    }

    private void LoadTemplateIntoWorkflow(WorkflowTemplate template)
    {
      ViewModel.WorkflowName = template.Name;
      ViewModel.WorkflowDescription = template.Description;

      ViewModel.WorkflowSteps.Clear();

      switch (template.Id)
      {
        case "batch_export":
          ViewModel.AddStep("synthesize_voice", "Synthesize Voice");
          ViewModel.AddStep("export_audio", "Export Audio");
          break;
        case "quality_check":
          ViewModel.AddStep("synthesize_voice", "Synthesize Voice");
          ViewModel.AddStep("apply_effect", "Apply Enhancement");
          break;
        case "effect_processing":
          ViewModel.AddStep("apply_chain", "Apply Effect Chain");
          ViewModel.AddStep("export_audio", "Export Audio");
          break;
      }
    }

    private async void ConfigureStep_Click(object sender, RoutedEventArgs _)
    {
      try
      {
        if (sender is Button button && button.CommandParameter is WorkflowStep step)
        {
          ViewModel.SelectedStep = step;
          await ShowStepConfigurationDialog(step);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async Task ShowStepConfigurationDialog(WorkflowStep step)
    {
      var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
      {
        Title = $"Configure {step.Name}",
        Content = $"Configuration for step: {step.Name}\nType: {step.Type}",
        PrimaryButtonText = "Save",
        CloseButtonText = "Cancel",
        XamlRoot = this.XamlRoot,
        DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
      };

      var result = await dialog.ShowAsync();
      if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
      {
        ViewModel.SelectedStep = step;
      }
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is WorkflowStep step)
      {
        ViewModel.RemoveStep(step);
      }
    }

    private async void AddVariable_Click(object _, RoutedEventArgs __)
    {
      try
      {
        await ShowAddVariableDialog();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async Task ShowAddVariableDialog()
    {
      var nameTextBox = new Microsoft.UI.Xaml.Controls.TextBox
      {
        PlaceholderText = "Variable name",
        Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12)
      };

      var valueTextBox = new Microsoft.UI.Xaml.Controls.TextBox
      {
        PlaceholderText = "Variable value",
        AcceptsReturn = false
      };

      var stackPanel = new Microsoft.UI.Xaml.Controls.StackPanel
      {
        Spacing = 8
      };
      stackPanel.Children.Add(nameTextBox);
      stackPanel.Children.Add(valueTextBox);

      var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
      {
        Title = "Add Variable",
        Content = stackPanel,
        PrimaryButtonText = "Add",
        CloseButtonText = "Cancel",
        XamlRoot = this.XamlRoot,
        DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
      };

      var result = await dialog.ShowAsync();
      if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
      {
        var name = nameTextBox.Text?.Trim() ?? string.Empty;
        var value = valueTextBox.Text?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(name))
        {
          ViewModel.AddVariable(name, value);
        }
      }
    }

    private void RemoveVariable_Click(object sender, RoutedEventArgs _)
    {
      if (sender is Button button && button.CommandParameter is WorkflowVariable variable)
      {
        ViewModel.RemoveVariable(variable);
      }
    }

    private string GetActionName(string actionType)
    {
      return actionType switch
      {
        "synthesize_voice" => "Synthesize Voice",
        "batch_synthesize" => "Batch Synthesize",
        "apply_effect" => "Apply Effect",
        "apply_chain" => "Apply Effect Chain",
        "export_audio" => "Export Audio",
        "export_batch" => "Export Batch",
        "if_condition" => "If Condition",
        "loop" => "Loop",
        "set_variable" => "Set Variable",
        _ => actionType
      };
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Workflow Automation Help";
      HelpOverlay.HelpText = "The Workflow Automation panel allows you to create and manage automation workflows. Workflows are sequences of actions that can be executed to automate complex tasks. You can drag actions from the library, configure them, and chain them together to create powerful automation sequences.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new workflow" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save workflow" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+T", Description = "Test workflow" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Shift+R", Description = "Run workflow" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+V", Description = "Add variable" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete step or variable" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Save workflow (in description field)" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Drag actions from the library to add them to your workflow");
      HelpOverlay.Tips.Add("Configure each step by clicking the Configure button");
      HelpOverlay.Tips.Add("Use variables to store and reuse values across steps");
      HelpOverlay.Tips.Add("Test workflows before running them to catch errors early");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}