using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// AIProductionAssistantView panel - AI-driven helper with natural language interface.
  /// </summary>
  public sealed partial class AIProductionAssistantView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public AIProductionAssistantViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public AIProductionAssistantView()
    {
      this.InitializeComponent();
      ViewModel = new AIProductionAssistantViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetAIProductionAssistantClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(AIProductionAssistantViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "AI Assistant Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(AIProductionAssistantViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "AI Assistant", ViewModel.StatusMessage);
        }
      };
    }
  }
}