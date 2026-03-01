using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels;

public sealed partial class EngineSetupWizardView : UserControl
{
    public EngineSetupWizardViewModel ViewModel { get; }

    public EngineSetupWizardView()
    {
        this.InitializeComponent();
        ViewModel = new EngineSetupWizardViewModel(ServiceProvider.GetBackendClient());
        this.DataContext = ViewModel;

        this.Loaded += async (_, _) =>
        {
            await ViewModel.CheckSystemCommand.ExecuteAsync(null);
        };
    }

    private void SelectEngine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string engineId)
        {
            ViewModel.SelectEngineCommand.Execute(engineId);
        }
    }

    private void SkipSetup_Click(object sender, RoutedEventArgs e)
    {
        var toast = ServiceProvider.GetToastNotificationService();
        toast?.ShowInfo("You can install engines later from Settings > Engines.", "Setup Skipped");
    }

    public static Visibility StepVisible(int currentStep, int step)
        => currentStep == step ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NotStepZeroVisible(int currentStep)
        => currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NotLastStepVisible(int currentStep)
        => currentStep < 3 ? Visibility.Visible : Visibility.Collapsed;

    public static double StepOpacity(int currentStep, int step)
        => currentStep == step ? 1.0 : 0.5;

    public static Visibility BoolToVisible(bool value)
        => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility StringToVisible(string value)
        => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static bool NotBool(bool value) => !value;
}
