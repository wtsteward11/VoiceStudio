using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceStudio.App.ViewModels
{
  public partial class StatusBarViewModel : ObservableObject
  {
    [ObservableProperty]
    private int cpuPercent;

    [ObservableProperty]
    private int gpuPercent;

    [ObservableProperty]
    private int ramPercent;

    [ObservableProperty]
    private int latencyMs = -1;

    [ObservableProperty]
    private bool isBackendConnected;

    [ObservableProperty]
    private double currentJobProgress;

    [ObservableProperty]
    private int runningJobCount;

    [ObservableProperty]
    private string jobStatusText = "Idle";

    [ObservableProperty]
    private string clockText = "--:--";

    public bool HasLatency => LatencyMs >= 0;

    public string LatencyDisplay => LatencyMs >= 0 ? $"{LatencyMs}ms" : "--ms";

    partial void OnLatencyMsChanged(int value)
    {
      OnPropertyChanged(nameof(HasLatency));
      OnPropertyChanged(nameof(LatencyDisplay));
    }

    partial void OnRunningJobCountChanged(int value)
    {
      UpdateJobStatusText();
    }

    partial void OnCurrentJobProgressChanged(double value)
    {
      UpdateJobStatusText();
    }

    private void UpdateJobStatusText()
    {
      if (RunningJobCount > 0)
      {
        var pct = (int)(CurrentJobProgress * 100);
        JobStatusText = RunningJobCount == 1
          ? $"Running ({pct}%)"
          : $"{RunningJobCount} Running ({pct}%)";
      }
      else
      {
        JobStatusText = "Idle";
      }
    }
  }
}
