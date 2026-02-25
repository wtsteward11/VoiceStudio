using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
    public sealed partial class AdvancedWaveformVisualizationView : UserControl
    {
        public AdvancedWaveformVisualizationViewModel ViewModel { get; }

        public AdvancedWaveformVisualizationView()
        {
            this.InitializeComponent();
            var backendClient = ServiceProvider.GetBackendClient();
            ViewModel = new AdvancedWaveformVisualizationViewModel(
                AppServices.GetRequiredService<IViewModelContext>(),
                backendClient);
            this.DataContext = ViewModel;
        }
    }
}
