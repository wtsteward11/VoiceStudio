using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
    public sealed partial class AdvancedSpectrogramVisualizationView : UserControl
    {
        public AdvancedSpectrogramVisualizationViewModel ViewModel { get; }

        public AdvancedSpectrogramVisualizationView()
        {
            this.InitializeComponent();
            var backendClient = ServiceProvider.GetBackendClient();
            ViewModel = new AdvancedSpectrogramVisualizationViewModel(
                AppServices.GetRequiredService<IViewModelContext>(),
                backendClient);
            this.DataContext = ViewModel;
        }
    }
}
