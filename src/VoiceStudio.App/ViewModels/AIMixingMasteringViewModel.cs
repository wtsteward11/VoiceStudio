using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the AIMixingMasteringView panel - AI mixing and mastering assistant.
  /// </summary>
  public partial class AIMixingMasteringViewModel : BaseViewModel, IPanelView
  {
    private readonly IAIMixingClient _aimixingClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private ObservableCollection<AIMixingMixSuggestionItem>? _suggestionsHooked;

    public string PanelId => PanelIds.AIMixingMastering;
    public string DisplayName => ResourceHelper.GetString("Panel.AIMixingMastering.DisplayName", "AI Mixing & Mastering");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private string? projectId;

    [ObservableProperty]
    private string selectedMode = "Balance Mix";

    [ObservableProperty]
    private ObservableCollection<string> availableModes = new() { "Balance Mix", "Master for Podcast", "Master for Broadcast", "Master for Streaming" };

    [ObservableProperty]
    private bool isAnalyzing;

    [ObservableProperty]
    private float analysisProgress;

    [ObservableProperty]
    private ObservableCollection<AIMixingMixSuggestionItem> suggestions = new();

    [ObservableProperty]
    private AIMixingMixSuggestionItem? selectedSuggestion;

    [ObservableProperty]
    private bool showBeforeAfter;

    [ObservableProperty]
    private string? originalAudioId;

    [ObservableProperty]
    private string? processedAudioId;

    [ObservableProperty]
    private string? processedAudioUrl;

    [ObservableProperty]
    private MixReportCardItem? reportCard;

    [ObservableProperty]
    private string? targetLoudness = "-16.0";

    [ObservableProperty]
    private string selectedTargetFormat = "podcast";

    [ObservableProperty]
    private ObservableCollection<string> availableFormats = new() { "podcast", "broadcast", "streaming", "music" };

    public Visibility ErrorMessageVisibility =>
        string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility MixAnalysisVisibility =>
        string.Equals(SelectedMode, "Balance Mix", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MasteringVisibility =>
        string.Equals(SelectedMode, "Balance Mix", StringComparison.Ordinal) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility AnalyzingVisibility =>
        IsAnalyzing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SuggestionsVisibility =>
        Suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ReportCardVisibility =>
        ReportCard != null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LoudnessMetVisibility =>
        ReportCard?.LoudnessMet == true ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProcessedAudioVisibility =>
        string.IsNullOrWhiteSpace(ProcessedAudioId) ? Visibility.Collapsed : Visibility.Visible;

    public AIMixingMasteringViewModel(IViewModelContext context, IAIMixingClient aimixingClient)
        : base(context)
    {
      _aimixingClient = aimixingClient ?? throw new ArgumentNullException(nameof(aimixingClient));

      // Get toast notification service (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _toastNotificationService = null;
      }

      AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !string.IsNullOrWhiteSpace(ProjectId) && !IsAnalyzing);
      ApplySuggestionCommand = new AsyncRelayCommand(ApplySuggestionAsync, () => SelectedSuggestion != null);
      ApplyAllCommand = new AsyncRelayCommand(ApplyAllAsync, () => Suggestions.Count > 0);
      PreviewSuggestionCommand = new AsyncRelayCommand(PreviewSuggestionAsync, () => SelectedSuggestion != null);
      AnalyzeMasteringCommand = new AsyncRelayCommand(AnalyzeMasteringAsync, () => !string.IsNullOrWhiteSpace(ProjectId));
      ApplyMasteringCommand = new AsyncRelayCommand(ApplyMasteringAsync, () => !string.IsNullOrWhiteSpace(ProjectId));
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);

      HookSuggestionsCollection(Suggestions);

      PropertyChanged += (_, e) =>
      {
        if (string.Equals(e.PropertyName, nameof(ErrorMessage), StringComparison.Ordinal))
        {
          OnPropertyChanged(nameof(ErrorMessageVisibility));
        }
      };
    }

    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IAsyncRelayCommand ApplySuggestionCommand { get; }
    public IAsyncRelayCommand ApplyAllCommand { get; }
    public IAsyncRelayCommand PreviewSuggestionCommand { get; }
    public IAsyncRelayCommand AnalyzeMasteringCommand { get; }
    public IAsyncRelayCommand ApplyMasteringCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnProjectIdChanged(string? value)
    {
      AnalyzeCommand.NotifyCanExecuteChanged();
      AnalyzeMasteringCommand.NotifyCanExecuteChanged();
      ApplyMasteringCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAnalyzingChanged(bool value)
    {
      AnalyzeCommand.NotifyCanExecuteChanged();
      OnPropertyChanged(nameof(AnalyzingVisibility));
    }

    partial void OnSelectedModeChanged(string value)
    {
      OnPropertyChanged(nameof(MixAnalysisVisibility));
      OnPropertyChanged(nameof(MasteringVisibility));
    }

    partial void OnProcessedAudioIdChanged(string? value)
    {
      OnPropertyChanged(nameof(ProcessedAudioVisibility));
    }

    partial void OnReportCardChanged(MixReportCardItem? value)
    {
      OnPropertyChanged(nameof(ReportCardVisibility));
      OnPropertyChanged(nameof(LoudnessMetVisibility));
    }

    partial void OnSelectedSuggestionChanged(AIMixingMixSuggestionItem? value)
    {
      ApplySuggestionCommand.NotifyCanExecuteChanged();
      PreviewSuggestionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSuggestionsChanged(ObservableCollection<AIMixingMixSuggestionItem> value)
    {
      ApplyAllCommand.NotifyCanExecuteChanged();
      HookSuggestionsCollection(value);
      OnPropertyChanged(nameof(SuggestionsVisibility));
    }

    private void HookSuggestionsCollection(ObservableCollection<AIMixingMixSuggestionItem> collection)
    {
      if (ReferenceEquals(_suggestionsHooked, collection))
      {
        return;
      }

      if (_suggestionsHooked != null)
      {
        _suggestionsHooked.CollectionChanged -= Suggestions_CollectionChanged;
      }

      _suggestionsHooked = collection;
      _suggestionsHooked.CollectionChanged += Suggestions_CollectionChanged;
    }

    private void Suggestions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      ApplyAllCommand.NotifyCanExecuteChanged();
      OnPropertyChanged(nameof(SuggestionsVisibility));
    }

    private async Task AnalyzeAsync()
    {
      if (string.IsNullOrWhiteSpace(ProjectId) || IsAnalyzing)
      {
        return;
      }

      try
      {
        IsAnalyzing = true;
        AnalysisProgress = 0.0f;
        ErrorMessage = null;
        Suggestions.Clear();

        // Simulate analysis progress
        for (int i = 0; i < 10; i++)
        {
          await Task.Delay(200);
          AnalysisProgress = (i + 1) / 10.0f;
        }

        var response = await _aimixingClient.AnalyzeMixAsync(ProjectId ?? "");

        if (response?.Suggestions != null)
        {
          Suggestions.Clear();
          foreach (var sug in response.Suggestions)
          {
            Suggestions.Add(new AIMixingMixSuggestionItem(sug));
          }

          StatusMessage = ResourceHelper.FormatString("AIMixingMastering.AnalysisComplete", Suggestions.Count);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("AIMixingMastering.AnalysisCompleteDetail", Suggestions.Count),
              ResourceHelper.GetString("Toast.Title.MixAnalysisComplete", "Mix Analysis Complete"));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AIMixingMastering.AnalyzeMixFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.AnalysisFailed", "Analysis Failed"),
            ex.Message);
      }
      finally
      {
        IsAnalyzing = false;
        AnalysisProgress = 0.0f;
      }
    }

    private async Task ApplySuggestionAsync()
    {
      if (SelectedSuggestion == null)
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new MixApplyRequest
        {
          SuggestionIds = new List<string> { SelectedSuggestion.SuggestionId },
          ApplyAll = false
        };

        var response = await _aimixingClient.ApplyMixAsync(request);

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString("AIMixingMastering.AppliedSuggestion", SelectedSuggestion.Description);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("AIMixingMastering.AppliedSuggestionDetail", SelectedSuggestion.Description),
              ResourceHelper.GetString("Toast.Title.SuggestionApplied", "Suggestion Applied"));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AIMixingMastering.ApplySuggestionFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ApplyFailed", "Apply Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyAllAsync()
    {
      if (Suggestions.Count == 0)
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new MixApplyRequest
        {
          SuggestionIds = Suggestions.Select(s => s.SuggestionId).ToList(),
          ApplyAll = true
        };

        var response = await _aimixingClient.ApplyMixAsync(request);

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString("AIMixingMastering.AppliedSuggestions", response.Applied);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("AIMixingMastering.AppliedSuggestionsDetail", response.Applied),
              ResourceHelper.GetString("Toast.Title.SuggestionsApplied", "Suggestions Applied"));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AIMixingMastering.ApplySuggestionsFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ApplyFailed", "Apply Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private Task PreviewSuggestionAsync()
    {
      if (SelectedSuggestion == null)
      {
        return Task.CompletedTask;
      }

      try
      {
        // Preview would apply suggestion temporarily and play audio
        StatusMessage = ResourceHelper.FormatString("AIMixingMastering.Previewing", SelectedSuggestion.Description);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AIMixingMastering.PreviewSuggestionFailed", ex.Message);
      }

      return Task.CompletedTask;
    }

    private async Task AnalyzeMasteringAsync()
    {
      if (string.IsNullOrWhiteSpace(ProjectId))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new MasteringAnalysisRequest
        {
          ProjectId = ProjectId,
          TargetLoudness = float.Parse(TargetLoudness ?? "-16.0"),
          TargetFormat = SelectedTargetFormat
        };

        var response = await _aimixingClient.AnalyzeMasteringAsync(request);

        if (response != null)
        {
          ReportCard = new MixReportCardItem(response);
          StatusMessage = ResourceHelper.GetString("AIMixingMastering.MasteringAnalysisComplete", "Mastering analysis complete");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("AIMixingMastering.MasteringAnalysisCompleteDetail", "Mastering analysis complete"),
              ResourceHelper.GetString("Toast.Title.AnalysisComplete", "Analysis Complete"));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AIMixingMastering.AnalyzeMasteringFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.AnalysisFailed", "Analysis Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyMasteringAsync()
    {
      if (string.IsNullOrWhiteSpace(ProjectId))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var settings = new MasteringSettingsData
        {
          Loudness = float.Parse(TargetLoudness ?? "-16.0"),
          PeakLimit = -1.0f
        };

        var request = new MasteringApplyRequest
        {
          ProjectId = ProjectId,
          Settings = settings
        };

        var response = await _aimixingClient.ApplyMasteringAsync(request);

        if (response != null)
        {
          ProcessedAudioId = response.OutputAudioId;
          ProcessedAudioUrl = response.OutputAudioUrl;
          StatusMessage = ResourceHelper.FormatString("AIMixingMastering.MasteringApplied", response.FinalLoudness);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("AIMixingMastering.MasteringAppliedDetail", response.FinalLoudness),
              ResourceHelper.GetString("Toast.Title.MasteringApplied", "Mastering Applied"));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AIMixingMastering.ApplyMasteringFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ApplyFailed", "Apply Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync()
    {
      await AnalyzeAsync();
      StatusMessage = ResourceHelper.GetString("AIMixingMastering.Refreshed", "Refreshed");
    }

  }

  // Data models
  public class AIMixingMixSuggestionItem : ObservableObject
  {
    public string SuggestionId { get; set; }
    public string Category { get; set; }
    public string Priority { get; set; }
    public string Description { get; set; }
    public float? CurrentValue { get; set; }
    public float? SuggestedValue { get; set; }
    public float Confidence { get; set; }

    public string PriorityDisplay => Priority.ToUpper();
    public string ConfidenceDisplay => $"{Confidence:P0}";
    public bool IsHighPriority => Priority == "high";
    public Visibility HighPriorityVisibility => IsHighPriority ? Visibility.Visible : Visibility.Collapsed;
    public string ValueChangeDisplay => CurrentValue.HasValue && SuggestedValue.HasValue
        ? $"{CurrentValue:F2} → {SuggestedValue:F2}"
        : "";
    public Visibility ValueChangeVisibility => string.IsNullOrEmpty(ValueChangeDisplay) ? Visibility.Collapsed : Visibility.Visible;

    public AIMixingMixSuggestionItem(MixSuggestionData data)
    {
      SuggestionId = data.SuggestionId;
      Category = data.Category;
      Priority = data.Priority;
      Description = data.Description;
      CurrentValue = data.CurrentValue;
      SuggestedValue = data.SuggestedValue;
      Confidence = data.Confidence;
    }
  }

  public class MixReportCardItem : ObservableObject
  {
    public string ProjectId { get; set; }
    public float CurrentLoudness { get; set; }
    public float TargetLoudness { get; set; }
    public float PeakLevel { get; set; }
    public float DynamicRange { get; set; }
    public Dictionary<string, float>? FrequencyBalance { get; set; }
    public List<Dictionary<string, object>>? Suggestions { get; set; }

    public string LoudnessDisplay => $"{CurrentLoudness:F1} LUFS (target: {TargetLoudness:F1})";
    public string PeakDisplay => $"{PeakLevel:F1} dB";
    public string DynamicRangeDisplay => $"{DynamicRange:F1} dB";
    public bool LoudnessMet => Math.Abs(CurrentLoudness - TargetLoudness) < 1.0f;
    public Visibility LoudnessMetVisibility => LoudnessMet ? Visibility.Visible : Visibility.Collapsed;

    public MixReportCardItem(MasteringAnalysisResponse response)
    {
      ProjectId = response.ProjectId;
      CurrentLoudness = response.CurrentLoudness;
      TargetLoudness = response.TargetLoudness;
      PeakLevel = response.PeakLevel;
      DynamicRange = response.DynamicRange;
      FrequencyBalance = response.FrequencyBalance;
      Suggestions = response.Suggestions;
    }
  }
}