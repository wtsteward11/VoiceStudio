using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Views.Dialogs;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views;

/// <summary>
/// First-run wizard that guides users through initial setup including:
/// - System requirements check
/// - Model registration check
/// - Backend health verification
/// - API key orientation and quick start guidance
/// </summary>
public sealed partial class FirstRunWizard : Window
{
  /// <summary>Unpackaged settings key for wizard step resume (GAP-063).</summary>
  public const string WizardCurrentStepKey = "WizardCurrentStep";

  private int _currentStep = 1;
  private const int TotalSteps = 5;
  private readonly CancellationTokenSource _cts = new();
  private readonly bool _isFirstRun;
  private readonly OnboardingWizardService? _onboardingService;

  /// <summary>
  /// GAP-X02: Tracks whether the wizard was completed successfully.
  /// True if user clicked Finish or Skip, false if user closed window early.
  /// </summary>
  public bool WasCompleted { get; private set; }

  public bool DontShowAgain => DontShowAgainCheckBox?.IsChecked ?? false;

  /// <param name="isFirstRun">False when the wizard is shown again via "show on startup" after a completed first run; controls app exit on cancel.</param>
  public FirstRunWizard(bool isFirstRun = true)
  {
    this.InitializeComponent();
    _isFirstRun = isFirstRun;
    _onboardingService = AppServices.GetOnboardingWizardService();

    var savedStep = UnpackagedSettingsHelper.GetValue<int>(WizardCurrentStepKey, 1);
    _currentStep = Math.Clamp(savedStep, 1, TotalSteps);
    SyncOnboardingProgress();

    // Set version text
    var version = typeof(FirstRunWizard).Assembly.GetName().Version;
    VersionText.Text = $"Version {version?.ToString(3) ?? "1.0.0"}";

    this.Closed += FirstRunWizard_Closed;

    UpdateStepUI();
  }

  private void FirstRunWizard_Closed(object sender, WindowEventArgs e)
  {
    _cts.Cancel();
  }

  private void SyncOnboardingProgress()
  {
    var progress = _onboardingService?.GetProgress();
    if (progress != null)
    {
      progress.CurrentStepId = $"first_run_step_{_currentStep}";
    }
  }

  private void SaveStepProgress()
  {
    try
    {
      UnpackagedSettingsHelper.SetValue(WizardCurrentStepKey, _currentStep);
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Failed to save wizard step: {ex.Message}");
    }
    SyncOnboardingProgress();
  }

  private void UpdateStepUI()
  {
    // Update step indicator
    StepIndicatorText.Text = _currentStep switch
    {
      1 => "Step 1 of 5: Welcome",
      2 => "Step 2 of 5: System Check",
      3 => "Step 3 of 5: Model Readiness",
      4 => "Step 4 of 5: Backend Connection",
      5 => "Step 5 of 5: API Keys & Finish",
      _ => "Setup"
    };

    // Show/hide panels
    Step1Welcome.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
    Step2SystemCheck.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
    Step3ModelReadiness.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
    Step4BackendHealth.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
    Step5ApiComplete.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;

    // Update button visibility
    BackButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;

    // Update Next button text
    NextButton.Content = _currentStep switch
    {
      1 => "Get Started",
      5 => "Finish",
      _ => "Next"
    };

    HelpButton.Visibility = _currentStep is 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
    if (_currentStep == 3)
    {
      HelpOverlay.Title = "Model Readiness";
      HelpOverlay.HelpText =
          "VoiceStudio requires at least one voice model to synthesize speech. " +
          "Open the Model Manager via Settings to download or import models.";
    }
    else if (_currentStep == 4)
    {
      HelpOverlay.Title = "Backend Health";
      HelpOverlay.HelpText =
          "VoiceStudio's Python backend must be running before synthesis. " +
          "Click 'Start Backend' to launch it. If it fails, ensure Python 3.11 is installed " +
          "and the engines folder is intact.";
    }

    // Trigger step-specific actions
    if (_currentStep == 2)
    {
      _ = RunSystemCheckAsync();
    }
    else if (_currentStep == 3)
    {
      _ = CheckModelReadinessAsync();
    }
    else if (_currentStep == 4)
    {
      _ = CheckBackendHealthAsync();
    }
  }

  private async void WizardRootGrid_Loaded(object sender, RoutedEventArgs e)
  {
    await TryShowTelemetryConsentOnFirstStepAsync();
  }

  private async Task TryShowTelemetryConsentOnFirstStepAsync()
  {
    if (_currentStep != 1)
    {
      return;
    }

    if (UnpackagedSettingsHelper.GetValue<bool>("TelemetryConsentShown", false))
    {
      return;
    }

    if (WizardRootGrid.XamlRoot == null)
    {
      return;
    }

    try
    {
      var consent = new TelemetryConsentDialog
      {
        XamlRoot = WizardRootGrid.XamlRoot,
      };
      await consent.ShowAsync();
      UnpackagedSettingsHelper.SetValue("TelemetryConsentShown", true);
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Telemetry consent dialog failed: {ex.Message}");
    }
  }

  private void HelpButton_Click(object sender, RoutedEventArgs e)
  {
    HelpOverlay.Show();
  }

  private async void OpenModelManagerInfoButton_Click(object sender, RoutedEventArgs e)
  {
    if (WizardRootGrid.XamlRoot == null)
    {
      return;
    }

    try
    {
      var dialog = new ContentDialog
      {
        Title = "Model Manager",
        Content = "After you complete this wizard, use the navigation rail → Train → Model Manager to download and register voice models.",
        CloseButtonText = "OK",
        XamlRoot = WizardRootGrid.XamlRoot,
      };
      await dialog.ShowAsync();
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Model Manager info dialog failed: {ex.Message}");
    }
  }

  private async void NextButton_Click(object sender, RoutedEventArgs e)
  {
    if (_currentStep < TotalSteps)
    {
      _currentStep++;
      SaveStepProgress();
      UpdateStepUI();
    }
    else
    {
      // GAP-X02: Mark wizard as completed and save preference
      WasCompleted = true;
      await SaveFirstRunCompleteAsync();
      this.Close();
    }
  }

  private void BackButton_Click(object sender, RoutedEventArgs e)
  {
    if (_currentStep > 1)
    {
      _currentStep--;
      SaveStepProgress();
      UpdateStepUI();
    }
  }

  private async void SkipButton_Click(object sender, RoutedEventArgs e)
  {
    // GAP-X02: Mark wizard as completed (skipped counts as completed)
    WasCompleted = true;
    await SaveFirstRunCompleteAsync();
    this.Close();
  }

  private async Task SaveFirstRunCompleteAsync()
  {
    try
    {
      // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
      UnpackagedSettingsHelper.SetValue("FirstRunComplete", true);
      UnpackagedSettingsHelper.SetValue("ShowWizardOnStartup", !DontShowAgain);
      UnpackagedSettingsHelper.SetValue(WizardCurrentStepKey, 1);

      await Task.CompletedTask;
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Failed to save first-run settings: {ex.Message}");
    }
  }

  private async Task RunSystemCheckAsync()
  {
    SystemCheckProgress.IsActive = true;
    GpuFallbackPanel.Visibility = Visibility.Collapsed;

    try
    {
      // Check .NET Runtime (always passes since we're running)
      await Task.Delay(300);
      SetCheckStatus(DotNetIcon, DotNetStatus, true, "Installed");

      // Check Python
      await Task.Delay(300);
      var pythonInstalled = await CheckPythonInstalledAsync();
      SetCheckStatus(PythonIcon, PythonStatus, pythonInstalled,
          pythonInstalled ? "Installed" : "Not found (optional)");

      // Check GPU
      await Task.Delay(300);
      var (gpuFound, gpuName) = await CheckGpuAsync();
      SetCheckStatus(GpuIcon, GpuStatus, gpuFound,
          gpuFound ? gpuName : "Not detected (CPU mode)");
      if (!gpuFound)
      {
        GpuFallbackPanel.Visibility = Visibility.Visible;
      }

      // Check Disk Space
      await Task.Delay(300);
      var (diskOk, diskSpace) = CheckDiskSpace();
      SetCheckStatus(DiskIcon, DiskStatus, diskOk, diskSpace);

      // Check RAM
      await Task.Delay(300);
      var (ramOk, ramSize) = CheckRam();
      SetCheckStatus(RamIcon, RamStatus, ramOk, ramSize);
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"System check error: {ex.Message}");
    }
    finally
    {
      SystemCheckProgress.IsActive = false;
    }
  }

  private void SetCheckStatus(FontIcon icon, TextBlock status, bool success, string message)
  {
    icon.Glyph = success ? "\uE73E" : "\uE7BA"; // Checkmark or Warning
    icon.Foreground = new SolidColorBrush(success ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Orange);
    status.Text = message;
  }

  private void SetModelStatus(bool success, string message)
  {
    ModelIcon.Glyph = success ? "\uE73E" : "\uE7BA";
    ModelIcon.Foreground = new SolidColorBrush(success ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Orange);
    ModelStatus.Text = message;
  }

  private async Task<bool> CheckPythonInstalledAsync()
  {
    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = "python",
        Arguments = "--version",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      if (process != null)
      {
        await process.WaitForExitAsync(_cts.Token);
        return process.ExitCode == 0;
      }
    }
    catch (Exception ex)
    {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "FirstRunWizard.Task");
    }
    return false;
  }

  private async Task<(bool found, string name)> CheckGpuAsync()
  {
    try
    {
      // Try nvidia-smi to check for NVIDIA GPU
      var psi = new ProcessStartInfo
      {
        FileName = "nvidia-smi",
        Arguments = "--query-gpu=name --format=csv,noheader",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      if (process != null)
      {
        var output = await process.StandardOutput.ReadLineAsync(_cts.Token);
        await process.WaitForExitAsync(_cts.Token);

        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
          return (true, output.Trim());
        }
      }
    }
    catch (Exception ex)
    {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "FirstRunWizard.Task");
    }
    return (false, "Not detected");
  }

  private (bool ok, string message) CheckDiskSpace()
  {
    try
    {
      var drive = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ?? "C");
      var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
      var isOk = freeGb >= 10;
      return (isOk, $"{freeGb:F1} GB free");
    }
    catch
    {
      return (true, "Unknown");
    }
  }

  private (bool ok, string message) CheckRam()
  {
    try
    {
      var totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
      var isOk = totalRam >= 8;
      return (isOk, $"{totalRam:F1} GB");
    }
    catch
    {
      return (true, "Unknown");
    }
  }

  private async Task CheckModelReadinessAsync()
  {
    ModelCheckProgress.IsActive = true;
    ModelDownloadCtaPanel.Visibility = Visibility.Collapsed;

    try
    {
      var client = AppServices.GetService<IModelManagerClient>();
      if (client == null)
      {
        SetModelStatus(false, "Model manager unavailable");
        ModelDownloadCtaPanel.Visibility = Visibility.Visible;
        return;
      }

      var models = await client.GetModelsAsync(engine: null, cancellationToken: _cts.Token).ConfigureAwait(true);
      var hasModels = models != null && models.Count > 0;
      SetModelStatus(hasModels, hasModels
          ? $"{models!.Count} model(s) registered"
          : "No models registered yet");
      ModelDownloadCtaPanel.Visibility = hasModels ? Visibility.Collapsed : Visibility.Visible;
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Model readiness check error: {ex.Message}");
      SetModelStatus(false, "Could not reach backend for models");
      ModelDownloadCtaPanel.Visibility = Visibility.Visible;
    }
    finally
    {
      ModelCheckProgress.IsActive = false;
    }
  }

  private async Task CheckBackendHealthAsync()
  {
    BackendProgress.IsActive = true;
    BackendWarningPanel.Visibility = Visibility.Collapsed;

    try
    {
      var diagnostics = AppServices.GetService<IDiagnosticsClient>();
      var enginesClient = AppServices.GetService<IEnginesClient>();

      if (diagnostics == null)
      {
        SetCheckStatus(BackendIcon, BackendStatus, false, "Diagnostics unavailable");
        SetCheckStatus(EnginesIcon, EnginesStatus, false, "Unavailable");
        BackendWarningPanel.Visibility = Visibility.Visible;
        return;
      }

      SetCheckStatus(BackendIcon, BackendStatus, false, "Connecting...");

      bool healthy;
      try
      {
        healthy = await diagnostics.CheckHealthAsync(_cts.Token).ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Backend health check failed: {ex.Message}");
        healthy = false;
      }

      SetCheckStatus(BackendIcon, BackendStatus, healthy,
          healthy ? "Connected" : "Not responding");

      if (!healthy)
      {
        SetCheckStatus(EnginesIcon, EnginesStatus, false, "Backend required");
        BackendWarningPanel.Visibility = Visibility.Visible;
      }
      else if (enginesClient != null)
      {
        SetCheckStatus(EnginesIcon, EnginesStatus, false, "Checking...");
        await Task.Delay(300);
        try
        {
          var engines = await enginesClient.GetEnginesAsync(_cts.Token).ConfigureAwait(true);
          var enginesOk = engines != null && engines.Count > 0;
          SetCheckStatus(EnginesIcon, EnginesStatus, enginesOk,
              enginesOk ? "Available" : "None listed");
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning($"Engines list check failed: {ex.Message}");
          SetCheckStatus(EnginesIcon, EnginesStatus, false, "Not available");
        }
      }
      else
      {
        SetCheckStatus(EnginesIcon, EnginesStatus, false, "Engines client unavailable");
      }
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Backend health check error: {ex.Message}");
      SetCheckStatus(BackendIcon, BackendStatus, false, "Error");
      SetCheckStatus(EnginesIcon, EnginesStatus, false, "Unavailable");
      BackendWarningPanel.Visibility = Visibility.Visible;
    }
    finally
    {
      BackendProgress.IsActive = false;
    }
  }

  private async void StartBackendButton_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      var mgr = AppServices.GetService<BackendProcessManager>();
      if (mgr == null)
      {
        BackendStatus.Text = "Backend manager unavailable";
        return;
      }

      BackendStatus.Text = "Starting...";
      await mgr.EnsureBackendRunningAsync(_cts.Token).ConfigureAwait(true);
      await Task.Delay(2000);
      await CheckBackendHealthAsync().ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Failed to start backend: {ex.Message}");
      BackendStatus.Text = "Failed to start";
    }
  }

  public static Task<bool> ShouldShowWizardAsync()
  {
    try
    {
      // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
      var firstRunComplete = UnpackagedSettingsHelper.GetValue<bool>("FirstRunComplete", false);

      if (firstRunComplete)
      {
        // Check if user wants to see wizard on startup
        var showOnStartup = UnpackagedSettingsHelper.GetValue<bool>("ShowWizardOnStartup", false);
        return Task.FromResult(showOnStartup);
      }
      return Task.FromResult(true); // First run
    }
    catch
    {
      return Task.FromResult(false);
    }
  }
}
