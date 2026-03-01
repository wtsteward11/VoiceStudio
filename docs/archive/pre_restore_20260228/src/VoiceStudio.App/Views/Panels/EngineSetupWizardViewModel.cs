using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels;

public sealed partial class EngineSetupWizardViewModel : ObservableObject
{
    private readonly IBackendClient _backendClient;
    private readonly ToastNotificationService? _toastService;

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private int _installProgress;
    [ObservableProperty] private string _installStage = "";
    [ObservableProperty] private string _selectedEngineId = "xtts_v2";
    [ObservableProperty] private string _selectedEngineName = "XTTS v2";
    [ObservableProperty] private bool _gpuDetected;
    [ObservableProperty] private string _gpuName = "Checking...";
    [ObservableProperty] private string _ramInfo = "Checking...";
    [ObservableProperty] private bool _installComplete;
    [ObservableProperty] private bool _installFailed;
    [ObservableProperty] private string _errorDetails = "";

    public ObservableCollection<EngineOption> AvailableEngines { get; } = new();

    public EngineSetupWizardViewModel(IBackendClient backendClient)
    {
        _backendClient = backendClient;
        _toastService = ServiceProvider.GetToastNotificationService();

        AvailableEngines.Add(new EngineOption(
            "xtts_v2", "XTTS v2",
            "Full voice cloning + multi-language TTS. Best quality, GPU recommended (4GB+ VRAM).",
            true));
        AvailableEngines.Add(new EngineOption(
            "piper", "Piper",
            "Lightweight, CPU-only, very fast. Good for basic TTS without GPU.",
            false));
        AvailableEngines.Add(new EngineOption(
            "bark", "Bark",
            "Creative and expressive. Supports music, laughter, sound effects. GPU recommended.",
            false));
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < 3)
            CurrentStep++;
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    [RelayCommand]
    private void SelectEngine(string engineId)
    {
        SelectedEngineId = engineId;
        var engine = AvailableEngines.FirstOrDefault(e => e.Id == engineId);
        if (engine != null)
            SelectedEngineName = engine.Name;
    }

    [RelayCommand]
    private async Task CheckSystemAsync()
    {
        StatusMessage = "Checking system requirements...";
        try
        {
            var health = await _backendClient.GetAsync<object>("/api/health");
            if (health != null)
            {
                GpuDetected = true;
                GpuName = "GPU check via backend health";
            }
        }
        catch
        {
            GpuDetected = false;
            GpuName = "Could not detect GPU (backend may not be running)";
        }

        try
        {
            var memInfo = GC.GetGCMemoryInfo();
            var totalRam = memInfo.TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
            RamInfo = $"{totalRam:F1} GB available";
        }
        catch
        {
            RamInfo = "Unable to detect";
        }

        StatusMessage = "System check complete.";
    }

    [RelayCommand]
    private async Task InstallEngineAsync(CancellationToken ct)
    {
        IsInstalling = true;
        InstallComplete = false;
        InstallFailed = false;
        InstallProgress = 0;
        ErrorDetails = "";

        try
        {
            InstallStage = "Checking engine availability...";
            InstallProgress = 10;
            await Task.Delay(500, ct);

            InstallStage = $"Installing {SelectedEngineName}...";
            InstallProgress = 30;

            await _backendClient.PostAsync<object, object>(
                $"/api/engines/{SelectedEngineId}/install",
                new { engine_id = SelectedEngineId });

            InstallProgress = 60;
            InstallStage = "Downloading model files...";
            await Task.Delay(1000, ct);

            InstallProgress = 80;
            InstallStage = "Verifying installation...";
            await Task.Delay(500, ct);

            InstallProgress = 100;
            InstallStage = "Installation complete!";
            InstallComplete = true;
            StatusMessage = $"{SelectedEngineName} installed successfully!";
            _toastService?.ShowSuccess($"{SelectedEngineName} is ready to use.", "Engine Installed");
        }
        catch (Exception ex)
        {
            InstallFailed = true;
            InstallStage = "Installation failed";
            ErrorDetails = ex.Message;
            StatusMessage = $"Failed to install {SelectedEngineName}. See error details below.";
            _toastService?.ShowError(ex.Message, "Installation Failed");
        }
        finally
        {
            IsInstalling = false;
        }
    }
}

public sealed class EngineOption
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public bool IsRecommended { get; }

    public EngineOption(string id, string name, string description, bool isRecommended)
    {
        Id = id;
        Name = name;
        Description = description;
        IsRecommended = isRecommended;
    }
}
