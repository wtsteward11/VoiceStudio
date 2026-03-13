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

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the UltimateDashboardView panel - Master dashboard aggregating all data.
  /// </summary>
  public partial class UltimateDashboardViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IUltimateDashboardClient _dashboardClient;

    public string PanelId => "ultimate-dashboard";
    public string DisplayName => ResourceHelper.GetString("Panel.UltimateDashboard.DisplayName", "Ultimate Dashboard");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private DashboardSummaryItem? summary;

    [ObservableProperty]
    private ObservableCollection<QuickStatItem> quickStats = new();

    [ObservableProperty]
    private ObservableCollection<RecentActivityItem> recentActivities = new();

    [ObservableProperty]
    private ObservableCollection<string> systemAlerts = new();

    public UltimateDashboardViewModel(IViewModelContext context, IUltimateDashboardClient dashboardClient)
        : base(context)
    {
      _dashboardClient = dashboardClient ?? throw new ArgumentNullException(nameof(dashboardClient));

      LoadDashboardCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadDashboard");
        await LoadDashboardAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default) =>
      LoadDashboardAsync(cancellationToken);

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
      RefreshAsyncInternal(cancellationToken);

    public IAsyncRelayCommand LoadDashboardCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadDashboardAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var data = await _dashboardClient.GetDashboardAsync(cancellationToken);

        if (data != null)
        {
          Summary = new DashboardSummaryItem(data.Summary);

          QuickStats.Clear();
          foreach (var stat in data.QuickStats)
          {
            QuickStats.Add(new QuickStatItem(stat));
          }

          RecentActivities.Clear();
          foreach (var activity in data.RecentActivities)
          {
            RecentActivities.Add(new RecentActivityItem(activity));
          }

          SystemAlerts.Clear();
          foreach (var alert in data.SystemAlerts)
          {
            SystemAlerts.Add(alert);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadDashboard");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsyncInternal(CancellationToken cancellationToken)
    {
      await LoadDashboardAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("UltimateDashboard.DashboardRefreshed", "Dashboard refreshed");
    }
  }

  // Data models
  public class DashboardSummaryItem : ObservableObject
  {
    public int TotalProjects { get; set; }
    public int TotalProfiles { get; set; }
    public int TotalAudioFiles { get; set; }
    public int ActiveJobs { get; set; }
    public int CompletedJobsToday { get; set; }
    public string SystemStatus { get; set; }
    public bool GpuAvailable { get; set; }
    public double GpuUtilization { get; set; }
    public double CpuUtilization { get; set; }
    public double MemoryUsagePercent { get; set; }

    public string SystemStatusDisplay => SystemStatus.ToUpper();
    public string GpuUtilizationDisplay => $"{GpuUtilization:F1}%";
    public string CpuUtilizationDisplay => $"{CpuUtilization:F1}%";
    public string MemoryUsageDisplay => $"{MemoryUsagePercent:F1}%";

    public DashboardSummaryItem(UltimateDashboardSummary summary)
    {
      TotalProjects = summary.TotalProjects;
      TotalProfiles = summary.TotalProfiles;
      TotalAudioFiles = summary.TotalAudioFiles;
      ActiveJobs = summary.ActiveJobs;
      CompletedJobsToday = summary.CompletedJobsToday;
      SystemStatus = summary.SystemStatus;
      GpuAvailable = summary.GpuAvailable;
      GpuUtilization = summary.GpuUtilization;
      CpuUtilization = summary.CpuUtilization;
      MemoryUsagePercent = summary.MemoryUsagePercent;
    }
  }

  public class QuickStatItem : ObservableObject
  {
    public string StatId { get; set; }
    public string Label { get; set; }
    public string Value { get; set; }
    public string? Trend { get; set; }
    public double? TrendValue { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }

    public string TrendDisplay => Trend switch
    {
      "up" => $"↑ {TrendValue:F1}%",
      "down" => $"↓ {Math.Abs(TrendValue ?? 0):F1}%",
      _ => "→"
    };

    public QuickStatItem(UltimateQuickStat stat)
    {
      StatId = stat.StatId;
      Label = stat.Label;
      Value = stat.Value;
      Trend = stat.Trend;
      TrendValue = stat.TrendValue;
      Icon = stat.Icon;
      Color = stat.Color;
    }
  }

  public class RecentActivityItem : ObservableObject
  {
    public string ActivityId { get; set; }
    public string Type { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public string Timestamp { get; set; }

    public string TimestampDisplay => FormatTimestamp(Timestamp);
    public string TypeIcon => Type switch
    {
      "synthesis_completed" => "✨",
      "project_created" => "📁",
      "profile_created" => "🎤",
      "batch_completed" => "⚙️",
      _ => "📋"
    };

    public RecentActivityItem(UltimateRecentActivity activity)
    {
      ActivityId = activity.ActivityId;
      Type = activity.Type;
      Title = activity.Title;
      Description = activity.Description;
      Timestamp = activity.Timestamp;
    }

    private static string FormatTimestamp(string isoString)
    {
      if (DateTime.TryParse(isoString, out var dateTime))
      {
        var now = DateTime.UtcNow;
        var diff = now - dateTime;

        if (diff.TotalMinutes < 1)
          return ResourceHelper.GetString("UltimateDashboard.TimeJustNow", "Just now");
        if (diff.TotalMinutes < 60)
          return ResourceHelper.FormatString("UltimateDashboard.TimeMinutesAgo", (int)diff.TotalMinutes);
        if (diff.TotalHours < 24)
          return ResourceHelper.FormatString("UltimateDashboard.TimeHoursAgo", (int)diff.TotalHours);
        if (diff.TotalDays < 7)
          return ResourceHelper.FormatString("UltimateDashboard.TimeDaysAgo", (int)diff.TotalDays);

        return dateTime.ToString("MMM dd, HH:mm");
      }
      return isoString;
    }
  }
}