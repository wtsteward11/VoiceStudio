using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Views;

/// <summary>
/// First-run wizard that guides users through initial setup including:
/// - System requirements check
/// - Backend health verification
/// - Quick start guidance
/// </summary>
public sealed partial class FirstRunWizard : Window
{
  private int _currentStep = 1;
  private const int TotalSteps = 4;
  private readonly CancellationTokenSource _cts = new();
  private bool _backendRunning;

  public bool DontShowAgain => DontShowAgainCheckBox?.IsChecked ?? false;

  public FirstRunWizard()
  {
    this.InitializeComponent();

    // Set version text
    var version = typeof(FirstRunWizard).Assembly.GetName().Version;
    VersionText.Text = $"Version {version?.ToString(3) ?? "1.0.0"}";

    UpdateStepUI();
  }

  private void UpdateStepUI()
  {
    // Update step indicator
    StepIndicatorText.Text = _currentStep switch
    {
      1 => "Step 1 of 4: Welcome",
      2 => "Step 2 of 4: System Check",
      3 => "Step 3 of 4: Backend Connection",
      4 => "Step 4 of 4: Complete",
      _ => "Setup"
    };

    // Show/hide panels
    Step1Welcome.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
    Step2SystemCheck.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
    Step3BackendHealth.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
    Step4Complete.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

    // Update button visibility
    BackButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;

    // Update Next button text
    NextButton.Content = _currentStep switch
    {
      1 => "Get Started",
      4 => "Finish",
      _ => "Next"
    };

    // Trigger step-specific actions
    if (_currentStep == 2)
    {
      _ = RunSystemCheckAsync();
    }
    else if (_currentStep == 3)
    {
      _ = CheckBackendHealthAsync();
    }
  }

  private async void NextButton_Click(object sender, RoutedEventArgs e)
  {
    if (_currentStep < TotalSteps)
    {
      _currentStep++;
      UpdateStepUI();
    }
    else
    {
      // Save preference and close
      await SaveFirstRunCompleteAsync();
      this.Close();
    }
  }

  private void BackButton_Click(object sender, RoutedEventArgs e)
  {
    if (_currentStep > 1)
    {
      _currentStep--;
      UpdateStepUI();
    }
  }

  private async void SkipButton_Click(object sender, RoutedEventArgs e)
  {
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

  private async Task CheckBackendHealthAsync()
  {
    BackendProgress.IsActive = true;
    BackendWarningPanel.Visibility = Visibility.Collapsed;

    try
    {
      using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

      // Try to connect to backend
      SetCheckStatus(BackendIcon, BackendStatus, false, "Connecting...");

      try
      {
        var response = await httpClient.GetAsync("http://localhost:8001/health", _cts.Token);
        _backendRunning = response.IsSuccessStatusCode;

        SetCheckStatus(BackendIcon, BackendStatus, _backendRunning,
            _backendRunning ? "Connected" : "Not responding");

        if (_backendRunning)
        {
          // Check engines
          SetCheckStatus(EnginesIcon, EnginesStatus, false, "Checking...");
          await Task.Delay(500);

          try
          {
            var enginesResponse = await httpClient.GetAsync("http://localhost:8001/api/engines", _cts.Token);
            var enginesOk = enginesResponse.IsSuccessStatusCode;
            SetCheckStatus(EnginesIcon, EnginesStatus, enginesOk,
                enginesOk ? "Available" : "Error loading");
          }
          catch
          {
            SetCheckStatus(EnginesIcon, EnginesStatus, false, "Not available");
          }
        }
        else
        {
          SetCheckStatus(EnginesIcon, EnginesStatus, false, "Backend required");
          BackendWarningPanel.Visibility = Visibility.Visible;
        }
      }
      catch (HttpRequestException)
      {
        _backendRunning = false;
        SetCheckStatus(BackendIcon, BackendStatus, false, "Not running");
        SetCheckStatus(EnginesIcon, EnginesStatus, false, "Backend required");
        BackendWarningPanel.Visibility = Visibility.Visible;
      }
    }
    catch (Exception ex)
    {
      ErrorLogger.LogWarning($"Backend health check error: {ex.Message}");
      SetCheckStatus(BackendIcon, BackendStatus, false, "Error");
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
      // Try to start the backend
      var psi = new ProcessStartInfo
      {
        FileName = "python",
        Arguments = "-m uvicorn backend.api.main:app --host 0.0.0.0 --port 8000",
        UseShellExecute = true,
        CreateNoWindow = false
      };

      Process.Start(psi);

      // Wait a bit and re-check
      BackendStatus.Text = "Starting...";
      await Task.Delay(5000);
      await CheckBackendHealthAsync();
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
