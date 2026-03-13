using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Engine Recommendation panel.
  /// Implements IDEA 47: Quality-Based Engine Recommendation System.
  /// </summary>
  public partial class EngineRecommendationViewModel : BaseViewModel, IPanelView
  {
    private readonly IQualityControlClient _qualityClient;

    public string PanelId => "engine_recommendation";
    public string DisplayName => ResourceHelper.GetString("Panel.EngineRecommendations.DisplayName", "Engine Recommendations");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private double? minMosScore;

    [ObservableProperty]
    private double? minSimilarity;

    [ObservableProperty]
    private double? minNaturalness;

    [ObservableProperty]
    private bool preferSpeed;

    [ObservableProperty]
    private string? qualityTier;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private ObservableCollection<EngineRecommendation> recommendations = new();

    public bool HasRecommendations => Recommendations?.Count > 0;

    public bool CanGetRecommendations => !IsLoading;

    public IAsyncRelayCommand GetRecommendationsCommand { get; }

    public EngineRecommendationViewModel(IViewModelContext context, IQualityControlClient qualityClient)
        : base(context)
    {
      _qualityClient = qualityClient ?? throw new ArgumentNullException(nameof(qualityClient));

      GetRecommendationsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GetEngineRecommendations");
        await GetRecommendationsAsync(ct);
      }, () => CanGetRecommendations);
    }

    private async Task GetRecommendationsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      HasError = false;
      ErrorMessage = null;

      try
      {
        var request = new EngineRecommendationRequest
        {
          TaskType = "tts",
          MinMosScore = MinMosScore,
          MinSimilarity = MinSimilarity,
          MinNaturalness = MinNaturalness,
          PreferSpeed = PreferSpeed,
          QualityTier = QualityTier
        };

        var response = await _qualityClient.GetEngineRecommendationAsync(request, cancellationToken);

        Recommendations.Clear();
        foreach (var recommendation in response.Recommendations)
        {
          Recommendations.Add(recommendation);
        }

        OnPropertyChanged(nameof(HasRecommendations));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to get recommendations: {ex.Message}";
        HasError = true;
        await HandleErrorAsync(ex, "GetRecommendations");
      }
      finally
      {
        IsLoading = false;
        GetRecommendationsCommand.NotifyCanExecuteChanged();
      }
    }
  }
}