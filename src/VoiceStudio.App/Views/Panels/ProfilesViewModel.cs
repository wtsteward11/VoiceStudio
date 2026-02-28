using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using Windows.Storage;
using Windows.Storage.Pickers;
using VoiceStudio.App.Logging;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;

namespace VoiceStudio.App.Views.Panels
{
  // GAP-005: Updated to inherit from BaseViewModel for standardized error handling
  // Backend-Frontend Integration Plan - Phase 2: Implements state persistence.
  public partial class ProfilesViewModel : BaseViewModel, IPanelView, IPanelStatePersistable
  {
    private readonly IBackendClient _backendClient;
    private readonly IProfilesUseCase _profilesUseCase;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;
    private readonly IEventAggregator? _eventAggregator;
    private readonly IContextManager? _contextManager;

    public string PanelId => "profiles";
    public string DisplayName => ResourceHelper.GetString("Panel.Profiles.DisplayName", "Profiles");
    public PanelRegion Region => PanelRegion.Left;

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> profiles = new();

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> filteredProfiles = new();

    [ObservableProperty]
    private VoiceProfile? selectedProfile;

    // Search and filter properties
    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string? selectedLanguage;

    [ObservableProperty]
    private string? selectedEmotion;

    [ObservableProperty]
    private string? selectedQualityRange;

    [ObservableProperty]
    private ObservableCollection<string> availableLanguages = new();

    [ObservableProperty]
    private ObservableCollection<string> availableEmotions = new();

    [ObservableProperty]
    private ObservableCollection<string> availableQualityRanges = new()
        {
            ResourceHelper.GetString("Filter.All", "All"),
            ResourceHelper.GetString("Filter.QualityHigh", "High (4.0+)"),
            ResourceHelper.GetString("Filter.QualityGood", "Good (3.0-4.0)"),
            ResourceHelper.GetString("Filter.QualityFair", "Fair (2.0-3.0)"),
            ResourceHelper.GetString("Filter.QualityLow", "Low (<2.0)")
        };

    [ObservableProperty]
    private int totalProfiles;

    [ObservableProperty]
    private int filteredCount;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isPreviewing;

    [ObservableProperty]
    private bool canPreview;

    [ObservableProperty]
    private QualityMetrics? previewQualityMetrics;

    [ObservableProperty]
    private bool hasPreviewQualityMetrics;

    [ObservableProperty]
    private double? previewQualityScore;

    // View and sort state (Phase 2 - Backend-Frontend Integration Plan)
    [ObservableProperty]
    private string viewMode = "Grid";

    [ObservableProperty]
    private string? sortColumn;

    [ObservableProperty]
    private bool sortDescending;

    // Reference audio enhancement
    [ObservableProperty]
    private bool isEnhancing;

    [ObservableProperty]
    private ReferenceAudioPreprocessResponse? enhancementResult;

    [ObservableProperty]
    private bool hasEnhancementResult;

    [ObservableProperty]
    private bool autoEnhance = true;

    [ObservableProperty]
    private bool selectOptimalSegments = true;

    [ObservableProperty]
    private bool isPlayingEnhanced;

    // Quality history (IDEA 30)
    [ObservableProperty]
    private ObservableCollection<QualityHistoryEntry> qualityHistory = new();

    [ObservableProperty]
    private QualityTrends? qualityTrends;

    [ObservableProperty]
    private bool isLoadingQualityHistory;

    [ObservableProperty]
    private string selectedTimeRange = "30d";

    [ObservableProperty]
    private ObservableCollection<string> availableTimeRanges = new() { "7d", "30d", "90d", "1y", "all" };

    [ObservableProperty]
    private bool hasQualityHistory;

    // Quality Degradation Detection (IDEA 56)
    [ObservableProperty]
    private QualityDegradationResponse? qualityDegradation;

    [ObservableProperty]
    private ObservableCollection<QualityDegradationAlert> qualityDegradationAlerts = new();

    [ObservableProperty]
    private QualityBaseline? qualityBaseline;

    [ObservableProperty]
    private bool isLoadingDegradation;

    [ObservableProperty]
    private bool hasQualityDegradation;

    [ObservableProperty]
    private int degradationTimeWindowDays = 7;

    // Multi-select support
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;

    // Cancellation token source for profile change async operations
    private CancellationTokenSource? _profileChangeCts;

    // GAP-I15: Disposal token for fire-and-forget operations
    private readonly CancellationTokenSource _disposalCts = new();

    [ObservableProperty]
    private int selectedCount;

    [ObservableProperty]
    private bool hasMultipleSelection;

    public bool HasProfiles => FilteredProfiles?.Count > 0;

    public bool IsProfileSelected(string profileId) => _multiSelectState?.SelectedIds.Contains(profileId) ?? false;

    // Default preview text for voice profiles
    private const string DEFAULT_PREVIEW_TEXT = "Hello, this is a preview of this voice profile.";

    // Cache for preview audio (profileId -> audioUrl)
    private readonly Dictionary<string, string> _previewCache = new();
    private readonly Dictionary<string, QualityMetrics?> _previewQualityCache = new();
    private readonly Dictionary<string, double> _previewQualityScoreCache = new();

    private sealed class ProfileImportData
    {
      public string? Name { get; set; }
      public string? Language { get; set; }
      public string? Emotion { get; set; }
      public List<string>? Tags { get; set; }
    }

    private sealed class ProfileExportBundle
    {
      public string ExportedAt { get; set; } = string.Empty;
      public string Version { get; set; } = "1.0";
      public List<ProfileImportData> Profiles { get; set; } = new();
    }

    public ProfilesViewModel(
      IBackendClient backendClient,
      IProfilesUseCase profilesUseCase,
      IAudioPlayerService audioPlayer,
      MultiSelectService multiSelectService,
      ToastNotificationService? toastNotificationService = null,
      UndoRedoService? undoRedoService = null,
      IErrorPresentationService? errorService = null,
      IErrorLoggingService? logService = null)
        : base(AppServices.GetViewModelContext())
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _profilesUseCase = profilesUseCase ?? throw new ArgumentNullException(nameof(profilesUseCase));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
      _multiSelectService = multiSelectService ?? throw new ArgumentNullException(nameof(multiSelectService));
      _multiSelectState = _multiSelectService.GetState(PanelId);

      _toastNotificationService = toastNotificationService;
      _undoRedoService = undoRedoService;

      _errorService = errorService;
      _logService = logService;
      _eventAggregator = AppServices.TryGetEventAggregator();
      _contextManager = AppServices.TryGetContextManager();

      LoadProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProfiles");
        await LoadProfilesAsync(ct);
      });

      // GAP-I15: Propagate cancellation token from command
      CreateProfileCommand = new EnhancedAsyncRelayCommand<string>(async (name, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateProfile");
        await CreateProfileAsync(name, ct);
      });

      // GAP-I15: Propagate cancellation token from command
      DeleteProfileCommand = new EnhancedAsyncRelayCommand<string>(async (profileId, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteProfile");
        await DeleteProfileAsync(profileId, ct);
      }, (string? profileId) => SelectedProfile != null && !IsLoading);

      // GAP-I15: Propagate cancellation token from command
      PreviewProfileCommand = new EnhancedAsyncRelayCommand<string>(async (profileId, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviewProfile");
        await PreviewProfileAsync(profileId, ct);
      }, (string? profileId) => CanPreview && !IsLoading && !IsPreviewing);

      StopPreviewCommand = new RelayCommand(StopPreview, () => IsPreviewing || _audioPlayer.IsPlaying);

      // Enhancement commands
      EnhanceReferenceAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("EnhanceReferenceAudio");
        await EnhanceReferenceAudioAsync(ct);
      }, () => SelectedProfile != null && !IsEnhancing && !IsLoading);

      PreviewEnhancedAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviewEnhancedAudio");
        await PreviewEnhancedAudioAsync(ct);
      }, () => HasEnhancementResult && !IsPlayingEnhanced);

      StopEnhancedPreviewCommand = new RelayCommand(StopEnhancedPreview, () => IsPlayingEnhanced);

      ApplyEnhancedAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyEnhancedAudio");
        await ApplyEnhancedAudioAsync(ct);
      }, () => HasEnhancementResult && !IsLoading);

      // Multi-select commands
      SelectAllCommand = new RelayCommand(SelectAll, () => HasProfiles);

      // Initialize filtered profiles
      FilteredProfiles = new ObservableCollection<VoiceProfile>();
      ClearSelectionCommand = new RelayCommand(ClearSelection);

      DeleteSelectedCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSelected");
        await DeleteSelectedAsync(ct);
      }, () => SelectedCount > 0);

      // GAP-B18: Added command for batch export - enables direct XAML command binding
      ExportSelectedCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportSelected");
        _logService?.LogInfo($"Batch export requested for {SelectedCount} profiles", "ProfilesViewModel");
        await ExportSelectedProfilesAsync();
      }, () => SelectedCount > 0);

      // Quality history commands
      LoadQualityHistoryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadQualityHistory");
        await LoadQualityHistoryAsync(ct);
      }, () => SelectedProfile != null && !IsLoadingQualityHistory);

      LoadQualityTrendsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadQualityTrends");
        await LoadQualityTrendsAsync(ct);
      }, () => SelectedProfile != null && !IsLoadingQualityHistory);

      // Quality degradation detection commands (IDEA 56)
      CheckQualityDegradationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CheckQualityDegradation");
        await CheckQualityDegradationAsync(ct);
      }, () => SelectedProfile != null && !IsLoadingDegradation);

      LoadQualityBaselineCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadQualityBaseline");
        await LoadQualityBaselineAsync(ct);
      }, () => SelectedProfile != null && !IsLoadingDegradation);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateSelectionProperties();
          OnPropertyChanged(nameof(SelectedCount));
          OnPropertyChanged(nameof(HasMultipleSelection));
        }
      };

      // GAP-B05: Subscribe to ProfileCreatedEvent to refresh list when new profiles
      // are created in other panels (e.g., TrainingView, VoiceCloningWizard)
      _eventAggregator?.Subscribe<ProfileCreatedEvent>(OnProfileCreatedRefresh);
    }

    /// <summary>
    /// Handles ProfileCreatedEvent by refreshing the profiles list.
    /// GAP-B05: Ensures newly trained/cloned profiles appear without manual refresh.
    /// </summary>
    private void OnProfileCreatedRefresh(ProfileCreatedEvent evt)
    {
      // Skip if this panel published the event (we already updated)
      if (evt.SourcePanelId == PanelId)
        return;

      System.Diagnostics.Debug.WriteLine(
        $"[ProfilesViewModel] ProfileCreatedEvent received from {evt.SourcePanelId}: {evt.ProfileId}");

      // GAP-I15: Refresh profile list on the UI thread using disposal token
      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(async () =>
      {
        await LoadProfilesAsync(_disposalCts.Token);
      });
    }

    public EnhancedAsyncRelayCommand LoadProfilesCommand { get; }
    public EnhancedAsyncRelayCommand<string> CreateProfileCommand { get; }
    public EnhancedAsyncRelayCommand<string> DeleteProfileCommand { get; }
    public EnhancedAsyncRelayCommand<string> PreviewProfileCommand { get; }
    public IRelayCommand StopPreviewCommand { get; }

    // Enhancement commands
    public EnhancedAsyncRelayCommand EnhanceReferenceAudioCommand { get; }
    public EnhancedAsyncRelayCommand PreviewEnhancedAudioCommand { get; }
    public IRelayCommand StopEnhancedPreviewCommand { get; }
    public EnhancedAsyncRelayCommand ApplyEnhancedAudioCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public EnhancedAsyncRelayCommand DeleteSelectedCommand { get; }
    // GAP-B18: Added ExportSelectedCommand for command binding pattern
    public EnhancedAsyncRelayCommand ExportSelectedCommand { get; }

    // Quality degradation detection commands (IDEA 56)
    public EnhancedAsyncRelayCommand CheckQualityDegradationCommand { get; }
    public EnhancedAsyncRelayCommand LoadQualityBaselineCommand { get; }

    // Quality history commands
    public EnhancedAsyncRelayCommand LoadQualityHistoryCommand { get; }
    public EnhancedAsyncRelayCommand LoadQualityTrendsCommand { get; }

    // GAP-I15: Added CancellationToken parameter for proper propagation
    public async Task ImportProfilesAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");

        var file = await picker.PickSingleFileAsync();
        if (file == null)
        {
          return;
        }

        var json = await FileIO.ReadTextAsync(file);
        var importProfiles = ParseProfileImports(json, out var parseError);
        if (!string.IsNullOrWhiteSpace(parseError))
        {
          ErrorMessage = parseError;
          _toastNotificationService?.ShowError(
              ResourceHelper.GetString("Toast.Title.ImportFailed", "Import Failed"),
              parseError);
          return;
        }

        if (importProfiles.Count == 0)
        {
          ErrorMessage = ResourceHelper.GetString("Profile.ImportEmpty", "Import file contains no profiles.");
          return;
        }

        var createdProfiles = new List<VoiceProfile>();
        foreach (var importProfile in importProfiles)
        {
          var name = importProfile.Name?.Trim();
          if (string.IsNullOrWhiteSpace(name))
          {
            continue;
          }

          var language = string.IsNullOrWhiteSpace(importProfile.Language) ? "en" : importProfile.Language!.Trim();
          var emotion = string.IsNullOrWhiteSpace(importProfile.Emotion) ? null : importProfile.Emotion!.Trim();
          var tags = importProfile.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToList();

          var profile = await _profilesUseCase.CreateAsync(
              name,
              language,
              emotion,
              tags,
              cancellationToken);
          if (profile != null)
          {
            Profiles.Add(profile);
            createdProfiles.Add(profile);
          }
        }

        UpdateAvailableFilters();
        ApplyFilters();

        if (createdProfiles.Count > 0)
        {
          SelectedProfile = createdProfiles.Last();
          var message = string.Format(
              ResourceHelper.GetString("Profile.ImportSuccess", "{0} profiles imported"),
              createdProfiles.Count);
          _toastNotificationService?.ShowSuccess(
              message,
              ResourceHelper.GetString("Toast.Title.ImportComplete", "Import Complete"));
        }
        else
        {
          ErrorMessage = ResourceHelper.GetString("Profile.ImportEmpty", "Import file contains no profiles.");
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        var message = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = string.Format(
            ResourceHelper.GetString("Profile.ImportFailed", "Import failed: {0}"),
            message);
        _logService?.LogError(ex, "ImportProfiles");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ImportFailed", "Import Failed"),
            message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    public async Task ExportProfileAsync(VoiceProfile? profile)
    {
      if (profile == null)
      {
        ErrorMessage = ResourceHelper.GetString("Profile.ExportNoSelection", "Select a profile to export.");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var exportBundle = new ProfileExportBundle
        {
          ExportedAt = DateTime.UtcNow.ToString("O"),
          Profiles = new List<ProfileImportData>
          {
            new ProfileImportData
            {
              Name = profile.Name,
              Language = profile.Language,
              Emotion = profile.Emotion,
              Tags = profile.Tags?.ToList()
            }
          }
        };

        var json = JsonSerializer.Serialize(exportBundle, new JsonSerializerOptions { WriteIndented = true });

        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        picker.SuggestedFileName = SanitizeFilename($"voice_profile_{profile.Name}_{DateTime.Now:yyyyMMdd}");

        var file = await picker.PickSaveFileAsync();
        if (file == null)
        {
          return;
        }

        await FileIO.WriteTextAsync(file, json);
        var message = string.Format(
            ResourceHelper.GetString("Profile.ExportSuccess", "Exported to {0}"),
            file.Name);
        _toastNotificationService?.ShowSuccess(
            message,
            ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        var message = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = string.Format(
            ResourceHelper.GetString("Profile.ExportFailed", "Export failed: {0}"),
            message);
        _logService?.LogError(ex, "ExportProfile");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ExportFailed", "Export Failed"),
            message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    public async Task ExportSelectedProfilesAsync()
    {
      var selectedProfiles = GetSelectedProfiles();
      if (selectedProfiles.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("Profile.ExportNoSelection", "Select profiles to export.");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var exportBundle = new ProfileExportBundle
        {
          ExportedAt = DateTime.UtcNow.ToString("O"),
          Profiles = selectedProfiles.Select(profile => new ProfileImportData
          {
            Name = profile.Name,
            Language = profile.Language,
            Emotion = profile.Emotion,
            Tags = profile.Tags?.ToList()
          }).ToList()
        };

        var json = JsonSerializer.Serialize(exportBundle, new JsonSerializerOptions { WriteIndented = true });

        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        picker.SuggestedFileName = SanitizeFilename($"voice_profiles_export_{DateTime.Now:yyyyMMdd}");

        var file = await picker.PickSaveFileAsync();
        if (file == null)
        {
          return;
        }

        await FileIO.WriteTextAsync(file, json);
        var message = string.Format(
            ResourceHelper.GetString("Profile.ExportSuccess", "Exported to {0}"),
            file.Name);
        _toastNotificationService?.ShowSuccess(
            message,
            ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        var message = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = string.Format(
            ResourceHelper.GetString("Profile.ExportFailed", "Export failed: {0}"),
            message);
        _logService?.LogError(ex, "ExportProfiles");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ExportFailed", "Export Failed"),
            message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    // GAP-I15: Added CancellationToken parameter for proper propagation
    public async Task DuplicateProfileAsync(VoiceProfile? profile, CancellationToken cancellationToken = default)
    {
      if (profile == null)
      {
        ErrorMessage = ResourceHelper.GetString("Profile.DuplicateNoSelection", "Select a profile to duplicate.");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var duplicateName = GenerateDuplicateName(profile.Name);
        var newProfile = await _profilesUseCase.CreateAsync(
            duplicateName,
            profile.Language,
            profile.Emotion,
            profile.Tags?.ToList(),
            cancellationToken);

        if (newProfile != null)
        {
          Profiles.Add(newProfile);
          SelectedProfile = newProfile;
          UpdateAvailableFilters();
          ApplyFilters();

          // Publish profile created event for cross-panel synchronization (Phase 4)
          _eventAggregator?.Publish(new ProfileCreatedEvent(PanelId, newProfile.Id, newProfile.Name ?? duplicateName));

          var message = string.Format(
              ResourceHelper.GetString("Profile.DuplicateSuccess", "Duplicated {0}"),
              duplicateName);
          _toastNotificationService?.ShowSuccess(
              message,
              ResourceHelper.GetString("Toast.Title.DuplicateComplete", "Profile Duplicated"));
        }
      }
      catch (Exception ex)
      {
        var message = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = string.Format(
            ResourceHelper.GetString("Profile.DuplicateFailed", "Duplicate failed: {0}"),
            message);
        _logService?.LogError(ex, "DuplicateProfile");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.DuplicateFailed", "Duplicate Failed"),
            message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    // GAP-I15: Added CancellationToken parameter for proper propagation
    public async Task UpdateProfileAsync(
        VoiceProfile? profile,
        string? name,
        string? language,
        string? emotion,
        string? tagsText,
        CancellationToken cancellationToken = default)
    {
      if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
      {
        ErrorMessage = ResourceHelper.GetString("Profile.UpdateNoSelection", "Select a profile to update.");
        return;
      }

      var validation = InputValidator.ValidateProfileName(name);
      if (!validation.IsValid)
      {
        ErrorMessage = validation.ErrorMessage;
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var tags = ParseTags(tagsText);
        var updated = await _profilesUseCase.UpdateAsync(
            profile.Id,
            name?.Trim(),
            string.IsNullOrWhiteSpace(language) ? null : language!.Trim(),
            string.IsNullOrWhiteSpace(emotion) ? null : emotion!.Trim(),
            tags,
            cancellationToken);

        ReplaceProfile(updated);
        SelectedProfile = updated;
        UpdateAvailableFilters();
        ApplyFilters();

        // Publish profile updated event for cross-panel synchronization (Phase 4)
        if (updated != null)
        {
          _eventAggregator?.Publish(new ProfileUpdatedEvent(PanelId, updated.Id, new Dictionary<string, object>
          {
            { "Name", updated.Name ?? "" },
            { "Language", updated.Language ?? "" },
            { "Emotion", updated.Emotion ?? "" }
          }));
        }

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Profile.UpdateSuccess", "Profile updated"),
            ResourceHelper.GetString("Toast.Title.UpdateComplete", "Profile Updated"));
      }
      catch (Exception ex)
      {
        var message = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = string.Format(
            ResourceHelper.GetString("Profile.UpdateFailed", "Update failed: {0}"),
            message);
        _logService?.LogError(ex, "UpdateProfile");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.UpdateFailed", "Update Failed"),
            message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    // GAP-I15: Added CancellationToken parameter for proper propagation
    public async Task AnalyzeProfileQualityAsync(VoiceProfile? profile, CancellationToken cancellationToken = default)
    {
      if (profile == null)
      {
        ErrorMessage = ResourceHelper.GetString("Profile.AnalyzeNoSelection", "Select a profile to analyze.");
        return;
      }

      SelectedProfile = profile;
      await LoadQualityHistoryAsync(cancellationToken);
      await LoadQualityTrendsAsync(cancellationToken);
      await CheckQualityDegradationAsync(cancellationToken);

      var message = string.Format(
          ResourceHelper.GetString("Profile.AnalyzeSuccess", "Analysis completed for {0}"),
          profile.Name ?? string.Empty);
      _toastNotificationService?.ShowSuccess(
          message,
          ResourceHelper.GetString("Toast.Title.AnalysisComplete", "Analysis Complete"));
    }

    public bool ReorderProfiles(VoiceProfile? dragged, VoiceProfile? target, DropPosition dropPosition)
    {
      if (dragged == null || target == null)
      {
        return false;
      }

      if (dragged.Id == target.Id)
      {
        return false;
      }

      var fromIndex = Profiles.IndexOf(dragged);
      var targetIndex = Profiles.IndexOf(target);
      if (fromIndex < 0 || targetIndex < 0)
      {
        return false;
      }

      Profiles.RemoveAt(fromIndex);
      if (fromIndex < targetIndex)
      {
        targetIndex--;
      }

      var insertIndex = dropPosition switch
      {
        DropPosition.Before => targetIndex,
        DropPosition.After => targetIndex + 1,
        _ => targetIndex
      };

      if (insertIndex < 0)
      {
        insertIndex = 0;
      }
      if (insertIndex > Profiles.Count)
      {
        insertIndex = Profiles.Count;
      }

      Profiles.Insert(insertIndex, dragged);
      ApplyFilters();

      _toastNotificationService?.ShowSuccess(
          ResourceHelper.GetString("Profile.ReorderSuccess", "Profile order updated"),
          ResourceHelper.GetString("Toast.Title.ReorderComplete", "Profiles Reordered"));

      return true;
    }

    private static List<string>? ParseTags(string? tagsText)
    {
      if (string.IsNullOrWhiteSpace(tagsText))
      {
        return null;
      }

      return tagsText
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(tag => !string.IsNullOrWhiteSpace(tag))
          .ToList();
    }

    private static string SanitizeFilename(string? value)
    {
      var name = string.IsNullOrWhiteSpace(value) ? "profile_export" : value!;
      foreach (var c in System.IO.Path.GetInvalidFileNameChars())
      {
        name = name.Replace(c, '_');
      }
      return name;
    }

    private string GenerateDuplicateName(string? baseName)
    {
      var normalized = string.IsNullOrWhiteSpace(baseName)
          ? ResourceHelper.GetString("Profile.Unnamed", "Unnamed Profile")
          : baseName!.Trim();

      var candidate = $"{normalized} (Copy)";
      var counter = 2;
      while (Profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
      {
        candidate = $"{normalized} (Copy {counter})";
        counter++;
      }

      return candidate;
    }

    private void ReplaceProfile(VoiceProfile? updatedProfile)
    {
      if (updatedProfile == null)
      {
        return;
      }

      for (var i = 0; i < Profiles.Count; i++)
      {
        if (Profiles[i].Id == updatedProfile.Id)
        {
          Profiles[i] = updatedProfile;
          break;
        }
      }
    }

    private List<VoiceProfile> GetSelectedProfiles()
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0)
      {
        return SelectedProfile != null ? new List<VoiceProfile> { SelectedProfile } : new List<VoiceProfile>();
      }

      return Profiles
          .Where(profile => _multiSelectState.SelectedIds.Contains(profile.Id))
          .ToList();
    }

    private static List<ProfileImportData> ParseProfileImports(string json, out string? errorMessage)
    {
      errorMessage = null;
      try
      {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
          return ParseProfileArray(root);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
          if (TryGetProperty(root, "profiles", out var profilesElement) && profilesElement.ValueKind == JsonValueKind.Array)
          {
            return ParseProfileArray(profilesElement);
          }

          var single = ParseProfileObject(root);
          return single != null ? new List<ProfileImportData> { single } : new List<ProfileImportData>();
        }
      }
      catch (JsonException ex)
      {
        errorMessage = ResourceHelper.FormatString("Profile.ImportParseFailed", ex.Message);
        return new List<ProfileImportData>();
      }

      errorMessage = ResourceHelper.GetString("Profile.ImportParseFailed", "Invalid profile import format.");
      return new List<ProfileImportData>();
    }

    private static List<ProfileImportData> ParseProfileArray(JsonElement element)
    {
      var profiles = new List<ProfileImportData>();
      foreach (var item in element.EnumerateArray())
      {
        if (item.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var profile = ParseProfileObject(item);
        if (profile != null)
        {
          profiles.Add(profile);
        }
      }
      return profiles;
    }

    private static ProfileImportData? ParseProfileObject(JsonElement element)
    {
      var importData = new ProfileImportData();

      if (TryGetProperty(element, "name", out var nameElement))
      {
        importData.Name = nameElement.GetString();
      }

      if (TryGetProperty(element, "language", out var languageElement))
      {
        importData.Language = languageElement.GetString();
      }

      if (TryGetProperty(element, "emotion", out var emotionElement))
      {
        importData.Emotion = emotionElement.GetString();
      }

      if (TryGetProperty(element, "tags", out var tagsElement))
      {
        if (tagsElement.ValueKind == JsonValueKind.Array)
        {
          importData.Tags = tagsElement.EnumerateArray()
              .Select(tag => tag.GetString())
              .Where(tag => !string.IsNullOrWhiteSpace(tag))
              .Select(tag => tag!.Trim())
              .ToList();
        }
        else if (tagsElement.ValueKind == JsonValueKind.String)
        {
          importData.Tags = ParseTags(tagsElement.GetString());
        }
      }

      return importData;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
      foreach (var property in element.EnumerateObject())
      {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
        {
          value = property.Value;
          return true;
        }
      }

      value = default;
      return false;
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profilesList = await _profilesUseCase.ListAsync(cancellationToken);

        Profiles.Clear();
        foreach (var profile in profilesList)
        {
          Profiles.Add(profile);
        }

        // Extract unique languages and emotions
        UpdateAvailableFilters();

        // Apply filters after loading
        ApplyFilters();
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Profile.LoadFailed", "Failed to load profiles"));
        _logService?.LogError(ex, "LoadProfiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateProfileAsync(string? name, CancellationToken cancellationToken)
    {
      // Validate input
      var validation = InputValidator.ValidateProfileName(name);
      if (!validation.IsValid)
      {
        ErrorMessage = validation.ErrorMessage;
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profile = await _profilesUseCase.CreateAsync(name!, cancellationToken: cancellationToken);
        ArgumentNullException.ThrowIfNull(profile);
        Profiles.Add(profile);
        SelectedProfile = profile;

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new CreateProfileAction(
              Profiles,
              _backendClient,
              profile,
              onUndo: (p) =>
              {
                if (SelectedProfile?.Id == p.Id)
                {
                  SelectedProfile = null;
                }
              },
              onRedo: (p) => SelectedProfile = p);
          _undoRedoService.RegisterAction(action);
        }

        // Publish profile created event for cross-panel synchronization (Phase 4)
        _eventAggregator?.Publish(new ProfileCreatedEvent(PanelId, profile.Id, profile.Name ?? name!));

        // Show success toast
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Success.ProfileCreated", name ?? string.Empty),
            ResourceHelper.GetString("Toast.Title.ProfileCreated", "Profile Created"));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Profile.CreateFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Profile.CreateFailed", "Failed to create profile"));
        _logService?.LogError(ex, "CreateProfile");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Profile.CreateFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.CreateFailed", "Create Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteProfileAsync(string? profileId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(profileId))
        return;

      var profile = Profiles.FirstOrDefault(p => p.Id == profileId);
      if (profile == null)
        return;

      // Show confirmation dialog
      // Note: XamlRoot should be passed from the View if available
      var confirmed = await Utilities.ConfirmationDialog.ShowDeleteConfirmationAsync(
          profile.Name ?? ResourceHelper.GetString("Profile.Unnamed", "Unnamed Profile"),
          "profile"
      );

      if (!confirmed)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var success = await _profilesUseCase.DeleteAsync(profileId, cancellationToken);
        if (success)
        {
          var profileToDelete = Profiles.FirstOrDefault(p => p.Id == profileId);
          if (profileToDelete != null)
          {
            var profileName = profileToDelete.Name ?? ResourceHelper.GetString("Profile.Unnamed", "Unnamed Profile");
            var wasSelected = SelectedProfile?.Id == profileId;

            Profiles.Remove(profileToDelete);
            if (wasSelected)
            {
              SelectedProfile = null;
            }

            // Register undo action
            if (_undoRedoService != null)
            {
              var action = new DeleteProfileAction(
                  Profiles,
                  _backendClient,
                  profileToDelete,
                  onUndo: (p) => SelectedProfile = p,
                  onRedo: (p) =>
                  {
                    if (SelectedProfile?.Id == p.Id)
                    {
                      SelectedProfile = null;
                    }
                  });
              _undoRedoService.RegisterAction(action);
            }

            // Publish profile deleted event for cross-panel synchronization (Phase 4)
            _eventAggregator?.Publish(new ProfileDeletedEvent(PanelId, profileId));

            // Show success toast
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("Success.ProfileDeleted", profileName),
                ResourceHelper.GetString("Toast.Title.ProfileDeleted", "Profile Deleted"));
          }
        }
        else
        {
          var errorMsg = ResourceHelper.GetString("Profile.DeleteFailed", "Failed to delete profile");
          ErrorMessage = errorMsg;
          _toastNotificationService?.ShowError(errorMsg, ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Profile.DeleteFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Profile.DeleteFailed", "Failed to delete profile"));
        _logService?.LogError(ex, "DeleteProfile");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Profile.DeleteFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    partial void OnSelectedProfileChanged(VoiceProfile? value)
    {
      CanPreview = SelectedProfile != null;
      PreviewProfileCommand.NotifyCanExecuteChanged();

      // Use ContextManager for centralized profile state (Panel Architecture Phase 2)
      // Falls back to direct event publishing if context manager unavailable
      if (value != null)
      {
        if (_contextManager != null)
        {
          _contextManager.SetActiveProfile(value.Id, value.Name, InteractionIntent.Navigation);
        }
        else
        {
          _eventAggregator?.Publish(new ProfileSelectedEvent(PanelId, value.Id, value.Name));
        }
      }
      else if (_contextManager != null)
      {
        // Clear the active profile when deselected
        _contextManager.SetActiveProfile(null, null);
      }

      // Cancel any pending profile change operations to avoid race conditions
      try
      {
        _profileChangeCts?.Cancel();
        _profileChangeCts?.Dispose();
      }
      // ALLOWED: empty catch - already disposed is expected during cleanup
      catch (ObjectDisposedException)
      {
      }
      _profileChangeCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

      // Load cached quality metrics if available
      if (value != null && _previewQualityCache.ContainsKey(value.Id))
      {
        PreviewQualityMetrics = _previewQualityCache[value.Id];
        HasPreviewQualityMetrics = PreviewQualityMetrics != null;
        PreviewQualityScore = _previewQualityScoreCache.GetValueOrDefault(value.Id);
      }
      else
      {
        PreviewQualityMetrics = null;
        HasPreviewQualityMetrics = false;
        PreviewQualityScore = null;
      }

      // Load quality history when profile is selected (IDEA 30)
      if (value != null)
      {
        var ct = _profileChangeCts.Token;
        _ = LoadQualityHistoryAsync(ct).ContinueWith(t =>
        {
          if (t.IsFaulted && t.Exception?.InnerException is not OperationCanceledException)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadQualityHistory failed"), "LoadQualityHistory");
        }, TaskScheduler.Default);
        _ = LoadQualityTrendsAsync(ct).ContinueWith(t =>
        {
          if (t.IsFaulted && t.Exception?.InnerException is not OperationCanceledException)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadQualityTrends failed"), "LoadQualityTrends");
        }, TaskScheduler.Default);
        // Also check for degradation (IDEA 56)
        _ = CheckQualityDegradationAsync(ct).ContinueWith(t =>
        {
          if (t.IsFaulted && t.Exception?.InnerException is not OperationCanceledException)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("CheckQualityDegradation failed"), "CheckQualityDegradation");
        }, TaskScheduler.Default);
        _ = LoadQualityBaselineAsync(ct).ContinueWith(t =>
        {
          if (t.IsFaulted && t.Exception?.InnerException is not OperationCanceledException)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadQualityBaseline failed"), "LoadQualityBaseline");
        }, TaskScheduler.Default);
      }
      else
      {
        QualityHistory.Clear();
        QualityTrends = null;
        HasQualityHistory = false;
        QualityDegradation = null;
        QualityDegradationAlerts.Clear();
        QualityBaseline = null;
        HasQualityDegradation = false;
      }
    }

    partial void OnIsLoadingChanged(bool value)
    {
      PreviewProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPreviewingChanged(bool value)
    {
      PreviewProfileCommand.NotifyCanExecuteChanged();
      StopPreviewCommand.NotifyCanExecuteChanged();
    }

    private async Task PreviewProfileAsync(string? profileId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(profileId) || SelectedProfile == null)
        return;

      try
      {
        IsPreviewing = true;
        IsLoading = true;
        ErrorMessage = null;
        HasPreviewQualityMetrics = false;

        string? audioUrl = null;
        QualityMetrics? qualityMetrics = null;
        double? qualityScore = null;

        // Check cache first
        if (_previewCache.ContainsKey(profileId))
        {
          audioUrl = _previewCache[profileId];
          qualityMetrics = _previewQualityCache.GetValueOrDefault(profileId);
          qualityScore = _previewQualityScoreCache.GetValueOrDefault(profileId);
        }
        else
        {
          // Synthesize preview audio with default text
          var request = new VoiceSynthesisRequest
          {
            Engine = "xtts", // Default engine for preview
            ProfileId = profileId,
            Text = DEFAULT_PREVIEW_TEXT,
            Language = SelectedProfile.Language ?? "en",
            Emotion = string.IsNullOrWhiteSpace(SelectedProfile.Emotion) ? null : SelectedProfile.Emotion,
            EnhanceQuality = false // Fast preview, no quality enhancement
          };

          var response = await _backendClient.SynthesizeVoiceAsync(request, cancellationToken);
          audioUrl = response.AudioUrl;
          qualityMetrics = response.QualityMetrics;
          qualityScore = response.QualityScore;

          // Cache the results
          if (!string.IsNullOrWhiteSpace(audioUrl))
          {
            _previewCache[profileId] = audioUrl;
            if (qualityMetrics != null)
              _previewQualityCache[profileId] = qualityMetrics;
            if (qualityScore.HasValue)
              _previewQualityScoreCache[profileId] = qualityScore.Value;
          }
        }

        // Update quality metrics display
        if (qualityMetrics != null)
        {
          PreviewQualityMetrics = qualityMetrics;
          HasPreviewQualityMetrics = true;
        }
        PreviewQualityScore = qualityScore;

        // Update profile's quality score if we have a new quality score from preview
        // This provides real-time quality updates when previewing profiles
        if (qualityScore.HasValue && SelectedProfile != null && SelectedProfile.Id == profileId)
        {
          // Update the profile's quality score (this will automatically update the badge via data binding)
          var profile = Profiles.FirstOrDefault(p => p.Id == profileId);
          if (profile != null)
          {
            profile.QualityScore = qualityScore.Value;
            // Trigger property change notification for the badge to update
            OnPropertyChanged(nameof(Profiles));
            OnPropertyChanged(nameof(FilteredProfiles));
          }
        }

        if (!string.IsNullOrWhiteSpace(audioUrl))
        {
          // Download and play preview audio
          using var httpClient = new System.Net.Http.HttpClient();
          var audioBytes = await httpClient.GetByteArrayAsync(audioUrl);

          // Save to temporary file
          var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voicestudio_preview_{Guid.NewGuid()}.wav");
          await System.IO.File.WriteAllBytesAsync(tempPath, audioBytes);

          // Play preview
          await _audioPlayer.PlayFileAsync(tempPath, () =>
          {
            // Cleanup and reset state after playback
            try
            {
              if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
            }
            catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "ProfileExportBundle.Unknown"); }

            IsPreviewing = false;
            IsLoading = false;
          });
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        IsPreviewing = false;
        IsLoading = false;
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Profile.PreviewFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Profile.PreviewFailed", "Failed to preview profile"));
        _logService?.LogError(ex, "PreviewProfile");
        IsPreviewing = false;
        IsLoading = false;
      }
    }

    private void StopPreview()
    {
      try
      {
        _audioPlayer.Stop();
        IsPreviewing = false;
      }
      catch (Exception ex)
      {
        ErrorHandler.LogError(ex, "StopPreview");
        ErrorMessage = ResourceHelper.FormatString("Profile.StopPreviewFailed", ErrorHandler.GetUserFriendlyMessage(ex));
      }
    }

    // Multi-select methods
    public void ToggleSelection(string profileId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allIds = Profiles.Select(p => p.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, profileId, allIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(profileId);
      }
      else
      {
        // Single selection (clear others)
        _multiSelectState.SetSingle(profileId);
      }

      UpdateSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAll()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var profile in FilteredProfiles)
      {
        _multiSelectState.Add(profile.Id);
      }
      if (FilteredProfiles.Count > 0)
      {
        _multiSelectState.RangeAnchorId = FilteredProfiles[0].Id;
      }

      UpdateSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      SelectAllCommand.NotifyCanExecuteChanged();
    }

    private void ClearSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedAsync(CancellationToken cancellationToken)
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new List<string>(_multiSelectState.SelectedIds);

      // Show confirmation dialog
      var confirmed = await Utilities.ConfirmationDialog.ShowDeleteConfirmationAsync(
          $"{selectedIds.Count} profile(s)",
          "profiles"
      );

      if (!confirmed)
        return;

      cancellationToken.ThrowIfCancellationRequested();

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profilesToDelete = new List<VoiceProfile>();
        int deletedCount = 0;
        var wasAnySelected = false;

        foreach (var profileId in selectedIds)
        {
          cancellationToken.ThrowIfCancellationRequested();

          try
          {
            var success = await _profilesUseCase.DeleteAsync(profileId, cancellationToken);
            if (success)
            {
              var profile = Profiles.FirstOrDefault(p => p.Id == profileId);
              if (profile != null)
              {
                profilesToDelete.Add(profile);
                Profiles.Remove(profile);
                if (SelectedProfile?.Id == profileId)
                {
                  SelectedProfile = null;
                  wasAnySelected = true;
                }
                deletedCount++;
              }
            }
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "ProfileExportBundle.DeleteSelectedAsync");
      }
        }

        // Register batch undo action if any profiles were deleted
        if (deletedCount > 0 && _undoRedoService != null && profilesToDelete.Count > 0)
        {
          var action = new BatchDeleteProfilesAction(
              Profiles,
              _backendClient,
              profilesToDelete,
              onUndo: (profiles) =>
              {
                if (wasAnySelected && profiles.Any())
                {
                  SelectedProfile = profiles.First();
                }
              },
              onRedo: (profiles) =>
              {
                if (SelectedProfile != null && profiles.Any(p => p.Id == SelectedProfile.Id))
                {
                  SelectedProfile = null;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        // Clear selection after deletion
        ClearSelection();

        // Show success toast
        if (deletedCount > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Profile.BatchDeleteComplete", deletedCount),
              ResourceHelper.GetString("Toast.Title.BatchDeleteComplete", "Batch Delete Complete"));
        }
        if (deletedCount < selectedIds.Count)
        {
          _toastNotificationService?.ShowWarning(
              ResourceHelper.FormatString("Profile.BatchDeletePartial", deletedCount, selectedIds.Count),
              ResourceHelper.GetString("Toast.Title.PartialDelete", "Partial Delete"));
        }
      }
      catch (Exception ex)
      {
        ErrorHandler.LogError(ex, "DeleteSelectedProfiles");
        var errorMsg = ResourceHelper.FormatString("Profile.BatchDeleteFailed", ErrorHandler.GetUserFriendlyMessage(ex));
        ErrorMessage = errorMsg;
        _toastNotificationService?.ShowError(errorMsg, ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedCount = 0;
        HasMultipleSelection = false;
      }
      else
      {
        SelectedCount = _multiSelectState.Count;
        HasMultipleSelection = _multiSelectState.IsMultipleSelection;
      }

      OnPropertyChanged(nameof(SelectedCount));
      OnPropertyChanged(nameof(HasMultipleSelection));
      DeleteSelectedCommand.NotifyCanExecuteChanged();

      // Notify UI that selection state changed for all profiles
      foreach (var profile in Profiles)
      {
        OnPropertyChanged(nameof(IsProfileSelected));
      }
    }

    // Filtering methods
    private void ApplyFilters()
    {
      var query = (SearchQuery ?? "").Trim().ToLowerInvariant();

      var filtered = Profiles.Where(profile =>
      {
        // Search query filter
        if (!string.IsNullOrEmpty(query))
        {
          var nameMatch = (profile.Name ?? "").ToLowerInvariant().Contains(query);
          var tagMatch = profile.Tags?.Any(tag => tag.ToLowerInvariant().Contains(query)) ?? false;
          if (!nameMatch && !tagMatch)
            return false;
        }

        // Language filter
        if (!string.IsNullOrEmpty(SelectedLanguage) && profile.Language != SelectedLanguage)
          return false;

        // Emotion filter
        if (!string.IsNullOrEmpty(SelectedEmotion) && profile.Emotion != SelectedEmotion)
          return false;

        // Quality range filter
        var allFilter = ResourceHelper.GetString("Filter.All", "All");
        if (!string.IsNullOrEmpty(SelectedQualityRange) && SelectedQualityRange != allFilter)
        {
          var quality = profile.QualityScore;
          var matches = SelectedQualityRange switch
          {
            var s when s == ResourceHelper.GetString("Filter.QualityHigh", "High (4.0+)") => quality >= 4.0,
            var s when s == ResourceHelper.GetString("Filter.QualityGood", "Good (3.0-4.0)") => quality >= 3.0 && quality < 4.0,
            var s when s == ResourceHelper.GetString("Filter.QualityFair", "Fair (2.0-3.0)") => quality >= 2.0 && quality < 3.0,
            var s when s == ResourceHelper.GetString("Filter.QualityLow", "Low (<2.0)") => quality < 2.0,
            _ => true
          };
          if (!matches)
            return false;
        }

        return true;
      }).ToList();

      FilteredProfiles.Clear();
      foreach (var profile in filtered)
      {
        FilteredProfiles.Add(profile);
      }

      TotalProfiles = Profiles.Count;
      FilteredCount = FilteredProfiles.Count;
      OnPropertyChanged(nameof(HasProfiles));
    }

    private void UpdateAvailableFilters()
    {
      var languages = Profiles.Select(p => p.Language).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();
      AvailableLanguages.Clear();
      AvailableLanguages.Add(ResourceHelper.GetString("Filter.All", "All"));
      foreach (var lang in languages)
      {
        AvailableLanguages.Add(lang);
      }

      var emotions = Profiles.Select(p => p.Emotion).Where(e => !string.IsNullOrEmpty(e)).Distinct().OrderBy(e => e).ToList();
      AvailableEmotions.Clear();
      AvailableEmotions.Add(ResourceHelper.GetString("Filter.All", "All"));
      foreach (var emotion in emotions)
      {
        AvailableEmotions.Add(emotion);
      }
    }

    partial void OnSearchQueryChanged(string? value)
    {
      ApplyFilters();
    }

    partial void OnSelectedLanguageChanged(string? value)
    {
      ApplyFilters();
    }

    partial void OnSelectedEmotionChanged(string? value)
    {
      ApplyFilters();
    }

    partial void OnSelectedQualityRangeChanged(string? value)
    {
      ApplyFilters();
    }

    partial void OnEnhancementResultChanged(ReferenceAudioPreprocessResponse? value)
    {
      HasEnhancementResult = value != null;
      PreviewEnhancedAudioCommand.NotifyCanExecuteChanged();
      ApplyEnhancedAudioCommand.NotifyCanExecuteChanged();
    }

    private async Task EnhanceReferenceAudioAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null)
        return;

      try
      {
        IsEnhancing = true;
        ErrorMessage = null;
        EnhanceReferenceAudioCommand.NotifyCanExecuteChanged();

        var request = new ReferenceAudioPreprocessRequest
        {
          ProfileId = SelectedProfile.Id,
          AutoEnhance = AutoEnhance,
          SelectOptimalSegments = SelectOptimalSegments,
          MinSegmentDuration = 1.0,
          MaxSegments = 5
        };

        var response = await _backendClient.SendRequestAsync<ReferenceAudioPreprocessRequest, ReferenceAudioPreprocessResponse>(
            $"/api/profiles/{SelectedProfile.Id}/preprocess-reference",
            request,
            cancellationToken
        );

        if (response != null)
        {
          EnhancementResult = response;
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Profile.EnhancementComplete", response.QualityImprovement),
              ResourceHelper.GetString("Toast.Title.EnhancementComplete", "Enhancement Complete")
          );
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Profile.EnhancementFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Profile.EnhancementFailed", "Failed to enhance reference audio"));
        _logService?.LogError(ex, "EnhanceReferenceAudio");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Profile.EnhancementFailed", ErrorHandler.GetUserFriendlyMessage(ex)),
            ResourceHelper.GetString("Toast.Title.EnhancementComplete", "Enhancement Failed")
        );
      }
      finally
      {
        IsEnhancing = false;
        EnhanceReferenceAudioCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task PreviewEnhancedAudioAsync(CancellationToken cancellationToken)
    {
      if (EnhancementResult == null || string.IsNullOrEmpty(EnhancementResult.ProcessedAudioUrl))
        return;

      IsPlayingEnhanced = true;
      StopEnhancedPreviewCommand.NotifyCanExecuteChanged();
      PreviewEnhancedAudioCommand.NotifyCanExecuteChanged();

      try
      {
        // Play the enhanced audio
        await _audioPlayer.PlayFileAsync(EnhancementResult.ProcessedAudioUrl);

        // Wait for playback to complete
        while (_audioPlayer.IsPlaying)
        {
          cancellationToken.ThrowIfCancellationRequested();
          await Task.Delay(100, cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        _audioPlayer.Stop();
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Error.PreviewEnhancedFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.PreviewEnhancedFailed", "Failed to preview enhanced audio"));
        _logService?.LogError(ex, "PreviewEnhancedAudio");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Error.PreviewEnhancedFailed", errorMsg),
            ResourceHelper.GetString("VoiceSynthesis.PreviewFailed", "Preview Failed")
        );
      }
      finally
      {
        IsPlayingEnhanced = false;
        StopEnhancedPreviewCommand.NotifyCanExecuteChanged();
        PreviewEnhancedAudioCommand.NotifyCanExecuteChanged();
      }
    }

    private void StopEnhancedPreview()
    {
      try
      {
        _audioPlayer.Stop();
      }
      catch (Exception ex)
      {
        ErrorHandler.LogError(ex, "StopEnhancedPreview");
      }
      finally
      {
        IsPlayingEnhanced = false;
        StopEnhancedPreviewCommand.NotifyCanExecuteChanged();
        PreviewEnhancedAudioCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task ApplyEnhancedAudioAsync(CancellationToken cancellationToken)
    {
      if (EnhancementResult == null || SelectedProfile == null)
        return;

      IsLoading = true;
      ErrorMessage = null;
      ApplyEnhancedAudioCommand.NotifyCanExecuteChanged();

      try
      {
        // Note: The backend may need to be updated to support reference_audio_url updates
        // For now, we'll just show a success message and reload profiles
        await LoadProfilesAsync(cancellationToken);

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Profile.EnhancementApplied", "Enhanced reference audio has been applied to the profile"),
            ResourceHelper.GetString("Toast.Title.EnhancementApplied", "Enhancement Applied")
        );

        // Clear enhancement result after applying
        EnhancementResult = null;
        HasEnhancementResult = false;
      }
      catch (Exception ex)
      {
        ErrorHandler.LogError(ex, "ApplyEnhancedAudio");
        ErrorMessage = ResourceHelper.FormatString("Error.ApplyEnhancedFailed", ErrorHandler.GetUserFriendlyMessage(ex));
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Error.ApplyEnhancedFailed", ErrorHandler.GetUserFriendlyMessage(ex)),
            ResourceHelper.GetString("Toast.Title.SaveFailed", "Apply Failed")
        );
      }
      finally
      {
        IsLoading = false;
        ApplyEnhancedAudioCommand.NotifyCanExecuteChanged();
      }
    }

    // Quality History Methods (IDEA 30)
    private async Task LoadQualityHistoryAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null)
        return;

      IsLoadingQualityHistory = true;
      LoadQualityHistoryCommand.NotifyCanExecuteChanged();
      LoadQualityTrendsCommand.NotifyCanExecuteChanged();

      try
      {
        var history = await _backendClient.GetQualityHistoryAsync(
            SelectedProfile.Id,
            limit: 50, // Load last 50 entries
            cancellationToken: cancellationToken
        );

        QualityHistory.Clear();
        foreach (var entry in history)
        {
          QualityHistory.Add(entry);
        }

        HasQualityHistory = QualityHistory.Count > 0;
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Log error but don't break UI - quality history is non-critical
        _logService?.LogError(ex, "LoadQualityHistory");
        QualityHistory.Clear();
        HasQualityHistory = false;
      }
      finally
      {
        IsLoadingQualityHistory = false;
        LoadQualityHistoryCommand.NotifyCanExecuteChanged();
        LoadQualityTrendsCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task LoadQualityTrendsAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null)
        return;

      IsLoadingQualityHistory = true;
      LoadQualityHistoryCommand.NotifyCanExecuteChanged();
      LoadQualityTrendsCommand.NotifyCanExecuteChanged();

      try
      {
        QualityTrends = await _backendClient.GetQualityTrendsAsync(
            SelectedProfile.Id,
            SelectedTimeRange,
            cancellationToken
        );
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Log error but don't break UI - quality trends are non-critical
        _logService?.LogError(ex, "LoadQualityTrends");
        QualityTrends = null;
      }
      finally
      {
        IsLoadingQualityHistory = false;
        LoadQualityHistoryCommand.NotifyCanExecuteChanged();
        LoadQualityTrendsCommand.NotifyCanExecuteChanged();
      }
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
      if (SelectedProfile != null)
      {
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
        _ = LoadQualityTrendsAsync(ct).ContinueWith(t =>
        {
          if (t.IsFaulted)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadQualityTrends failed"), "LoadQualityTrends");
        }, TaskScheduler.Default);
      }
    }

    // Quality Degradation Detection Methods (IDEA 56)
    private async Task CheckQualityDegradationAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null)
        return;

      IsLoadingDegradation = true;
      CheckQualityDegradationCommand.NotifyCanExecuteChanged();
      LoadQualityBaselineCommand.NotifyCanExecuteChanged();

      try
      {
        var degradation = await _backendClient.GetQualityDegradationAsync(
            SelectedProfile.Id,
            DegradationTimeWindowDays,
            degradationThresholdPercent: 10.0,
            criticalThresholdPercent: 25.0,
            cancellationToken
        );

        QualityDegradation = degradation;
        HasQualityDegradation = degradation?.HasDegradation ?? false;

        // Update alerts collection for binding
        QualityDegradationAlerts.Clear();
        if (degradation?.Alerts != null)
        {
          foreach (var alert in degradation.Alerts)
          {
            QualityDegradationAlerts.Add(alert);
          }
        }

        // Show toast notification if degradation detected
        if (HasQualityDegradation)
        {
          var criticalCount = QualityDegradationAlerts.Count(a => a.Severity == "critical");
          var warningCount = QualityDegradationAlerts.Count(a => a.Severity == "warning");

          if (criticalCount > 0)
          {
            _toastNotificationService?.ShowError(
                ResourceHelper.GetString("Profile.QualityDegradationAlert", "Quality Degradation Alert"),
                ResourceHelper.FormatString("Profile.QualityDegradationCritical", criticalCount, warningCount)
            );
          }
          else if (warningCount > 0)
          {
            _toastNotificationService?.ShowWarning(
                ResourceHelper.GetString("Profile.QualityDegradationAlert", "Quality Degradation Alert"),
                ResourceHelper.FormatString("Profile.QualityDegradationWarning", warningCount)
            );
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Log error but don't break UI - degradation detection is non-critical
        _logService?.LogError(ex, "CheckQualityDegradation");
        QualityDegradationAlerts.Clear();
        HasQualityDegradation = false;
      }
      finally
      {
        IsLoadingDegradation = false;
        CheckQualityDegradationCommand.NotifyCanExecuteChanged();
        LoadQualityBaselineCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task LoadQualityBaselineAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null)
        return;

      IsLoadingDegradation = true;
      CheckQualityDegradationCommand.NotifyCanExecuteChanged();
      LoadQualityBaselineCommand.NotifyCanExecuteChanged();

      try
      {
        QualityBaseline = await _backendClient.GetQualityBaselineAsync(
            SelectedProfile.Id,
            timePeriodDays: 30,
            cancellationToken
        );
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Log error but don't break UI - baseline loading is non-critical
        _logService?.LogError(ex, "LoadQualityBaseline");
        QualityBaseline = null;
      }
      finally
      {
        IsLoadingDegradation = false;
        CheckQualityDegradationCommand.NotifyCanExecuteChanged();
        LoadQualityBaselineCommand.NotifyCanExecuteChanged();
      }
    }

    #region IPanelStatePersistable Implementation

    /// <summary>
    /// Gets the current panel state for persistence.
    /// Backend-Frontend Integration Plan - Phase 2.
    /// </summary>
    public PanelStateData? GetCurrentState()
    {
      try
      {
        var state = new PanelStateData
        {
          PanelId = PanelId,
          SelectedItemId = SelectedProfile?.Id,
          SearchText = SearchQuery,
          CapturedAt = DateTime.UtcNow,
          CustomData = new Dictionary<string, object>()
        };

        // Store filter states
        if (!string.IsNullOrEmpty(SelectedLanguage))
          state.CustomData["SelectedLanguage"] = SelectedLanguage;
        if (!string.IsNullOrEmpty(SelectedEmotion))
          state.CustomData["SelectedEmotion"] = SelectedEmotion;
        if (!string.IsNullOrEmpty(SelectedQualityRange))
          state.CustomData["SelectedQualityRange"] = SelectedQualityRange;

        // Store view mode
        state.CustomData["ViewMode"] = ViewMode;

        // Store sort state
        state.SortColumn = SortColumn;
        state.SortDescending = SortDescending;

        return state;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to get ProfilesViewModel state: {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Restores panel state from persistence.
    /// Backend-Frontend Integration Plan - Phase 2.
    /// </summary>
    public async Task RestoreStateAsync(PanelStateData state, CancellationToken cancellationToken = default)
    {
      if (state == null) return;

      try
      {
        // Restore search query
        if (!string.IsNullOrEmpty(state.SearchText))
          SearchQuery = state.SearchText;

        // Restore filter states
        if (state.CustomData?.TryGetValue("SelectedLanguage", out var lang) == true && lang is string langStr)
          SelectedLanguage = langStr;
        if (state.CustomData?.TryGetValue("SelectedEmotion", out var emotion) == true && emotion is string emotionStr)
          SelectedEmotion = emotionStr;
        if (state.CustomData?.TryGetValue("SelectedQualityRange", out var quality) == true && quality is string qualityStr)
          SelectedQualityRange = qualityStr;

        // Restore view mode
        if (state.CustomData?.TryGetValue("ViewMode", out var viewMode) == true && viewMode is string viewModeStr)
          ViewMode = viewModeStr;

        // Restore sort state
        if (!string.IsNullOrEmpty(state.SortColumn))
          SortColumn = state.SortColumn;
        if (state.SortDescending.HasValue)
          SortDescending = state.SortDescending.Value;

        // Restore profile selection (need profiles to be loaded first)
        if (!string.IsNullOrEmpty(state.SelectedItemId))
        {
          var profile = Profiles.FirstOrDefault(p => p.Id == state.SelectedItemId);
          if (profile != null)
            SelectedProfile = profile;
        }

        await Task.CompletedTask;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to restore ProfilesViewModel state: {ex.Message}");
      }
    }

    #endregion
  }
}