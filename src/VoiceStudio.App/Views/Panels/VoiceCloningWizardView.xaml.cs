using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// VoiceCloningWizardView panel - Step-by-step voice cloning wizard.
  /// </summary>
  public sealed partial class VoiceCloningWizardView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public VoiceCloningWizardViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private IDragDropService? _panelDragDropService;

    public VoiceCloningWizardView()
    {
      this.InitializeComponent();
      ViewModel = new VoiceCloningWizardViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<IVoiceCloningWizardClient>()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _panelDragDropService = AppServices.TryGetDragDropService();

      // Add keyboard navigation
      this.KeyDown += VoiceCloningWizardView_KeyDown;

      // Setup keyboard navigation
      this.Loaded += VoiceCloningWizardView_KeyboardNavigation_Loaded;
      this.Unloaded += VoiceCloningWizardView_Unloaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });

      // Subscribe to ViewModel events for toast notifications and step visibility
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VoiceCloningWizardViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Voice Cloning Wizard Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VoiceCloningWizardViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Voice Cloning Wizard", ViewModel.StatusMessage);
        }
        else if (e.PropertyName == nameof(VoiceCloningWizardViewModel.CurrentStep))
        {
          UpdateStepVisibility();
          if (ViewModel.CurrentStep == 4)
          {
            _toastService?.ShowToast(ToastType.Success, "Wizard Complete", "Voice cloning completed successfully!");
          }
        }
      };

      // Initial step visibility
      UpdateStepVisibility();
    }

    private void VoiceCloningWizardView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      // Escape key cancels wizard (key code 27)
      if ((int)e.Key == 27)
      {
        if (ViewModel.CancelWizardCommand.CanExecute(null))
        {
          ViewModel.CancelWizardCommand.Execute(null);
          e.Handled = true;
        }
      }
      // Enter key in single-line TextBoxes triggers validation or next step (key code 13)
      else if ((int)e.Key == 13)
      {
        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
        if (focusedElement is Microsoft.UI.Xaml.Controls.TextBox textBox && !textBox.AcceptsReturn)
        {
          // Step 1: Enter in audio file field triggers validation if file is selected
          if (ViewModel.CurrentStep == 1 && ViewModel.SelectedAudioFile is not null)
          {
            if (ViewModel.ValidateAudioCommand.CanExecute(null))
            {
              ViewModel.ValidateAudioCommand.Execute(null);
              e.Handled = true;
            }
          }
          // Step 2: Enter in profile name field triggers next step if valid
          else if (ViewModel.CurrentStep == 2 && !string.IsNullOrWhiteSpace(ViewModel.ProfileName))
          {
            if (ViewModel.NextStepCommand.CanExecute(null))
            {
              ViewModel.NextStepCommand.Execute(null);
              e.Handled = true;
            }
          }
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Cloning Wizard Help";
      HelpOverlay.HelpText = "The Voice Cloning Wizard guides you through creating a voice profile step-by-step. Step 1: Upload a reference audio file (WAV, MP3, FLAC, M4A) containing the voice you want to clone. The audio should be clear, at least 3 seconds long, and preferably 10+ seconds for best results. Validate the audio to check for issues. Step 2: Configure the cloning settings - choose an engine (XTTS, Chatterbox, Tortoise), quality mode (fast/standard/high/ultra), and provide a profile name and optional description. Step 3: The system processes the voice cloning. Progress is shown in real-time. Step 4: Review the results, including quality metrics and a test synthesis. Finalize to create the voice profile.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Next step" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+P", Description = "Previous step" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+B", Description = "Browse audio file" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+V", Description = "Validate audio" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Esc", Description = "Cancel wizard" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use clear, high-quality audio recordings for best results");
      HelpOverlay.Tips.Add("Audio should be at least 3 seconds, preferably 10+ seconds");
      HelpOverlay.Tips.Add("Mono audio is recommended for voice cloning");
      HelpOverlay.Tips.Add("Sample rate should be 16kHz or higher");
      HelpOverlay.Tips.Add("XTTS engine offers good balance of quality and speed");
      HelpOverlay.Tips.Add("Tortoise engine provides highest quality but slower processing");
      HelpOverlay.Tips.Add("Quality modes: fast (quick, lower quality), standard (balanced), high (best quality, slower), ultra (maximum quality, very slow)");
      HelpOverlay.Tips.Add("Review quality metrics to assess cloning success");
      HelpOverlay.Tips.Add("Test synthesis allows you to preview the cloned voice");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void VoiceCloningWizardView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);

      // Register as drop target for ReferenceAudio and Asset payloads (Panel Architecture Phase 4)
      _panelDragDropService?.RegisterDropTarget(
          ViewModel.PanelId,
          CanAcceptDrop);

      // Initialize wizard data from Loaded (ADR-047: no constructor fire-and-forget)
      ViewModel.InitializeAsync();
    }

    private void VoiceCloningWizardView_Unloaded(object _, RoutedEventArgs __)
    {
      // Unregister from drop target (Panel Architecture Phase 4)
      _panelDragDropService?.UnregisterDropTarget(ViewModel.PanelId);
    }

    /// <summary>
    /// Determines if this panel can accept a drag payload.
    /// Accepts ReferenceAudio, Assets (audio files), and external files.
    /// </summary>
    private static bool CanAcceptDrop(DragPayload payload)
    {
      return payload.PayloadType == DragPayloadType.ReferenceAudio ||
             payload.PayloadType == DragPayloadType.Asset ||
             payload.PayloadType == DragPayloadType.ExternalFile;
    }

    /// <summary>
    /// Handles a dropped audio file for voice cloning.
    /// </summary>
    private async Task HandleReferenceAudioDropAsync(DragPayload payload, CancellationToken cancellationToken)
    {
      // Only accept drops on Step 1 (audio selection)
      if (ViewModel.CurrentStep != 1)
      {
        _toastService?.ShowToast(ToastType.Warning, "Cannot Drop", "Return to Step 1 to add reference audio");
        return;
      }

      var audioItem = payload.Items.FirstOrDefault();
      if (audioItem == null)
        return;

      switch (payload.PayloadType)
      {
        case DragPayloadType.ReferenceAudio:
        case DragPayloadType.Asset:
          // Use the dropped audio as reference
          _toastService?.ShowToast(ToastType.Success, "Reference Audio Added", $"Using '{audioItem.DisplayName}' as reference");
          break;

        case DragPayloadType.ExternalFile:
          // Load external audio file path
          _toastService?.ShowToast(ToastType.Info, "Loading Audio", $"Loading '{audioItem.DisplayName}'...");
          break;
      }

      await Task.CompletedTask;
    }

    private void UpdateStepVisibility()
    {
      // Find step panels by name (XAML-generated fields may not be available during compilation)
      var step1 = this.FindName("Step1Panel") as Microsoft.UI.Xaml.UIElement;
      var step2 = this.FindName("Step2Panel") as Microsoft.UI.Xaml.UIElement;
      var step3 = this.FindName("Step3Panel") as Microsoft.UI.Xaml.UIElement;
      var step4 = this.FindName("Step4Panel") as Microsoft.UI.Xaml.UIElement;

      if (step1 != null)
        step1.Visibility = ViewModel.CurrentStep == 1 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
      if (step2 != null)
        step2.Visibility = ViewModel.CurrentStep == 2 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
      if (step3 != null)
        step3.Visibility = ViewModel.CurrentStep == 3 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
      if (step4 != null)
        step4.Visibility = ViewModel.CurrentStep == 4 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void StepIndicator_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
      // Right-click on step indicator - could show context menu with step info
      // Currently no-op to prevent runtime errors
      e.Handled = true;
    }

    private void ValidationResults_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
      // Right-click on validation results - could show context menu with copy/export options
      // Currently no-op to prevent runtime errors
      e.Handled = true;
    }

    private void QualityMetrics_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
      // Right-click on quality metrics - could show context menu with copy/export options
      // Currently no-op to prevent runtime errors
      e.Handled = true;
    }
  }
}