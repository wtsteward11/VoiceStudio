using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using IRelayCommand = CommunityToolkit.Mvvm.Input.IRelayCommand;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the QualityOptimizationWizardView panel - Voice Profile Quality Optimization Wizard.
  /// Implements IDEA 43: Voice Profile Quality Optimization Wizard.
  /// </summary>
  public partial class QualityOptimizationWizardViewModel : BaseViewModel, IPanelView
  {
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly IQualityControlClient _qualityClient;
    private readonly IProfilesClient _profilesClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.QualityOptimizer;
    public string DisplayName => ResourceHelper.GetString("Panel.QualityOptimizationWizard.DisplayName", "Quality Optimization Wizard");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> availableProfiles = new();

    [ObservableProperty]
    private VoiceProfile? selectedProfile;

    [ObservableProperty]
    private string targetTier = "standard";

    [ObservableProperty]
    private ObservableCollection<string> availableTiers = new() { "fast", "standard", "high", "ultra", "professional" };

    [ObservableProperty]
    private int currentStep = 1;

    [ObservableProperty]
    private int totalSteps = 5;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool IsStep5 => CurrentStep == 5;

    [ObservableProperty]
    private bool canGoNext;

    [ObservableProperty]
    private bool canGoBack;

    [ObservableProperty]
    private QualityAnalysisResult? analysisResult;

    [ObservableProperty]
    private QualityOptimizationResult? optimizationResult;

    [ObservableProperty]
    private bool isAnalyzing;

    [ObservableProperty]
    private bool isOptimizing;

    [ObservableProperty]
    private string? testText = "Hello, this is a test of the voice profile quality.";

    public QualityOptimizationWizardViewModel(IViewModelContext context, IVoiceSynthesisService voiceSynthesisService, IQualityControlClient qualityClient, IProfilesClient profilesClient)
        : base(context)
    {
      _voiceSynthesisService = voiceSynthesisService ?? throw new ArgumentNullException(nameof(voiceSynthesisService));
      _qualityClient = qualityClient ?? throw new ArgumentNullException(nameof(qualityClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

      // Get toast notification service using helper (reduces code duplication)
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());

      LoadProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProfiles");
        await LoadProfilesAsync(ct);
      }, () => !IsLoading);
      AnalyzeQualityCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AnalyzeQuality");
        await AnalyzeQualityAsync(ct);
      }, () => SelectedProfile != null && !IsAnalyzing);
      OptimizeQualityCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("OptimizeQuality");
        await OptimizeQualityAsync(ct);
      }, () => AnalysisResult != null && !IsOptimizing);
      NextStepCommand = new RelayCommand(NextStep, () => CanGoNext);
      PreviousStepCommand = new RelayCommand(PreviousStep, () => CanGoBack);
      ResetWizardCommand = new RelayCommand(ResetWizard);

      // Initial data load deferred to InitializeAsync (ADR-047)
    }

    private bool _isInitialized;

    /// <summary>
    /// One-shot initialization. Call from View Loaded. Loads profiles.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      if (_isInitialized)
        return;
      _isInitialized = true;
      await LoadProfilesAsync(cancellationToken);
    }

    public IAsyncRelayCommand LoadProfilesCommand { get; }
    public IAsyncRelayCommand AnalyzeQualityCommand { get; }
    public IAsyncRelayCommand OptimizeQualityCommand { get; }
    public IRelayCommand NextStepCommand { get; }
    public IRelayCommand PreviousStepCommand { get; }
    public IRelayCommand ResetWizardCommand { get; }

    partial void OnSelectedProfileChanged(VoiceProfile? value)
    {
      AnalyzeQualityCommand.NotifyCanExecuteChanged();
      UpdateStepNavigation();
    }

    partial void OnCurrentStepChanged(int value)
    {
      UpdateStepNavigation();
      OnPropertyChanged(nameof(IsStep1));
      OnPropertyChanged(nameof(IsStep2));
      OnPropertyChanged(nameof(IsStep3));
      OnPropertyChanged(nameof(IsStep4));
      OnPropertyChanged(nameof(IsStep5));
    }

    partial void OnAnalysisResultChanged(QualityAnalysisResult? value)
    {
      OptimizeQualityCommand.NotifyCanExecuteChanged();
      UpdateStepNavigation();
    }

    private void UpdateStepNavigation()
    {
      CanGoNext = CurrentStep < TotalSteps && (
                 (CurrentStep == 1 && SelectedProfile != null) ||
                 (CurrentStep == 2 && AnalysisResult != null) ||
                 (CurrentStep == 3 && AnalysisResult != null) ||
                 (CurrentStep == 4 && OptimizationResult != null));
      CanGoBack = CurrentStep > 1;
      NextStepCommand.NotifyCanExecuteChanged();
      PreviousStepCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);

        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableProfiles.Add(profile);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityOptimizationWizard.LoadProfilesFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("QualityOptimizationWizard.LoadProfilesFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.LoadProfilesFailed", "Load Profiles Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AnalyzeQualityAsync(CancellationToken cancellationToken)
    {
      var selectedProfile = SelectedProfile;
      if (selectedProfile == null)
        return;

      var targetTier = string.IsNullOrWhiteSpace(TargetTier) ? "standard" : TargetTier;
      var testText = string.IsNullOrWhiteSpace(TestText)
          ? "Hello, this is a test."
          : TestText;

      IsAnalyzing = true;
      ErrorMessage = null;
      AnalyzeQualityCommand.NotifyCanExecuteChanged();

      try
      {
        // First, synthesize with the profile to get current quality metrics
        var synthesizeRequest = new VoiceSynthesisRequest
        {
          ProfileId = selectedProfile.Id,
          Text = testText,
          Engine = "xtts",
          Language = selectedProfile.Language ?? "en"
        };

        var synthesizeResponse = await _voiceSynthesisService.SynthesizeVoiceAsync(synthesizeRequest, cancellationToken);

        if (synthesizeResponse?.QualityMetrics == null)
        {
          ErrorMessage = ResourceHelper.GetString("QualityOptimizationWizard.GetQualityMetricsFailed", "Failed to get quality metrics from synthesis");
          return;
        }

        // Analyze quality
        var analyzeRequest = new QualityAnalysisRequest
        {
          MosScore = synthesizeResponse.QualityMetrics.MosScore,
          Similarity = synthesizeResponse.QualityMetrics.Similarity,
          Naturalness = synthesizeResponse.QualityMetrics.Naturalness,
          SnrDb = synthesizeResponse.QualityMetrics.SnrDb,
          TargetTier = targetTier
        };

        var analysis = await _qualityClient.AnalyzeQualityAsync(analyzeRequest, cancellationToken);

        if (analysis != null)
        {
          AnalysisResult = new QualityAnalysisResult
          {
            MeetsTarget = analysis.MeetsTarget,
            QualityScore = analysis.QualityScore,
            Deficiencies = analysis.Deficiencies?.Select(d => new QualityDeficiency
            {
              Metric = d.ContainsKey("metric") ? d["metric"]?.ToString() ?? "" : "",
              CurrentValue = d.ContainsKey("current_value") && d["current_value"] is double cv ? cv : 0,
              TargetValue = d.ContainsKey("target_value") && d["target_value"] is double tv ? tv : 0,
              Severity = d.ContainsKey("severity") ? d["severity"]?.ToString() ?? "" : ""
            }).ToList() ?? new(),
            Recommendations = analysis.Recommendations?.Select(r => new QualityRecommendation
            {
              Title = r.ContainsKey("title") ? r["title"]?.ToString() ?? "" : "",
              Description = r.ContainsKey("description") ? r["description"]?.ToString() ?? "" : "",
              Action = r.ContainsKey("action") ? r["action"]?.ToString() ?? "" : "",
              Priority = r.ContainsKey("priority") ? r["priority"]?.ToString() ?? "" : ""
            }).ToList() ?? new(),
            CurrentMetrics = synthesizeResponse.QualityMetrics
          };

          // Move to next step
          if (CurrentStep == 2)
          {
            CurrentStep = 3;
          }

          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("QualityOptimizationWizard.AnalysisCompleted", "Quality analysis completed"),
              ResourceHelper.GetString("Toast.Title.AnalysisComplete", "Analysis Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AnalyzeQuality");
      }
      finally
      {
        IsAnalyzing = false;
        AnalyzeQualityCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task OptimizeQualityAsync(CancellationToken cancellationToken)
    {
      var analysisResult = AnalysisResult;
      if (analysisResult == null || SelectedProfile == null)
        return;

      var targetTier = string.IsNullOrWhiteSpace(TargetTier) ? "standard" : TargetTier;

      try
      {
        IsOptimizing = true;
        ErrorMessage = null;
        OptimizeQualityCommand.NotifyCanExecuteChanged();

        var metrics = new Dictionary<string, object>();
        if (analysisResult.CurrentMetrics?.MosScore.HasValue == true)
          metrics["mos_score"] = analysisResult.CurrentMetrics.MosScore.Value;
        if (analysisResult.CurrentMetrics?.Similarity.HasValue == true)
          metrics["similarity"] = analysisResult.CurrentMetrics.Similarity.Value;
        if (analysisResult.CurrentMetrics?.Naturalness.HasValue == true)
          metrics["naturalness"] = analysisResult.CurrentMetrics.Naturalness.Value;
        if (analysisResult.CurrentMetrics?.SnrDb.HasValue == true)
          metrics["snr_db"] = analysisResult.CurrentMetrics.SnrDb.Value;

        var currentParams = new Dictionary<string, object>
        {
          ["engine"] = "xtts",
          ["quality_mode"] = targetTier
        };

        var optimizeRequest = new QualityOptimizationRequest
        {
          Metrics = metrics,
          CurrentParams = currentParams,
          TargetTier = targetTier
        };

        var optimization = await _qualityClient.OptimizeQualityAsync(optimizeRequest, cancellationToken);

        if (optimization != null)
        {
          OptimizationResult = new QualityOptimizationResult
          {
            OptimizedParams = optimization.OptimizedParams ?? new(),
            Analysis = optimization.Analysis ?? new()
          };

          // Move to next step
          if (CurrentStep == 4)
          {
            CurrentStep = 5;
          }

          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("QualityOptimizationWizard.OptimizationCompleted", "Quality optimization completed"),
              ResourceHelper.GetString("Toast.Title.OptimizationComplete", "Optimization Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityOptimizationWizard.OptimizeQualityFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("QualityOptimizationWizard.OptimizeQualityFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.OptimizationFailed", "Optimization Failed"));
        await HandleErrorAsync(ex, "OptimizeQuality", showDialog: false);
      }
      finally
      {
        IsOptimizing = false;
        OptimizeQualityCommand.NotifyCanExecuteChanged();
      }
    }

    private void NextStep()
    {
      if (CanGoNext && CurrentStep < TotalSteps)
      {
        CurrentStep++;

        // Auto-trigger actions when moving to certain steps
        if (CurrentStep == 2 && SelectedProfile != null)
        {
          _ = AnalyzeQualityAsync(CancellationToken.None);
        }
        else if (CurrentStep == 4 && AnalysisResult != null)
        {
          _ = OptimizeQualityAsync(CancellationToken.None);
        }
      }
    }

    private void PreviousStep()
    {
      if (CanGoBack && CurrentStep > 1)
      {
        CurrentStep--;
      }
    }

    private void ResetWizard()
    {
      CurrentStep = 1;
      SelectedProfile = null;
      AnalysisResult = null;
      OptimizationResult = null;
      TargetTier = "standard";
      ErrorMessage = null;
    }
  }

  /// <summary>
  /// Data models.
  /// </summary>
  public class QualityAnalysisResult : ObservableObject
  {
    public bool MeetsTarget { get; set; }
    public double QualityScore { get; set; }
    public List<QualityDeficiency> Deficiencies { get; set; } = new();
    public List<QualityRecommendation> Recommendations { get; set; } = new();
    public QualityMetrics? CurrentMetrics { get; set; }
    public string QualityScoreDisplay => $"{QualityScore:F2}/5.0";
  }

  public class QualityDeficiency : ObservableObject
  {
    public string Metric { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public double TargetValue { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Display => ResourceHelper.FormatString("QualityOptimizationWizard.DeficiencyDisplay", Metric, CurrentValue, TargetValue);
  }

  public class QualityRecommendation : ObservableObject
  {
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
  }

  public class QualityOptimizationResult : ObservableObject
  {
    public Dictionary<string, object> OptimizedParams { get; set; } = new();
    public Dictionary<string, object> Analysis { get; set; } = new();
    public string OptimizedParamsDisplay => string.Join("\n", OptimizedParams.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
  }
}