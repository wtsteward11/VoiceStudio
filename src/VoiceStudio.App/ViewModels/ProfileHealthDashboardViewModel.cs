using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for Voice Profile Health Dashboard.
  /// Implements IDEA 35: Voice Profile Health Dashboard.
  /// </summary>
  public partial class ProfileHealthDashboardViewModel : BaseViewModel, IPanelView
  {
    public string PanelId => "profile-health-dashboard";
    public string DisplayName => ResourceHelper.GetString("Panel.ProfileHealthDashboard.DisplayName", "Profile Health Dashboard");
    public PanelRegion Region => PanelRegion.Center;
    private readonly IBackendClient _backendClient;

    [ObservableProperty]
    private ObservableCollection<ProfileHealthItem> profiles = new();

    [ObservableProperty]
    private ProfileHealthItem? selectedProfile;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string statusMessage = ResourceHelper.GetString("ProfileHealthDashboard.Ready", "Ready");

    [ObservableProperty]
    private int totalProfiles;

    [ObservableProperty]
    private int healthyProfiles;

    [ObservableProperty]
    private int degradedProfiles;

    [ObservableProperty]
    private int criticalProfiles;

    public ProfileHealthDashboardViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RefreshHealthData");
        await RefreshHealthDataAsync(ct);
      }, () => !IsLoading);
      CheckSelectedProfileCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CheckSelectedProfileHealth");
        await CheckSelectedProfileHealthAsync(ct);
      }, () => SelectedProfile != null && !IsLoading);
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CheckSelectedProfileCommand { get; }

    partial void OnSelectedProfileChanged(ProfileHealthItem? value)
    {
      CheckSelectedProfileCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadHealthDataAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("ProfileHealthDashboard.LoadingHealthData", "Loading profile health data...");

      try
      {
        // Load all profiles
        var allProfiles = await _backendClient.GetProfilesAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        Profiles.Clear();
        TotalProfiles = allProfiles.Count;
        HealthyProfiles = 0;
        DegradedProfiles = 0;
        CriticalProfiles = 0;

        // Check health for each profile
        foreach (var profile in allProfiles)
        {
          cancellationToken.ThrowIfCancellationRequested();

          var healthItem = new ProfileHealthItem
          {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            CurrentQuality = profile.QualityScore,
            Language = profile.Language ?? "en",
            Tags = profile.Tags ?? new List<string>()
          };

          // Check for degradation
          try
          {
            var degradation = await _backendClient.GetQualityDegradationAsync(
                profile.Id,
                timeWindowDays: 7,
                degradationThresholdPercent: 10.0,
                criticalThresholdPercent: 25.0,
                cancellationToken);

            if (degradation?.HasDegradation == true)
            {
              healthItem.HasDegradation = true;
              healthItem.DegradationAlerts = degradation.Alerts ?? new List<QualityDegradationAlert>();

              // Determine severity
              var maxDegradation = degradation.Alerts?.Max(a => a.DegradationPercent) ?? 0.0;
              if (maxDegradation >= 25.0)
              {
                healthItem.HealthStatus = HealthStatus.Critical;
                CriticalProfiles++;
              }
              else
              {
                healthItem.HealthStatus = HealthStatus.Degraded;
                DegradedProfiles++;
              }
            }
            else
            {
              healthItem.HealthStatus = HealthStatus.Healthy;
              HealthyProfiles++;
            }

            // Get quality baseline
            var baseline = await _backendClient.GetQualityBaselineAsync(profile.Id, timePeriodDays: 30, cancellationToken);
            if (baseline != null)
            {
              healthItem.BaselineQuality = baseline.BaselineQualityScore;
              if (DateTime.TryParse(baseline.CalculatedAt, out var calcDt))
                healthItem.BaselineDate = calcDt;
            }

            // Get quality trends
            var trends = await _backendClient.GetQualityTrendsAsync(profile.Id, timeRange: "30d", cancellationToken);
            if (trends != null)
            {
              healthItem.Trend = trends.OverallTrend ?? "stable";
              healthItem.TrendData = trends.MetricsOverTime ?? new Dictionary<string, List<double>>();
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation
          }
          catch (Exception ex)
          {
            // Log error but continue with other profiles
            System.Diagnostics.Debug.WriteLine($"Error checking health for profile {profile.Id}: {ex.Message}");
            healthItem.HealthStatus = HealthStatus.Unknown;
          }

          Profiles.Add(healthItem);
        }

        StatusMessage = $"Loaded {TotalProfiles} profiles. {HealthyProfiles} healthy, {DegradedProfiles} degraded, {CriticalProfiles} critical.";
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadHealthData");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshHealthDataAsync(CancellationToken cancellationToken)
    {
      await LoadHealthDataAsync(cancellationToken);
    }

    private async Task CheckSelectedProfileHealthAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.FormatString("ProfileHealthDashboard.CheckingHealth", SelectedProfile.ProfileName);

      try
      {
        // Re-check degradation
        var degradation = await _backendClient.GetQualityDegradationAsync(
            SelectedProfile.ProfileId,
            timeWindowDays: 7,
            degradationThresholdPercent: 10.0,
            criticalThresholdPercent: 25.0,
            cancellationToken);

        if (degradation?.HasDegradation == true)
        {
          SelectedProfile.HasDegradation = true;
          SelectedProfile.DegradationAlerts = degradation.Alerts ?? new List<QualityDegradationAlert>();

          var maxDegradation = degradation.Alerts?.Max(a => a.DegradationPercent) ?? 0.0;
          if (maxDegradation >= 25.0)
          {
            SelectedProfile.HealthStatus = HealthStatus.Critical;
          }
          else
          {
            SelectedProfile.HealthStatus = HealthStatus.Degraded;
          }
        }
        else
        {
          SelectedProfile.HasDegradation = false;
          SelectedProfile.HealthStatus = HealthStatus.Healthy;
        }

        // Update baseline
        var baseline = await _backendClient.GetQualityBaselineAsync(SelectedProfile.ProfileId, timePeriodDays: 30, cancellationToken);
        if (baseline != null)
        {
          SelectedProfile.BaselineQuality = baseline.BaselineQualityScore;
          if (DateTime.TryParse(baseline.CalculatedAt, out var calcDt))
          {
            SelectedProfile.BaselineDate = calcDt;
          }
        }

        // Update trends
        var trends = await _backendClient.GetQualityTrendsAsync(SelectedProfile.ProfileId, timeRange: "30d", cancellationToken);
        if (trends != null)
        {
          SelectedProfile.Trend = trends.OverallTrend ?? "stable";
          SelectedProfile.TrendData = trends.MetricsOverTime ?? new Dictionary<string, List<double>>();
        }

        StatusMessage = ResourceHelper.FormatString("ProfileHealthDashboard.HealthCheckComplete", SelectedProfile.ProfileName);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CheckSelectedProfileHealth");
      }
      finally
      {
        IsLoading = false;
      }
    }
  }

  /// <summary>
  /// Health status for a profile.
  /// </summary>
  public enum HealthStatus
  {
    Healthy = 0,
    Degraded = 1,
    Critical = 2,
    Unknown = 3
  }

  /// <summary>
  /// Health information for a voice profile.
  /// </summary>
  public class ProfileHealthItem : ObservableObject
  {
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public double CurrentQuality { get; set; }
    public double? BaselineQuality { get; set; }
    public DateTime? BaselineDate { get; set; }
    public HealthStatus HealthStatus { get; set; } = HealthStatus.Unknown;
    public bool HasDegradation { get; set; }
    public List<QualityDegradationAlert> DegradationAlerts { get; set; } = new();
    public string Trend { get; set; } = "stable";
    public Dictionary<string, List<double>> TrendData { get; set; } = new();
    public string Language { get; set; } = "en";
    public List<string> Tags { get; set; } = new();

    public string HealthStatusText => HealthStatus switch
    {
      HealthStatus.Healthy => ResourceHelper.GetString("ProfileHealthDashboard.HealthStatusHealthy", "Healthy"),
      HealthStatus.Degraded => ResourceHelper.GetString("ProfileHealthDashboard.HealthStatusDegraded", "Degraded"),
      HealthStatus.Critical => ResourceHelper.GetString("ProfileHealthDashboard.HealthStatusCritical", "Critical"),
      _ => ResourceHelper.GetString("ProfileHealthDashboard.HealthStatusUnknown", "Unknown")
    };

    public string HealthStatusColor => HealthStatus switch
    {
      HealthStatus.Healthy => "#4CAF50",
      HealthStatus.Degraded => "#FF9800",
      HealthStatus.Critical => "#F44336",
      _ => "#9E9E9E"
    };
  }
}