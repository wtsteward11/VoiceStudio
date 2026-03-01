using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using SelectionChangedEventArgs = global::Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UIColors = Microsoft.UI.Colors;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class DiagnosticsView : UserControl
  {
    public DiagnosticsViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private IErrorLoggingService? _errorLoggingService;
    private IUnifiedCommandRegistry? _commandRegistry;
    private ObservableCollection<CommandHealthItem> _commandHealthItems = new();
    private DispatcherTimer? _resourceMonitorTimer;
    private PerformanceCounter? _cpuCounter;
    private ObservableCollection<JobQueueItem> _jobQueueItems = new();
    private GPUStatusResponse? _cachedGpuStatus;
    private DateTime _lastGpuUpdate = DateTime.MinValue;
    private readonly TimeSpan _gpuUpdateInterval = TimeSpan.FromSeconds(5);
    private ObservableCollection<CircuitBreakerItem> _circuitBreakerItems = new();
    private ObservableCollection<LogEntry> _filteredLogs = new();

    public DiagnosticsView()
    {
      this.InitializeComponent();
      // Wire DataContext with BackendClient from service provider
      ViewModel = new DiagnosticsViewModel(AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(), ServiceProvider.GetBackendClient());
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _errorLoggingService = AppServices.TryGetErrorLoggingService();
      _commandRegistry = AppServices.TryGetCommandRegistry();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(DiagnosticsViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Diagnostics Error", ViewModel.ErrorMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += DiagnosticsView_KeyboardNavigation_Loaded;
      this.Unloaded += DiagnosticsView_Unloaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Hide();
        }
      });

      // Initialize resource monitoring
      InitializeResourceMonitoring();

      // Wire logs display and filtering
      LogsListView.ItemsSource = ViewModel.Logs;
      ViewModel.Logs.CollectionChanged += (_, _) =>
      {
        if (IsLogFilterActive())
          ApplyLogFilters();
      };

      // Set backend URL from actual configuration
      try
      {
        var backendAddress = ServiceProvider.GetBackendClient().BaseAddress;
        BaseUrlText.Text = backendAddress?.ToString() ?? "Not configured";
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "DiagnosticsView_ctor_BackendUrl");
        BaseUrlText.Text = "Not available";
      }
    }

    private void DiagnosticsView_Unloaded(object sender, RoutedEventArgs e)
    {
      // Cleanup resources
      _resourceMonitorTimer?.Stop();
      _cpuCounter?.Dispose();
    }

    private void InitializeResourceMonitoring()
    {
      try
      {
        // Initialize CPU counter
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _cpuCounter.NextValue(); // First call always returns 0, so call it once to prime

        // Setup timer for resource updates (every 2 seconds)
        _resourceMonitorTimer = new DispatcherTimer
        {
          Interval = TimeSpan.FromSeconds(2)
        };
        _resourceMonitorTimer.Tick += ResourceMonitorTimer_Tick;
        _resourceMonitorTimer.Start();

        // Initial update
        UpdateResourceGauges();
        _ = UpdateJobQueueAsync();
        _ = UpdateGpuMetricsAsync();
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "InitializeResourceMonitoring");
      }
    }

    private void ResourceMonitorTimer_Tick(object? sender, object e)
    {
      UpdateResourceGauges();
      
      // Update GPU metrics asynchronously on a slower interval
      if (DateTime.Now - _lastGpuUpdate > _gpuUpdateInterval)
      {
        _ = UpdateGpuMetricsAsync();
      }
    }

    private async Task UpdateGpuMetricsAsync()
    {
      try
      {
        var backendClient = ServiceProvider.GetBackendClient();
        if (backendClient == null) return;

        var gpuStatus = await backendClient.GetAsync<GPUStatusResponse>("/api/gpu-status");
        if (gpuStatus != null)
        {
          _cachedGpuStatus = gpuStatus;
          _lastGpuUpdate = DateTime.Now;

          // Update UI on dispatcher thread
          DispatcherQueue.TryEnqueue(() =>
          {
            if (gpuStatus.Devices?.Count > 0)
            {
              var primary = gpuStatus.Devices[0];
              var memUsedGb = primary.MemoryUsedMb / 1024.0;
              var memTotalGb = primary.MemoryTotalMb / 1024.0;
              var memPercent = memTotalGb > 0 ? (memUsedGb / memTotalGb) * 100 : 0;

              GpuNameText.Text = primary.Name ?? "Unknown GPU";
              GpuMemoryUsageText.Text = $"{memUsedGb:F1} GB";
              GpuMemoryProgressBar.Value = memPercent;
              GpuMemoryDetailsText.Text = $"{memUsedGb:F1} / {memTotalGb:F1} GB";
              GpuLoadText.Text = $"{primary.UtilizationPercent:F0}%";
              GpuLoadProgressBar.Value = primary.UtilizationPercent;
            }
            else
            {
              GpuNameText.Text = "No GPU detected";
              GpuMemoryUsageText.Text = "N/A";
              GpuLoadText.Text = "N/A";
            }
          });
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "UpdateGpuMetricsAsync");
      }
    }

    private void UpdateResourceGauges()
    {
      try
      {
        // Update CPU usage
        if (_cpuCounter != null)
        {
          var cpuUsage = _cpuCounter.NextValue();
          CpuUsageText.Text = $"{cpuUsage:F0}%";
          CpuProgressBar.Value = cpuUsage;
        }

        // Update Memory usage
        var memInfo = GetMemoryInfo();
        if (memInfo.HasValue)
        {
          var (usedGb, totalGb, percentUsed) = memInfo.Value;
          MemoryUsageText.Text = $"{usedGb:F1} GB";
          MemoryProgressBar.Value = percentUsed;
          MemoryDetailsText.Text = $"{usedGb:F1} / {totalGb:F1} GB";
        }

        // Update GPU metrics
        var gpuInfo = GetGpuInfo();
        if (gpuInfo.HasValue)
        {
          var (gpuMemUsedGb, gpuMemTotalGb, gpuMemPercent, gpuLoad, gpuName) = gpuInfo.Value;
          GpuMemoryUsageText.Text = $"{gpuMemUsedGb:F1} GB";
          GpuMemoryProgressBar.Value = gpuMemPercent;
          GpuMemoryDetailsText.Text = $"{gpuMemUsedGb:F1} / {gpuMemTotalGb:F1} GB";
          GpuLoadText.Text = $"{gpuLoad:F0}%";
          GpuLoadProgressBar.Value = gpuLoad;
          GpuNameText.Text = gpuName;
        }
        else
        {
          GpuMemoryUsageText.Text = "N/A";
          GpuLoadText.Text = "N/A";
          GpuNameText.Text = "No GPU detected";
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "UpdateResourceGauges");
      }
    }

    // P/Invoke for memory status
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
      public uint dwLength;
      public uint dwMemoryLoad;
      public ulong ullTotalPhys;
      public ulong ullAvailPhys;
      public ulong ullTotalPageFile;
      public ulong ullAvailPageFile;
      public ulong ullTotalVirtual;
      public ulong ullAvailVirtual;
      public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private (double usedGb, double totalGb, double percentUsed)? GetMemoryInfo()
    {
      try
      {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
          var totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
          var usedGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
          var percent = memStatus.dwMemoryLoad;
          return (usedGb, totalGb, percent);
        }
        
        return null;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "GetMemoryInfo");
        return null;
      }
    }

    private (double memUsedGb, double memTotalGb, double memPercent, double load, string name)? GetGpuInfo()
    {
      try
      {
        // Use cached GPU status from backend API
        if (_cachedGpuStatus?.Devices?.Count > 0)
        {
          var primary = _cachedGpuStatus.Devices[0];
          var memUsedGb = primary.MemoryUsedMb / 1024.0;
          var memTotalGb = primary.MemoryTotalMb / 1024.0;
          var memPercent = memTotalGb > 0 ? (memUsedGb / memTotalGb) * 100 : 0;
          return (memUsedGb, memTotalGb, memPercent, primary.UtilizationPercent, primary.Name);
        }
        
        // Return loading state if no cached data yet
        return (0.0, 0.0, 0.0, 0.0, "Loading GPU info...");
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "GetGpuInfo");
        return null;
      }
    }

    private async Task UpdateJobQueueAsync()
    {
      try
      {
        var backendClient = ServiceProvider.GetBackendClient();
        if (backendClient == null) return;

        // Fetch job queue status from backend
        var jobsResponse = await backendClient.GetAsync<JobQueueResponse>("/api/jobs/status");
        
        if (jobsResponse != null)
        {
          QueuedJobsCount.Text = jobsResponse.Queued.ToString();
          RunningJobsCount.Text = jobsResponse.Running.ToString();
          CompletedJobsCount.Text = jobsResponse.Completed.ToString();
          FailedJobsCount.Text = jobsResponse.Failed.ToString();

          _jobQueueItems.Clear();
          foreach (var job in jobsResponse.ActiveJobs ?? new List<JobInfo>())
          {
            _jobQueueItems.Add(new JobQueueItem
            {
              JobId = job.JobId,
              JobType = job.JobType,
              Status = job.Status,
              Progress = job.Progress,
              StartTime = job.StartTime
            });
          }
          
          ActiveJobsListView.ItemsSource = _jobQueueItems;
          NoActiveJobsText.Visibility = _jobQueueItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "UpdateJobQueueAsync");
        // Show empty state on error
        NoActiveJobsText.Visibility = Visibility.Visible;
      }
    }

    private void RefreshJobs_Click(object sender, RoutedEventArgs e)
    {
      _ = UpdateJobQueueAsync();
    }

    private void DiagnosticsView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);

      // Setup tab selection changed handler
      DiagnosticsTabView.SelectionChanged += TabView_SelectionChanged;

      // Initialize System Info tab as visible (default selected)
      SystemInfoGrid.Visibility = Visibility.Visible;

      // Configure virtualization for all ListViews to optimize large lists
      VirtualizedListHelper.ConfigureListView(LogsListView);
      VirtualizedListHelper.ConfigureListView(TracesListView);
      VirtualizedListHelper.ConfigureListView(EnginesListView);
      VirtualizedListHelper.ConfigureListView(CommandsListView);
      VirtualizedListHelper.ConfigureListView(EnvVarsListView);
    }

    private void TabView_SelectionChanged(object? sender, global::Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
      // Show/hide tab content based on selection
      if (DiagnosticsTabView?.SelectedItem is TabViewItem selectedTab)
      {
        // Hide all content grids
        SystemInfoGrid.Visibility = Visibility.Collapsed;
        LogsGrid.Visibility = Visibility.Collapsed;
        TracesGrid.Visibility = Visibility.Collapsed;
        NetworkGrid.Visibility = Visibility.Collapsed;
        EnginesGrid.Visibility = Visibility.Collapsed;
        CommandsGrid.Visibility = Visibility.Collapsed;
        EnvironmentGrid.Visibility = Visibility.Collapsed;

        // Legacy grids
        ErrorsTabGrid.Visibility = Visibility.Collapsed;
        AuditLogTabGrid.Visibility = Visibility.Collapsed;
        AnalyticsTabGrid.Visibility = Visibility.Collapsed;
        PerformanceTabGrid.Visibility = Visibility.Collapsed;
        FeatureFlagsTabGrid.Visibility = Visibility.Collapsed;
        EnvironmentTabGrid.Visibility = Visibility.Collapsed;

        // Show selected tab grid based on header
        switch (selectedTab.Header?.ToString())
        {
          case "System Info":
            SystemInfoGrid.Visibility = Visibility.Visible;
            break;
          case "Logs":
            LogsGrid.Visibility = Visibility.Visible;
            ApplyLogFilters();
            break;
          case "Traces":
            TracesGrid.Visibility = Visibility.Visible;
            _ = ViewModel.LoadTracesAsync();
            break;
          case "Network":
            NetworkGrid.Visibility = Visibility.Visible;
            break;
          case "Engines":
            EnginesGrid.Visibility = Visibility.Visible;
            _ = LoadCircuitBreakersAsync();
            _ = LoadEnginesAsync();
            break;
          case "Commands":
            CommandsGrid.Visibility = Visibility.Visible;
            LoadCommandHealth();
            break;
          case "Environment":
            EnvironmentGrid.Visibility = Visibility.Visible;
            LoadEnvironmentVariables();
            break;
          case "Errors":
            ErrorsTabGrid.Visibility = Visibility.Visible;
            break;
          case "Audit Log":
            AuditLogTabGrid.Visibility = Visibility.Visible;
            _ = ViewModel.LoadAuditEntriesAsync();
            break;
          case "Analytics":
            AnalyticsTabGrid.Visibility = Visibility.Visible;
            break;
          case "Performance":
            PerformanceTabGrid.Visibility = Visibility.Visible;
            break;
          case "Feature Flags":
            FeatureFlagsTabGrid.Visibility = Visibility.Visible;
            break;
        }
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Diagnostics Help";
      HelpOverlay.HelpText = "The Diagnostics panel provides system health monitoring, telemetry data, and comprehensive logging. Monitor CPU, GPU, and RAM usage in real-time. View application logs and error logs with filtering options. Check system health status and connection status. Use auto-refresh to keep telemetry data up to date. Export error logs for debugging purposes. The panel also shows memory breakdown by component and system performance metrics.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh telemetry" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Select all logs (in log view)" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Clear selection / Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Enable auto-refresh to monitor system metrics continuously");
      HelpOverlay.Tips.Add("Use error log filters to find specific errors or warnings");
      HelpOverlay.Tips.Add("Export error logs to share with support or for debugging");
      HelpOverlay.Tips.Add("Monitor VRAM usage to avoid memory issues during synthesis");
      HelpOverlay.Tips.Add("Check health status to ensure all components are functioning");
      HelpOverlay.Tips.Add("The degraded mode warning indicates system issues that need attention");
      HelpOverlay.Tips.Add("Memory breakdown shows resource usage by UI, audio, and engines");
      HelpOverlay.Tips.Add("Connection status shows backend connectivity and queued operations");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }

    private void RefreshCommands_Click(object sender, RoutedEventArgs e)
    {
      LoadCommandHealth();
    }

    private void LoadCommandHealth()
    {
      if (_commandRegistry == null)
      {
        _errorLoggingService?.LogWarning("Command registry not available", "LoadCommandHealth");
        return;
      }

      try
      {
        var allCommands = _commandRegistry.GetAllCommands();
        _commandHealthItems.Clear();

        int totalCommands = 0;
        int workingCount = 0;
        int brokenCount = 0;
        int totalExecutions = 0;

        foreach (var descriptor in allCommands)
        {
          totalCommands++;
          
          // Get runtime state for this command
          var state = _commandRegistry.GetState(descriptor.Id);
          var status = state?.Status ?? CommandStatus.Unknown;
          var successCount = state?.SuccessCount ?? 0;
          var failureCount = state?.FailureCount ?? 0;
          var avgMs = state?.AverageExecutionMs ?? 0.0;
          var lastError = state?.LastError;
          var lastExecuted = state?.LastExecuted;
          
          // Check live CanExecute status
          var canExecute = _commandRegistry.CanExecute(descriptor.Id, null);
          
          if (status == CommandStatus.Working)
            workingCount++;
          else if (status == CommandStatus.Broken)
            brokenCount++;

          totalExecutions += successCount + failureCount;

          _commandHealthItems.Add(new CommandHealthItem
          {
            CommandId = descriptor.Id,
            Title = descriptor.Title,
            Shortcut = descriptor.KeyboardShortcut ?? "",
            Status = status,
            CanExecute = canExecute,
            SuccessCount = successCount,
            FailureCount = failureCount,
            AvgExecutionMs = avgMs.ToString("F1"),
            LastError = lastError,
            LastExecuted = lastExecuted
          });
        }

        // Update summary counts
        TotalCommandsCount.Text = totalCommands.ToString();
        WorkingCommandsCount.Text = workingCount.ToString();
        BrokenCommandsCount.Text = brokenCount.ToString();
        TotalExecutionsCount.Text = totalExecutions.ToString();

        CommandsListView.ItemsSource = _commandHealthItems;
      }
      catch (System.Exception ex)
      {
        _errorLoggingService?.LogError(ex, "LoadCommandHealth");
      }
    }

    #region Log Filtering

    private void LogLevelFilter_SelectionChanged(object? sender, global::Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
      ApplyLogFilters();
    }

    private void LogSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
      ApplyLogFilters();
    }

    private bool IsLogFilterActive()
    {
      var levelFilter = (LogLevelFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString();
      var searchText = LogSearchBox?.Text?.Trim();
      return (levelFilter != null && levelFilter != "All") || !string.IsNullOrEmpty(searchText);
    }

    private void ApplyLogFilters()
    {
      if (LogsListView == null || ViewModel?.Logs == null)
        return;

      var levelFilter = (LogLevelFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
      var searchText = LogSearchBox?.Text?.Trim() ?? "";

      if (levelFilter == "All" && string.IsNullOrEmpty(searchText))
      {
        LogsListView.ItemsSource = ViewModel.Logs;
        return;
      }

      IEnumerable<LogEntry> filtered = ViewModel.Logs;

      if (levelFilter != "All")
      {
        filtered = filtered.Where(l => string.Equals(l.Level, levelFilter, StringComparison.OrdinalIgnoreCase));
      }

      if (!string.IsNullOrEmpty(searchText))
      {
        filtered = filtered.Where(l =>
            l.Message?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true);
      }

      _filteredLogs.Clear();
      foreach (var log in filtered)
      {
        _filteredLogs.Add(log);
      }
      LogsListView.ItemsSource = _filteredLogs;
    }

    #endregion

    #region Data Loading

    private async Task LoadEnginesAsync()
    {
      try
      {
        var backendClient = ServiceProvider.GetBackendClient();
        if (backendClient == null) return;

        var engines = await backendClient.GetEnginesAsync();
        if (engines?.Count > 0)
        {
          EnginesListView.ItemsSource = engines;
        }
        else
        {
          EnginesListView.ItemsSource = new List<string> { "(No engines available)" };
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "LoadEnginesAsync");
        EnginesListView.ItemsSource = new List<string> { $"(Failed to load: {ex.Message})" };
      }
    }

    private void LoadEnvironmentVariables()
    {
      try
      {
        var envVars = System.Environment.GetEnvironmentVariables();
        var items = new List<EnvironmentInfoItem>();

        foreach (System.Collections.DictionaryEntry entry in envVars)
        {
          items.Add(new EnvironmentInfoItem
          {
            Key = entry.Key?.ToString() ?? "",
            Value = entry.Value?.ToString() ?? ""
          });
        }

        EnvVarsListView.ItemsSource = items.OrderBy(i => i.Key).ToList();
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "LoadEnvironmentVariables");
      }
    }

    #endregion

    #region Additional Button Handlers

    // Logs tab handlers
    private async void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var savePicker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
        
        savePicker.SuggestedFileName = $"voicestudio_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        savePicker.FileTypeChoices.Add("Text Files", new List<string> { ".txt" });
        
        var file = await savePicker.PickSaveFileAsync();
        if (file != null)
        {
          // GAP-CS-002: Export real logs from ViewModel
          var logContent = new System.Text.StringBuilder();
          logContent.AppendLine($"VoiceStudio Log Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
          logContent.AppendLine($"Total entries: {ViewModel.Logs.Count}");
          logContent.AppendLine(new string('-', 80));
          
          foreach (var log in ViewModel.Logs)
          {
            logContent.AppendLine(log.FormattedLine);
          }
          
          await Windows.Storage.FileIO.WriteTextAsync(file, logContent.ToString());
          _toastService?.ShowToast(ToastType.Success, "Export Complete", $"Exported {ViewModel.Logs.Count} log entries");
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "ExportLogs_Click");
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    // Traces tab handlers
    private async void RefreshTraces_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        await ViewModel.RefreshTelemetryAsync();
        _toastService?.ShowToast(ToastType.Success, "Refreshed", "Trace data refreshed");
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "RefreshTraces_Click");
      }
    }

    private async void ExportTraces_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var savePicker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
        
        savePicker.SuggestedFileName = $"voicestudio_traces_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        savePicker.FileTypeChoices.Add("JSON Files", new List<string> { ".json" });
        
        var file = await savePicker.PickSaveFileAsync();
        if (file != null)
        {
          // GAP-CS-002: Export real traces from ViewModel
          var traceData = new
          {
            exported_at = DateTime.UtcNow,
            total_traces = ViewModel.TotalTracesCount,
            success_rate = ViewModel.TraceSuccessRate,
            avg_duration = ViewModel.TraceAvgDuration,
            traces = ViewModel.Traces.Select(t => new
            {
              trace_id = t.TraceId,
              start_time = t.StartTime,
              duration_ms = t.DurationMs,
              status = t.Status,
              operation = t.OperationName,
              spans = t.Spans?.Select(s => new
              {
                span_id = s.SpanId,
                name = s.Name,
                duration_ms = s.DurationMs,
                status = s.Status
              })
            }).ToList()
          };
          
          var json = System.Text.Json.JsonSerializer.Serialize(
            traceData, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, json);
          _toastService?.ShowToast(ToastType.Success, "Export Complete", $"Exported {ViewModel.TotalTracesCount} traces");
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "ExportTraces_Click");
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    // Network tab handlers
    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var backendClient = ServiceProvider.GetBackendClient();
        var isConnected = await backendClient.CheckHealthAsync();
        
        if (isConnected)
          _toastService?.ShowToast(ToastType.Success, "Connection OK", "Backend connection successful");
        else
          _toastService?.ShowToast(ToastType.Warning, "Connection Failed", "Unable to reach backend");
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "TestConnection_Click");
        _toastService?.ShowToast(ToastType.Error, "Connection Error", ex.Message);
      }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var backendClient = ServiceProvider.GetBackendClient();
        // Attempt reconnection by checking health - this will establish connection if needed
        var isConnected = await backendClient.CheckHealthAsync();
        if (isConnected)
          _toastService?.ShowToast(ToastType.Success, "Reconnected", "Backend connection re-established");
        else
          _toastService?.ShowToast(ToastType.Warning, "Reconnect Failed", "Unable to reach backend");
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "Reconnect_Click");
        _toastService?.ShowToast(ToastType.Error, "Reconnect Failed", ex.Message);
      }
    }

    // Engines tab handlers
    private async void RefreshEngines_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        await ViewModel.RefreshTelemetryAsync();
        _toastService?.ShowToast(ToastType.Success, "Refreshed", "Engine data refreshed");
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "RefreshEngines_Click");
      }
    }

    private async void StopAllEngines_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var backendClient = ServiceProvider.GetBackendClient();
        await backendClient.PostAsync<object, object>("/api/engines/stop-all", new { });
        _toastService?.ShowToast(ToastType.Success, "Engines Stopped", "All engines have been stopped");
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "StopAllEngines_Click");
        _toastService?.ShowToast(ToastType.Error, "Stop Failed", ex.Message);
      }
    }

    // GAP-I24: Circuit Breaker handlers
    private void RefreshCircuitBreakers_Click(object sender, RoutedEventArgs e)
    {
      _ = LoadCircuitBreakersAsync();
    }

    private async void ResetCircuitBreaker_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is Button button && button.Tag is string engineId)
        {
          var backendClient = ServiceProvider.GetBackendClient();
          var response = await backendClient.PostAsync<object, CircuitBreakerResetResponse>(
            $"/api/health/circuit-breakers/{engineId}/reset", new { });
          
          if (response?.Success == true)
          {
            _toastService?.ShowToast(ToastType.Success, "Circuit Breaker Reset", $"'{engineId}' reset to CLOSED");
            await LoadCircuitBreakersAsync();
          }
          else
          {
            _toastService?.ShowToast(ToastType.Warning, "Reset Failed", response?.Message ?? "Unknown error");
          }
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "ResetCircuitBreaker_Click");
        _toastService?.ShowToast(ToastType.Error, "Reset Failed", ex.Message);
      }
    }

    private async Task LoadCircuitBreakersAsync()
    {
      try
      {
        var backendClient = ServiceProvider.GetBackendClient();
        if (backendClient == null) return;

        var response = await backendClient.GetAsync<CircuitBreakerHealthResponse>("/api/health/circuit-breakers");
        
        if (response != null)
        {
          // Update summary counts
          TotalCircuitBreakersCount.Text = response.Summary?.Total.ToString() ?? "0";
          ClosedCircuitBreakersCount.Text = response.Summary?.Closed.ToString() ?? "0";
          HalfOpenCircuitBreakersCount.Text = response.Summary?.HalfOpen.ToString() ?? "0";
          OpenCircuitBreakersCount.Text = response.Summary?.Open.ToString() ?? "0";

          // Populate list
          _circuitBreakerItems.Clear();
          foreach (var cb in response.CircuitBreakers ?? new List<CircuitBreakerInfo>())
          {
            _circuitBreakerItems.Add(new CircuitBreakerItem
            {
              Name = cb.Name ?? "Unknown",
              State = cb.State ?? "UNKNOWN",
              FailureCount = cb.FailureCount,
              SuccessCount = cb.SuccessCount,
              TotalCalls = cb.TotalCalls,
              BlockedRequests = cb.BlockedRequests,
              FailureRate = cb.FailureRate
            });
          }

          CircuitBreakersListView.ItemsSource = _circuitBreakerItems;
          NoCircuitBreakersText.Visibility = _circuitBreakerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "LoadCircuitBreakersAsync");
        NoCircuitBreakersText.Visibility = Visibility.Visible;
      }
    }

    #endregion
  }

  /// <summary>
  /// View model for command health display in diagnostics.
  /// </summary>
  public sealed class CommandHealthItem
  {
    public string CommandId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Shortcut { get; set; } = "";
    public CommandStatus Status { get; set; }
    public bool CanExecute { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public string AvgExecutionMs { get; set; } = "0.0";
    public string? LastError { get; set; }
    public DateTime? LastExecuted { get; set; }

    public string StatusGlyph => Status switch
    {
      CommandStatus.Working => "\uE73E",  // Checkmark
      CommandStatus.Broken => "\uE711",   // Error
      CommandStatus.Disabled => "\uE8D8", // Pause
      _ => "\uE9CE"                       // Question mark
    };

    public SolidColorBrush StatusColor => Status switch
    {
      CommandStatus.Working => new SolidColorBrush(UIColors.Green),
      CommandStatus.Broken => new SolidColorBrush(UIColors.Red),
      CommandStatus.Disabled => new SolidColorBrush(UIColors.Gray),
      _ => new SolidColorBrush(UIColors.Orange)
    };

    public string StatusText => Status switch
    {
      CommandStatus.Working => "Working",
      CommandStatus.Broken => "Broken",
      CommandStatus.Disabled => "Disabled",
      CommandStatus.Unknown => "Unknown",
      _ => "Unknown"
    };

    public string CanExecuteGlyph => CanExecute ? "\uE73E" : "\uE711";  // Check or X

    public SolidColorBrush CanExecuteColor => CanExecute 
      ? new SolidColorBrush(UIColors.Green) 
      : new SolidColorBrush(UIColors.Red);

    public string CanExecuteText => CanExecute ? "Ready" : "Blocked";

    public string LastExecutedText => LastExecuted.HasValue 
      ? LastExecuted.Value.ToLocalTime().ToString("HH:mm:ss") 
      : "Never";

    public SolidColorBrush FailureColor => FailureCount > 0 
      ? new SolidColorBrush(UIColors.Red) 
      : new SolidColorBrush(UIColors.Gray);
  }

  /// <summary>
  /// Response model for job queue API.
  /// </summary>
  public sealed class JobQueueResponse
  {
    public int Queued { get; set; }
    public int Running { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public List<JobInfo>? ActiveJobs { get; set; }
  }

  /// <summary>
  /// Individual job information.
  /// </summary>
  public sealed class JobInfo
  {
    public string JobId { get; set; } = "";
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public DateTime StartTime { get; set; }
  }

  /// <summary>
  /// Response model for GPU status API.
  /// </summary>
  public sealed class GPUStatusResponse
  {
    public List<GPUDeviceInfo>? Devices { get; set; }
    public int TotalDevices { get; set; }
    public int AvailableDevices { get; set; }
    public string? PrimaryDevice { get; set; }
  }

  /// <summary>
  /// Individual GPU device information.
  /// </summary>
  public sealed class GPUDeviceInfo
  {
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public int MemoryTotalMb { get; set; }
    public int MemoryUsedMb { get; set; }
    public int MemoryFreeMb { get; set; }
    public double UtilizationPercent { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? PowerUsageWatts { get; set; }
    public string? DriverVersion { get; set; }
    public string? ComputeCapability { get; set; }
    public bool IsAvailable { get; set; } = true;
  }

  /// <summary>
  /// View model for job queue display in diagnostics.
  /// </summary>
  public sealed class JobQueueItem
  {
    public string JobId { get; set; } = "";
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public DateTime StartTime { get; set; }

    public string StatusIcon => Status switch
    {
      "queued" => "\uE768",      // Clock
      "running" => "\uE916",     // Sync
      "completed" => "\uE73E",   // Check
      "failed" => "\uE711",      // Error
      _ => "\uE9CE"              // Question
    };

    public SolidColorBrush StatusColor => Status switch
    {
      "queued" => new SolidColorBrush(UIColors.Gray),
      "running" => new SolidColorBrush(UIColors.Orange),
      "completed" => new SolidColorBrush(UIColors.Green),
      "failed" => new SolidColorBrush(UIColors.Red),
      _ => new SolidColorBrush(UIColors.Gray)
    };

    public string ProgressText => $"{Progress:F0}%";

    public string Duration
    {
      get
      {
        var elapsed = DateTime.UtcNow - StartTime;
        if (elapsed.TotalHours >= 1)
          return $"{elapsed.TotalHours:F1}h";
        if (elapsed.TotalMinutes >= 1)
          return $"{elapsed.TotalMinutes:F0}m";
        return $"{elapsed.TotalSeconds:F0}s";
      }
    }
  }

  #region GAP-I24: Circuit Breaker Models

  /// <summary>
  /// Response model for circuit breaker health API (GAP-I24).
  /// </summary>
  public sealed class CircuitBreakerHealthResponse
  {
    public string? Timestamp { get; set; }
    public List<CircuitBreakerInfo>? CircuitBreakers { get; set; }
    public CircuitBreakerSummary? Summary { get; set; }
    public string? Error { get; set; }
  }

  /// <summary>
  /// Summary of circuit breaker states (GAP-I24).
  /// </summary>
  public sealed class CircuitBreakerSummary
  {
    public int Total { get; set; }
    public int Open { get; set; }
    public int HalfOpen { get; set; }
    public int Closed { get; set; }
  }

  /// <summary>
  /// Individual circuit breaker information (GAP-I24).
  /// </summary>
  public sealed class CircuitBreakerInfo
  {
    public string? Name { get; set; }
    public string? State { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public int TotalCalls { get; set; }
    public int BlockedRequests { get; set; }
    public double FailureRate { get; set; }
    public double? LastFailureTime { get; set; }
    public double? LastStateChange { get; set; }
  }

  /// <summary>
  /// Response model for circuit breaker reset API (GAP-I24).
  /// </summary>
  public sealed class CircuitBreakerResetResponse
  {
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
  }

  /// <summary>
  /// View model for circuit breaker display in diagnostics (GAP-I24).
  /// </summary>
  public sealed class CircuitBreakerItem
  {
    public string Name { get; set; } = "";
    public string State { get; set; } = "UNKNOWN";
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public int TotalCalls { get; set; }
    public int BlockedRequests { get; set; }
    public double FailureRate { get; set; }

    public SolidColorBrush StateColor => State switch
    {
      "CLOSED" => new SolidColorBrush(UIColors.Green),
      "HALF_OPEN" => new SolidColorBrush(UIColors.Orange),
      "OPEN" => new SolidColorBrush(UIColors.Red),
      _ => new SolidColorBrush(UIColors.Gray)
    };

    public string FailureRateFormatted => $"{FailureRate * 100:F1}%";

    public SolidColorBrush FailureRateColor
    {
      get
      {
        if (FailureRate >= 0.5) return new SolidColorBrush(UIColors.Red);
        if (FailureRate >= 0.2) return new SolidColorBrush(UIColors.Orange);
        return new SolidColorBrush(UIColors.Green);
      }
    }

    public Visibility ShowResetButton => State != "CLOSED" ? Visibility.Visible : Visibility.Collapsed;
  }

  #endregion
}