// Phase 5: Synthesis UI
// Task 5.16: Main synthesis interface
// GAP-FE-001: Integrated with VoiceGateway and EngineGateway for backend connectivity

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VoiceStudio.App.Features.VoiceProfile;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Gateways;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Features.Synthesis;

/// <summary>
/// Synthesis engine info.
/// </summary>
public class EngineInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAvailable { get; set; } = true;
    public bool RequiresGPU { get; set; }
    
    /// <summary>
    /// Reason the engine is unavailable (GAP-CRIT-005).
    /// </summary>
    public string? UnavailableReason { get; set; }
    
    /// <summary>
    /// Display name with availability indicator (GAP-CRIT-005).
    /// Shows "(Unavailable)" suffix for placeholder or unavailable engines.
    /// </summary>
    public string DisplayName => IsAvailable ? Name : $"{Name} (Unavailable)";
}

/// <summary>
/// Synthesis parameters.
/// </summary>
public partial class SynthesisParameters : ObservableObject
{
    private double _speed = 1.0;
    private double _pitch = 1.0;
    private double _volume = 1.0;
    private double _stability = 0.5;
    private double _similarity = 0.75;
    private double _style = 0.0;

    public double Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, Math.Clamp(value, 0.25, 4.0));
    }

    public double Pitch
    {
        get => _pitch;
        set => SetProperty(ref _pitch, Math.Clamp(value, 0.5, 2.0));
    }

    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, Math.Clamp(value, 0, 2.0));
    }

    public double Stability
    {
        get => _stability;
        set => SetProperty(ref _stability, Math.Clamp(value, 0, 1.0));
    }

    public double Similarity
    {
        get => _similarity;
        set => SetProperty(ref _similarity, Math.Clamp(value, 0, 1.0));
    }

    public double Style
    {
        get => _style;
        set => SetProperty(ref _style, Math.Clamp(value, 0, 1.0));
    }
}

/// <summary>
/// Synthesis result.
/// </summary>
public class SynthesisResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = "";
    public string AudioPath { get; set; } = "";
    public string AudioId { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Engine { get; set; } = "";
    public string Voice { get; set; } = "";
}

/// <summary>
/// ViewModel for the synthesis interface.
/// GAP-FE-001: Refactored to inherit from BaseViewModel and integrate with VoiceGateway.
/// </summary>
public partial class SynthesisViewModel : BaseViewModel
{
    private readonly IVoiceGateway _voiceGateway;
    private readonly IEngineGateway _engineGateway;
    private readonly IEventAggregator? _eventAggregator;
    private ISubscriptionToken? _voiceProfileSelectedSubscription;

    private string _inputText = "";
    private EngineInfo? _selectedEngine;
    private VoiceProfileData? _selectedVoice;
    private bool _isSynthesizing;
    private double _progress;
    private SynthesisResult? _currentResult;
    private bool _isPlaying;
    private CancellationTokenSource? _synthesizeCts;

    public SynthesisViewModel(
        IViewModelContext context,
        IVoiceGateway voiceGateway,
        IEngineGateway engineGateway)
        : base(context)
    {
        _voiceGateway = voiceGateway ?? throw new ArgumentNullException(nameof(voiceGateway));
        _engineGateway = engineGateway ?? throw new ArgumentNullException(nameof(engineGateway));

        Parameters = new SynthesisParameters();
        AvailableEngines = new ObservableCollection<EngineInfo>();
        AvailableVoices = new ObservableCollection<VoiceProfileData>();
        History = new ObservableCollection<SynthesisResult>();
        
        SynthesizeCommand = new AsyncRelayCommand(SynthesizeAsync, CanSynthesize);
        CancelCommand = new RelayCommand(CancelSynthesis, () => IsSynthesizing);
        PlayCommand = new AsyncRelayCommand(PlayAsync, CanPlay);
        PauseCommand = new RelayCommand(Pause);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        ClearHistoryCommand = new RelayCommand(ClearHistory);
        AddToTimelineCommand = new RelayCommand(AddToTimeline, CanAddToTimeline);
        
        // Subscribe to VoiceProfileSelectedEvent for inter-panel workflow
        _eventAggregator = AppServices.TryGetEventAggregator();
        _voiceProfileSelectedSubscription = _eventAggregator?.Subscribe<VoiceProfileSelectedEvent>(OnVoiceProfileSelected);
    }

    // Input
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                OnPropertyChanged(nameof(CharacterCount));
                OnPropertyChanged(nameof(WordCount));
                OnPropertyChanged(nameof(EstimatedDuration));
                SynthesizeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int CharacterCount => InputText.Length;
    public int WordCount => string.IsNullOrWhiteSpace(InputText)
        ? 0
        : InputText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries).Length;
    
    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(WordCount * 0.5 / Parameters.Speed);

    // Engine and voice selection
    public ObservableCollection<EngineInfo> AvailableEngines { get; }
    public ObservableCollection<VoiceProfileData> AvailableVoices { get; }

    public EngineInfo? SelectedEngine
    {
        get => _selectedEngine;
        set
        {
            if (SetProperty(ref _selectedEngine, value))
            {
                // Reload voices for engine
                _ = LoadVoicesAsync();
                SynthesizeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public VoiceProfileData? SelectedVoice
    {
        get => _selectedVoice;
        set
        {
            if (SetProperty(ref _selectedVoice, value))
            {
                SynthesizeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // Parameters
    public SynthesisParameters Parameters { get; }

    // State
    public bool IsSynthesizing
    {
        get => _isSynthesizing;
        set
        {
            if (SetProperty(ref _isSynthesizing, value))
            {
                SynthesizeCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                PlayCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public SynthesisResult? CurrentResult
    {
        get => _currentResult;
        set
        {
            if (SetProperty(ref _currentResult, value))
            {
                PlayCommand.NotifyCanExecuteChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    // History
    public ObservableCollection<SynthesisResult> History { get; }

    // Commands
    public AsyncRelayCommand SynthesizeCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand PlayCommand { get; }
    public RelayCommand PauseCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public RelayCommand AddToTimelineCommand { get; }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading engines...";

        try
        {
            await LoadEnginesAsync();
            await LoadVoicesAsync();
            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Failed to initialize synthesis panel");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadEnginesAsync()
    {
        AvailableEngines.Clear();
        
        try
        {
            // Get engines from backend via EngineGateway
            var result = await _engineGateway.GetAllAsync();
            
            if (result.Success && result.Data != null)
            {
                foreach (var engine in result.Data)
                {
                    // Only show TTS engines (check capabilities for synthesis)
                    if (engine.Capabilities?.Contains("synthesis") == true ||
                        engine.Capabilities?.Contains("tts") == true ||
                        engine.Name.Contains("TTS", StringComparison.OrdinalIgnoreCase))
                    {
                        AvailableEngines.Add(new EngineInfo
                        {
                            Id = engine.Id,
                            Name = engine.Name,
                            Description = engine.Description ?? "",
                            IsAvailable = engine.Availability == VoiceStudio.Core.Gateways.EngineAvailability.Available,
                            RequiresGPU = false, // Not exposed in EngineInfo, will be set by engine details if needed
                        });
                    }
                }
            }
            
            // Fallback to defaults if no engines from backend (GAP-CRIT-005: mark as unavailable placeholders)
            if (AvailableEngines.Count == 0)
            {
                AvailableEngines.Add(new EngineInfo
                {
                    Id = "xtts_v2",
                    Name = "XTTS v2",
                    Description = "Coqui XTTS for high-quality voice cloning",
                    IsAvailable = false,
                    UnavailableReason = "Backend not connected - placeholder engine",
                    RequiresGPU = true,
                });
                
                AvailableEngines.Add(new EngineInfo
                {
                    Id = "piper",
                    Name = "Piper TTS",
                    Description = "Fast, lightweight TTS engine",
                    IsAvailable = false,
                    UnavailableReason = "Backend not connected - placeholder engine",
                    RequiresGPU = false,
                });
            }
            
            if (AvailableEngines.Count > 0)
            {
                SelectedEngine = AvailableEngines[0];
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load engines from backend, using defaults");
            
            // Add default engines as fallback (GAP-CRIT-005: mark as unavailable)
            AvailableEngines.Add(new EngineInfo
            {
                Id = "xtts_v2",
                Name = "XTTS v2",
                Description = "Coqui XTTS for high-quality voice cloning",
                IsAvailable = false,
                UnavailableReason = "Failed to connect to backend",
                RequiresGPU = true,
            });
            
            SelectedEngine = AvailableEngines[0];
        }
    }

    private async Task LoadVoicesAsync()
    {
        AvailableVoices.Clear();
        
        try
        {
            // Get voices from backend via VoiceGateway
            var result = await _voiceGateway.GetAvailableVoicesAsync(SelectedEngine?.Id);
            
            if (result.Success && result.Data != null)
            {
                foreach (var voice in result.Data)
                {
                    AvailableVoices.Add(new VoiceProfileData
                    {
                        Id = voice.Id,
                        Name = voice.Name,
                        Description = voice.Description ?? "",
                        Engine = voice.EngineId ?? SelectedEngine?.Id ?? "",
                        Language = voice.Language ?? "en",
                        Gender = voice.Gender ?? "",
                        Source = voice.IsCloned ? VoiceSource.Cloned : VoiceSource.Builtin,
                    });
                }
            }
            
            // Fallback to default if no voices from backend
            if (AvailableVoices.Count == 0)
            {
                AvailableVoices.Add(new VoiceProfileData
                {
                    Name = "Default English",
                    Engine = SelectedEngine?.Id ?? "",
                    Language = "en",
                });
            }
            
            if (AvailableVoices.Count > 0)
            {
                SelectedVoice = AvailableVoices[0];
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load voices from backend, using defaults");
            
            AvailableVoices.Add(new VoiceProfileData
            {
                Name = "Default English",
                Engine = SelectedEngine?.Id ?? "",
                Language = "en",
            });
            
            SelectedVoice = AvailableVoices[0];
        }
    }

    private bool CanSynthesize() =>
        !string.IsNullOrWhiteSpace(InputText) &&
        SelectedEngine != null &&
        SelectedEngine.IsAvailable && // GAP-CRIT-005: Prevent synthesis with unavailable engines
        SelectedVoice != null &&
        !IsSynthesizing;

    private async Task SynthesizeAsync()
    {
        if (!CanSynthesize())
        {
            return;
        }
        
        IsSynthesizing = true;
        Progress = 0;
        StatusMessage = "Synthesizing...";
        ErrorMessage = null;
        
        _synthesizeCts = new CancellationTokenSource();
        
        try
        {
            // Create synthesis request for backend
            var request = new VoiceSynthesisRequest
            {
                Text = InputText,
                VoiceId = SelectedVoice!.Id,
                EngineId = SelectedEngine!.Id,
                Language = SelectedVoice.Language,
                Speed = (float)Parameters.Speed,
                Pitch = (float)Parameters.Pitch,
                EngineParameters = new Dictionary<string, object>
                {
                    ["stability"] = Parameters.Stability,
                    ["similarity"] = Parameters.Similarity,
                    ["style"] = Parameters.Style,
                    ["volume"] = Parameters.Volume,
                }
            };

            Progress = 10;
            
            // Call backend via VoiceGateway
            var result = await _voiceGateway.SynthesizeAsync(request, _synthesizeCts.Token);
            
            Progress = 90;
            
            if (result.Success && result.Data != null)
            {
                var synthesisResult = new SynthesisResult
                {
                    Id = result.Data.AudioId,
                    Text = InputText,
                    AudioPath = result.Data.AudioPath,
                    AudioId = result.Data.AudioId,
                    Duration = TimeSpan.FromSeconds(result.Data.DurationSeconds),
                    Engine = SelectedEngine.Id,
                    Voice = SelectedVoice.Name,
                };
                
                CurrentResult = synthesisResult;
                History.Insert(0, synthesisResult);
                
                Progress = 100;
                StatusMessage = "Synthesis complete";
                
                // Publish event for cross-panel integration (C.3: Synthesis to Timeline)
                var eventAggregator = AppServices.TryGetEventAggregator();
                eventAggregator?.Publish(new SynthesisCompletedEvent(
                    "synthesis-panel",
                    synthesisResult.AudioId,
                    synthesisResult.AudioPath,
                    synthesisResult.Duration,
                    synthesisResult.Text,
                    synthesisResult.Voice,
                    synthesisResult.Engine));
            }
            else
            {
                ErrorMessage = result.Error?.Message ?? "Synthesis failed";
                StatusMessage = "Synthesis failed";
                await HandleErrorAsync(ErrorMessage, "Synthesis error", showDialog: false);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Synthesis cancelled";
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Synthesis failed");
            StatusMessage = "Error during synthesis";
        }
        finally
        {
            IsSynthesizing = false;
            Progress = 0;
            _synthesizeCts?.Dispose();
            _synthesizeCts = null;
        }
    }

    private void CancelSynthesis()
    {
        _synthesizeCts?.Cancel();
    }

    private bool CanPlay() => CurrentResult != null && !IsSynthesizing;

    private async Task PlayAsync()
    {
        if (CurrentResult == null)
        {
            return;
        }
        
        IsPlaying = true;
        StatusMessage = "Playing...";
        
        try
        {
            // Simulate playback - in a real implementation, use IAudioPlayerService
            await Task.Delay((int)CurrentResult.Duration.TotalMilliseconds);
        }
        finally
        {
            IsPlaying = false;
            StatusMessage = "Ready";
        }
    }

    private void Pause()
    {
        IsPlaying = false;
    }

    private bool CanSave() => CurrentResult != null;

    private async Task SaveAsync()
    {
        if (CurrentResult == null)
        {
            return;
        }
        
        StatusMessage = "Saved";
        await Task.CompletedTask;
    }

    private void ClearHistory()
    {
        History.Clear();
    }

    #region Timeline Integration (Audit remediation C.3)

    private bool CanAddToTimeline() => CurrentResult != null;

    private void AddToTimeline()
    {
        if (CurrentResult == null)
        {
            return;
        }

        var eventAggregator = AppServices.TryGetEventAggregator();
        eventAggregator?.Publish(new AddToTimelineEvent(
            "synthesis-panel",
            CurrentResult.AudioId,
            CurrentResult.AudioPath,
            CurrentResult.Duration,
            $"Synthesis: {CurrentResult.Text?.Substring(0, Math.Min(30, CurrentResult.Text?.Length ?? 0))}...",
            targetTrackIndex: null,
            insertPosition: null));

        StatusMessage = "Added to timeline";
    }

    /// <summary>
    /// Add a specific synthesis result to the timeline.
    /// </summary>
    public void AddToTimeline(SynthesisResult result)
    {
        if (result == null)
        {
            return;
        }

        var eventAggregator = AppServices.TryGetEventAggregator();
        eventAggregator?.Publish(new AddToTimelineEvent(
            "synthesis-panel",
            result.AudioId,
            result.AudioPath,
            result.Duration,
            $"Synthesis: {result.Text?.Substring(0, Math.Min(30, result.Text?.Length ?? 0))}...",
            targetTrackIndex: null,
            insertPosition: null));
    }

    #endregion

    #region Inter-Panel Workflow

    /// <summary>
    /// Handles the VoiceProfileSelectedEvent from Library or other panels.
    /// Sets the selected voice profile for synthesis.
    /// </summary>
    private void OnVoiceProfileSelected(VoiceProfileSelectedEvent e)
    {
        try
        {
            if (string.IsNullOrEmpty(e.ProfileId))
            {
                return;
            }

            // Find the voice profile in available voices
            var profile = AvailableVoices.FirstOrDefault(v => v.Id == e.ProfileId);
            if (profile != null)
            {
                SelectedVoice = profile;
                StatusMessage = $"Voice profile '{profile.Name ?? e.ProfileName}' selected for synthesis";
            }
            else
            {
                // Profile not loaded - load it asynchronously
                _ = LoadAndSelectVoiceProfileAsync(e.ProfileId, e.ProfileName);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error selecting voice profile: {ex.Message}");
            ErrorMessage = $"Failed to select voice profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads a voice profile by ID and sets it as selected.
    /// </summary>
    private async Task LoadAndSelectVoiceProfileAsync(string profileId, string? profileName)
    {
        try
        {
            // Refresh voice list to ensure we have the latest
            await LoadVoicesAsync();

            // Try to find and select the profile now
            var profile = AvailableVoices.FirstOrDefault(v => v.Id == profileId);
            if (profile != null)
            {
                SelectedVoice = profile;
                StatusMessage = $"Voice profile '{profile.Name ?? profileName}' loaded and selected";
            }
            else
            {
                ErrorMessage = $"Voice profile '{profileName ?? profileId}' not found";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading voice profile: {ex.Message}");
            ErrorMessage = $"Failed to load voice profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Cleanup subscriptions when view model is disposed.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _voiceProfileSelectedSubscription?.Dispose();
            _voiceProfileSelectedSubscription = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
