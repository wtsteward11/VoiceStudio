using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using Microsoft.UI.Dispatching;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Views.Panels
{
  public partial class DiagnosticsViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IErrorLoggingService? _errorLoggingService;
    private readonly IAnalyticsService? _analyticsService;
    private readonly IFeatureFlagsService? _featureFlagsService;
    private readonly MultiSelectService _multiSelectService;
    private readonly ToastNotificationService? _toastNotificationService;
    private MultiSelectState? _logsMultiSelectState;
    private MultiSelectState? _errorLogsMultiSelectState;
    private CancellationTokenSource? _telemetryCancellationTokenSource;
    private long _peakMemoryBytes;
    private readonly List<BudgetViolationEventArgs> _budgetViolations = new();

    public string PanelId => "diagnostics";
    public string DisplayName => ResourceHelper.GetString("Panel.Diagnostics.DisplayName", "Diagnostics");
    public PanelRegion Region => PanelRegion.Bottom;

    [ObservableProperty]
    private bool isHealthy;

    [ObservableProperty]
    private string? healthStatus;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string connectionStatus = ResourceHelper.GetString("Diagnostics.ConnectionStatusUnknown", "Unknown");

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private Telemetry? telemetry;

    [ObservableProperty]
    private double cpuUsage;

    [ObservableProperty]
    private double gpuUsage;

    [ObservableProperty]
    private double ramUsage;

    [ObservableProperty]
    private long currentMemoryBytes;

    [ObservableProperty]
    private long peakMemoryBytes;

    [ObservableProperty]
    private long memoryByUI;

    [ObservableProperty]
    private long memoryByAudio;

    [ObservableProperty]
    private long memoryByEngines;

    [ObservableProperty]
    private string memoryFormatted = string.Empty;

    // System information properties
    // Computed properties for system information
    public string OsArchitecture => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
    public string ProcessArchitecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
    public string MachineName => System.Environment.MachineName;
    public string UserName => System.Environment.UserName;
    public double WorkingSetMB => System.Environment.WorkingSet / (1024.0 * 1024.0);
    public int ProcessorCount => System.Environment.ProcessorCount;

    [ObservableProperty]
    private string peakMemoryFormatted = string.Empty;

    [ObservableProperty]
    private string vramWarning = string.Empty;

    [ObservableProperty]
    private bool showVramWarning;

    [ObservableProperty]
    private ObservableCollection<LogEntry> logs = new();

    [ObservableProperty]
    private ObservableCollection<ErrorLogEntryViewModel> errorLogs = new();

    /// <summary>
    /// Filtered error logs based on correlation ID, level, and text filters.
    /// Phase 5.3.1: Correlation Filtering
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ErrorLogEntryViewModel> filteredErrorLogs = new();

    /// <summary>
    /// Number of logs matching the current correlation ID filter.
    /// </summary>
    [ObservableProperty]
    private int correlationIdMatchCount;

    /// <summary>
    /// Whether correlation ID filter is active.
    /// </summary>
    public bool HasCorrelationIdFilter =>
        !string.IsNullOrEmpty(CorrelationIdFilter);

    [ObservableProperty]
    private bool autoRefreshTelemetry;

    [ObservableProperty]
    private string errorLogFilter = string.Empty;

    [ObservableProperty]
    private string selectedErrorLevel = "All"; // All, Error, Warning, Info

    // Correlation ID tracking
    [ObservableProperty]
    private string? currentCorrelationId;

    [ObservableProperty]
    private string correlationIdFilter = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CorrelationIdEntry> recentCorrelationIds = new();

    // Trace visualization (Phase 5.1.3)
    [ObservableProperty]
    private ObservableCollection<TraceEntry> traces = new();

    [ObservableProperty]
    private int totalTracesCount;

    [ObservableProperty]
    private string traceSuccessRate = "100%";

    [ObservableProperty]
    private string traceAvgDuration = "--";

    [ObservableProperty]
    private int traceErrorCount;

    [ObservableProperty]
    private string traceIdFilter = string.Empty;

    [ObservableProperty]
    private string traceStatusFilter = "All";

    [ObservableProperty]
    private string traceTimeRangeFilter = "Last 15 min";

    [ObservableProperty]
    private bool isTracesLoading;

    [ObservableProperty]
    private int errorLogCount;

    [ObservableProperty]
    private int errorCount;

    [ObservableProperty]
    private int warningCount;

    [ObservableProperty]
    private int infoCount;

    [ObservableProperty]
    private bool isDegradedMode;

    [ObservableProperty]
    private string? degradationReason;

    [ObservableProperty]
    private int queuedOperationsCount;

    // Multi-select support for logs
    [ObservableProperty]
    private int selectedLogCount;

    [ObservableProperty]
    private bool hasMultipleLogSelection;

    // Multi-select support for error logs
    [ObservableProperty]
    private int selectedErrorLogCount;

    [ObservableProperty]
    private bool hasMultipleErrorLogSelection;

    // Analytics tab
    [ObservableProperty]
    private ObservableCollection<AnalyticsEvent> recentAnalyticsEvents = new();

    // Audit Log tab
    [ObservableProperty]
    private ObservableCollection<AuditEntryViewModel> auditEntries = new();

    [ObservableProperty]
    private int auditEntryCount;

    [ObservableProperty]
    private string auditFilter = string.Empty;

    [ObservableProperty]
    private bool isAuditLoading;

    private IAuditLoggingService? _auditLoggingService;

    // Commands

    // Performance tab
    [ObservableProperty]
    private ObservableCollection<BudgetViolationViewModel> budgetViolations = new();

    // Feature flags tab
    [ObservableProperty]
    private ObservableCollection<FeatureFlagViewModel> featureFlags = new();

    // Environment tab
    private string appVersion = string.Empty;
    public string AppVersion
    {
      get => appVersion;
      private set => SetProperty(ref appVersion, value);
    }

    private string dotNetVersion = string.Empty;
    public string DotNetVersion
    {
      get => dotNetVersion;
      private set => SetProperty(ref dotNetVersion, value);
    }

    private string osVersion = string.Empty;
    public string OsVersion
    {
      get => osVersion;
      private set => SetProperty(ref osVersion, value);
    }

    [ObservableProperty]
    private ObservableCollection<EnvironmentInfoItem> environmentInfo = new();

    public bool IsLogSelected(string logId) => _logsMultiSelectState?.SelectedIds.Contains(logId) ?? false;
    public bool IsErrorLogSelected(string errorLogId) => _errorLogsMultiSelectState?.SelectedIds.Contains(errorLogId) ?? false;

    public DiagnosticsViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get error logging service using helper (reduces code duplication)
      _errorLoggingService = ServiceInitializationHelper.TryGetService(() => ServiceProvider.GetErrorLoggingService());
      if (_errorLoggingService != null)
      {
        _errorLoggingService.ErrorLogged += OnErrorLogged;
        LoadErrorLogs();
      }

      // Get analytics service using helper
      _analyticsService = ServiceInitializationHelper.TryGetService(() => ServiceProvider.TryGetAnalyticsService());
      if (_analyticsService != null)
      {
        _analyticsService.EventTracked += OnAnalyticsEventTracked;
        LoadAnalyticsEvents();
      }

      // Get feature flags service using helper (reduces code duplication)
      _featureFlagsService = ServiceInitializationHelper.TryGetService(() => ServiceProvider.GetFeatureFlagsService());

      // Get audit logging service
      _auditLoggingService = ServiceInitializationHelper.TryGetService(() => ServiceProvider.GetAuditLoggingService());
      if (_auditLoggingService != null)
      {
        _auditLoggingService.AuditEntryLogged += OnAuditEntryLogged;
      }
      if (_featureFlagsService != null)
      {
        LoadFeatureFlags();
      }

      // Subscribe to performance profiler budget violations
      PerformanceProfiler.BudgetViolated += OnBudgetViolated;

      // Load environment info
      LoadEnvironmentInfo();

      // Show Errors tab by default
      // (Tab visibility is handled in XAML code-behind)

      CheckHealthCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CheckHealth");
        await CheckHealthAsync(ct);
      });
      LoadTelemetryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTelemetry");
        await LoadTelemetryAsync(ct);
      });
      ClearLogsCommand = new RelayCommand(ClearLogs);
      LoadErrorLogsCommand = new RelayCommand(LoadErrorLogs);
      ClearErrorLogsCommand = new RelayCommand(ClearErrorLogs);
      ExportErrorLogsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportErrorLogs");
        await ExportErrorLogsAsync(ct);
      });
      RefreshAnalyticsCommand = new RelayCommand(LoadAnalyticsEvents);
      RefreshPerformanceCommand = new RelayCommand(LoadPerformanceData);
      RefreshFeatureFlagsCommand = new RelayCommand(LoadFeatureFlags);
      RefreshEnvironmentCommand = new RelayCommand(LoadEnvironmentInfo);
      ToggleFeatureFlagCommand = new RelayCommand<string>(ToggleFeatureFlag);
      
      // Correlation ID commands
      FilterByCorrelationIdCommand = new RelayCommand<string>(FilterByCorrelationId);
      ClearCorrelationIdFilterCommand = new RelayCommand(ClearCorrelationIdFilter);
      CopyCorrelationIdCommand = new RelayCommand<string>(CopyCorrelationIdToClipboard);

      // Trace commands (Phase 5.1.3)
      RefreshTracesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RefreshTraces");
        await LoadTracesAsync();
      });
      ExportTracesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportTraces");
        await ExportTracesAsync(ct);
      });

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _logsMultiSelectState = _multiSelectService.GetState($"{PanelId}_logs");
      _errorLogsMultiSelectState = _multiSelectService.GetState($"{PanelId}_errorlogs");

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

      // Multi-select commands for logs
      SelectAllLogsCommand = new RelayCommand(SelectAllLogs, () => Logs?.Count > 0);
      ClearLogSelectionCommand = new RelayCommand(ClearLogSelection);
      DeleteSelectedLogsCommand = new RelayCommand(DeleteSelectedLogs, () => SelectedLogCount > 0);

      // Multi-select commands for error logs
      SelectAllErrorLogsCommand = new RelayCommand(SelectAllErrorLogs, () => ErrorLogs?.Count > 0);
      ClearErrorLogSelectionCommand = new RelayCommand(ClearErrorLogSelection);
      DeleteSelectedErrorLogsCommand = new RelayCommand(DeleteSelectedErrorLogs, () => SelectedErrorLogCount > 0);
      ExportSelectedErrorLogsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportSelectedErrorLogs");
        await ExportSelectedErrorLogsAsync(ct);
      }, () => SelectedErrorLogCount > 0);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == $"{PanelId}_logs")
        {
          UpdateLogSelectionProperties();
          OnPropertyChanged(nameof(SelectedLogCount));
          OnPropertyChanged(nameof(HasMultipleLogSelection));
        }
        else if (e.PanelId == $"{PanelId}_errorlogs")
        {
          UpdateErrorLogSelectionProperties();
          OnPropertyChanged(nameof(SelectedErrorLogCount));
          OnPropertyChanged(nameof(HasMultipleErrorLogSelection));
        }
      };

      // Initialize memory monitoring
      UpdateMemoryMetrics();

      // Subscribe to graceful degradation service
      try
      {
        var degradationService = ServiceProvider.GetGracefulDegradationService();
        if (degradationService != null)
        {
          degradationService.DegradedModeChanged += (_, isDegraded) =>
          {
            IsDegradedMode = isDegraded;
            DegradationReason = degradationService.DegradationReason;
          };
          IsDegradedMode = degradationService.IsDegradedMode;
          DegradationReason = degradationService.DegradationReason;
        }

        var queueService = ServiceProvider.GetOperationQueueService();
        if (queueService != null)
        {
          queueService.QueueCountChanged += (_, count) => QueuedOperationsCount = count;
          QueuedOperationsCount = queueService.QueueCount;
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "DiagnosticsViewModel.Unknown");
      }
    }

    public IAsyncRelayCommand CheckHealthCommand { get; }
    public IAsyncRelayCommand LoadTelemetryCommand { get; }
    public IRelayCommand ClearLogsCommand { get; }
    public IRelayCommand LoadErrorLogsCommand { get; }
    public IRelayCommand ClearErrorLogsCommand { get; }
    public IAsyncRelayCommand ExportErrorLogsCommand { get; }
    public IRelayCommand RefreshAnalyticsCommand { get; }
    public IRelayCommand RefreshPerformanceCommand { get; }
    public IRelayCommand RefreshFeatureFlagsCommand { get; }
    public IRelayCommand RefreshEnvironmentCommand { get; }
    public IRelayCommand<string> ToggleFeatureFlagCommand { get; }

    // Multi-select commands for logs
    public IRelayCommand SelectAllLogsCommand { get; }
    public IRelayCommand ClearLogSelectionCommand { get; }
    public IRelayCommand DeleteSelectedLogsCommand { get; }

    // Multi-select commands for error logs
    public IRelayCommand SelectAllErrorLogsCommand { get; }
    public IRelayCommand ClearErrorLogSelectionCommand { get; }
    public IRelayCommand DeleteSelectedErrorLogsCommand { get; }
    public IAsyncRelayCommand ExportSelectedErrorLogsCommand { get; }
    
    // Correlation ID commands
    public IRelayCommand<string> FilterByCorrelationIdCommand { get; }
    public IRelayCommand ClearCorrelationIdFilterCommand { get; }
    public IRelayCommand<string> CopyCorrelationIdCommand { get; }

    // Trace commands (Phase 5.1.3)
    public IAsyncRelayCommand RefreshTracesCommand { get; }
    public IAsyncRelayCommand ExportTracesCommand { get; }

    partial void OnAutoRefreshTelemetryChanged(bool value)
    {
      if (value)
      {
        StartTelemetryRefresh();
      }
      else
      {
        StopTelemetryRefresh();
      }
    }

    private void StartTelemetryRefresh()
    {
      StopTelemetryRefresh();
      _telemetryCancellationTokenSource = new CancellationTokenSource();
      _ = RefreshTelemetryLoop(_telemetryCancellationTokenSource.Token);
    }

    private void StopTelemetryRefresh()
    {
      _telemetryCancellationTokenSource?.Cancel();
      _telemetryCancellationTokenSource?.Dispose();
      _telemetryCancellationTokenSource = null;
    }

    private async Task RefreshTelemetryLoop(CancellationToken cancellationToken)
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        try
        {
          await LoadTelemetryAsync(cancellationToken);
          // Also update memory metrics (doesn't require backend call)
          UpdateMemoryMetrics();
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "DiagnosticsViewModel.RefreshTelemetryLoop");
      }
        await Task.Delay(2000, cancellationToken); // Refresh every 2 seconds
      }
    }

    private async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var healthy = await _backendClient.CheckHealthAsync(cancellationToken);
        IsHealthy = healthy;

        // Update connection status
        IsConnected = _backendClient.IsConnected;
        UpdateConnectionStatus();

        HealthStatus = healthy ? "Backend API is healthy" : "Backend API is not responding";

        AddLog("INFO", HealthStatus ?? "Health check completed");

        // Show toast notification
        if (healthy)
        {
          _toastNotificationService?.ShowSuccess(
              "Health Check",
              "Backend API is healthy and responding");
          // Auto-load telemetry after health check
          _ = LoadTelemetryAsync(CancellationToken.None);
        }
        else
        {
          _toastNotificationService?.ShowWarning(
              "Health Check",
              "Backend API is not responding. Check connection and try again.");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        IsHealthy = false;
        IsConnected = false;
        UpdateConnectionStatus();
        HealthStatus = ResourceHelper.GetString("Diagnostics.ErrorCheckingHealth", "Error checking health");
        ErrorMessage = ResourceHelper.FormatString("Diagnostics.CheckHealthFailed", ex.Message);
        AddLog("ERROR", ResourceHelper.FormatString("Diagnostics.HealthCheckFailed", ex.Message));
        await HandleErrorAsync(ex, "CheckHealth");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateConnectionStatus()
    {
      if (!IsConnected)
      {
        ConnectionStatus = "Offline";
      }
      else
      {
        // Try to get circuit breaker state if available
        try
        {
          if (_backendClient is Services.BackendClient client)
          {
            var circuitState = client.CircuitState;
            ConnectionStatus = circuitState switch
            {
              Utilities.CircuitState.Open => "Circuit Open (Temporarily Unavailable)",
              Utilities.CircuitState.HalfOpen => "Testing Connection...",
              Utilities.CircuitState.Closed => "Connected",
              _ => "Connected"
            };
          }
          else
          {
            ConnectionStatus = IsHealthy ? "Connected" : "Disconnected";
          }
        }
        catch
        {
          ConnectionStatus = IsHealthy ? "Connected" : "Disconnected";
        }
      }
    }

    private async Task LoadTelemetryAsync(CancellationToken cancellationToken)
    {
      try
      {
        Telemetry = await _backendClient.GetTelemetryAsync(cancellationToken);

        // Update individual properties for UI binding
        CpuUsage = Telemetry.CpuPct ?? 0.0;
        GpuUsage = Telemetry.VramPct;
        RamUsage = Telemetry.RamPct ?? 0.0;

        // Update VRAM warning
        UpdateVramWarning();

        // Update connection status
        IsConnected = _backendClient.IsConnected;
        UpdateConnectionStatus();

        // Update memory metrics
        UpdateMemoryMetrics();

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.TelemetryUpdated", "Telemetry Updated"),
            ResourceHelper.GetString("Diagnostics.TelemetryRefreshed", "System telemetry data has been refreshed"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        // Log error but don't break the UI
        AddLog("WARNING", $"Failed to load telemetry: {ex.Message}");
        await HandleErrorAsync(ex, "LoadTelemetry");
      }
    }

    private void UpdateMemoryMetrics()
    {
      try
      {
        // Get current process memory usage
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var workingSet = process.WorkingSet64;
        var privateMemory = process.PrivateMemorySize64;

        CurrentMemoryBytes = workingSet;

        // Update peak memory
        if (workingSet > _peakMemoryBytes)
        {
          _peakMemoryBytes = workingSet;
        }
        PeakMemoryBytes = _peakMemoryBytes;

        // Format memory for display
        MemoryFormatted = FormatBytes(workingSet);
        PeakMemoryFormatted = FormatBytes(_peakMemoryBytes);

        // Estimate memory by category (simplified - would need more detailed tracking)
        // For now, use heuristics based on process memory
        var totalMemory = GC.GetTotalMemory(false);
        MemoryByUI = (long)(totalMemory * 0.3); // Estimate 30% for UI
        MemoryByAudio = (long)(totalMemory * 0.2); // Estimate 20% for audio
        MemoryByEngines = (long)(totalMemory * 0.5); // Estimate 50% for engines
      }
      catch (Exception ex)
      {
        // Silently fail - memory monitoring should not break the UI
        System.Diagnostics.Debug.WriteLine($"Failed to update memory metrics: {ex.Message}");
      }
    }

    private static string FormatBytes(long bytes)
    {
      string[] sizes = { "B", "KB", "MB", "GB", "TB" };
      double len = bytes;
      int order = 0;
      while (len >= 1024 && order < sizes.Length - 1)
      {
        order++;
        len /= 1024;
      }
      return $"{len:0.##} {sizes[order]}";
    }

    private void UpdateVramWarning()
    {
      if (Telemetry == null)
      {
        ShowVramWarning = false;
        VramWarning = string.Empty;
        return;
      }

      var vramPct = Telemetry.VramPct;

      if (vramPct >= 95)
      {
        ShowVramWarning = true;
        VramWarning = ResourceHelper.GetString("Diagnostics.VramCritical", "⚠️ CRITICAL: VRAM usage is very high. Close other applications or reduce audio processing load.");
      }
      else if (vramPct >= 85)
      {
        ShowVramWarning = true;
        VramWarning = ResourceHelper.GetString("Diagnostics.VramWarning", "⚠️ WARNING: VRAM usage is high. Consider closing other GPU applications.");
      }
      else if (vramPct >= 75)
      {
        ShowVramWarning = true;
        VramWarning = ResourceHelper.GetString("Diagnostics.VramElevated", "ℹ️ VRAM usage is elevated. Monitor usage if you experience performance issues.");
      }
      else
      {
        ShowVramWarning = false;
        VramWarning = string.Empty;
      }
    }

    private void AddLog(string level, string message)
    {
      var logEntry = new LogEntry
      {
        Timestamp = DateTime.Now,
        Level = level,
        Message = message
      };

      // Add to beginning of list (newest first)
      Logs.Insert(0, logEntry);

      // Keep only last 100 entries
      if (Logs.Count > 100)
      {
        Logs.RemoveAt(Logs.Count - 1);
      }
    }

    private void ClearLogs()
    {
      var count = Logs.Count;
      Logs.Clear();

      if (count > 0)
      {
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.LogsCleared", "Logs Cleared"),
            ResourceHelper.FormatString("Diagnostics.LogsCleared", count, count == 1 ? "y" : "ies"));
      }
    }

    private void OnErrorLogged(object? sender, ErrorLogEntry entry)
    {
      // Add to error logs collection on UI thread
      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
      {
        var viewModel = new ErrorLogEntryViewModel(entry);
        ErrorLogs.Insert(0, viewModel);
        UpdateErrorCounts();
        ApplyFilters();
      });
    }

    private void OnAnalyticsEventTracked(object? sender, AnalyticsEvent evt)
    {
      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
      {
        RecentAnalyticsEvents.Insert(0, evt);
        // Keep only last 100 events
        while (RecentAnalyticsEvents.Count > 100)
        {
          RecentAnalyticsEvents.RemoveAt(RecentAnalyticsEvents.Count - 1);
        }
      });
    }

    private void OnBudgetViolated(object? sender, BudgetViolationEventArgs args)
    {
      lock (_budgetViolations)
      {
        _budgetViolations.Add(args);
        // Keep only last 50 violations
        if (_budgetViolations.Count > 50)
        {
          _budgetViolations.RemoveAt(0);
        }
      }
      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => LoadPerformanceData());
    }

    private void LoadAnalyticsEvents()
    {
      if (_analyticsService == null)
        return;

      var events = _analyticsService.GetRecentEvents(100);
      RecentAnalyticsEvents.Clear();
      foreach (var evt in events.OrderByDescending(e => e.Timestamp))
      {
        RecentAnalyticsEvents.Add(evt);
      }
    }

    private void LoadPerformanceData()
    {
      lock (_budgetViolations)
      {
        BudgetViolations.Clear();
        foreach (var violation in _budgetViolations.OrderByDescending(v => v.Timestamp))
        {
          BudgetViolations.Add(new BudgetViolationViewModel(violation));
        }
      }
    }

    private void LoadFeatureFlags()
    {
      if (_featureFlagsService == null)
        return;

      var flags = _featureFlagsService.GetAllFlags();
      FeatureFlags.Clear();

      // Get descriptions if available
      foreach (var flag in flags)
      {
        var description = _featureFlagsService.GetDescription(flag.Key) ?? "No description available";
        FeatureFlags.Add(new FeatureFlagViewModel(flag.Key, flag.Value, description, _featureFlagsService));
      }
    }

    private void ToggleFeatureFlag(string? flagName)
    {
      if (string.IsNullOrWhiteSpace(flagName) || _featureFlagsService == null)
        return;

      var flag = FeatureFlags.FirstOrDefault(f => f.FlagName == flagName);
      if (flag != null)
      {
        flag.IsEnabled = !flag.IsEnabled;
      }
    }

    /// <summary>
    /// Refreshes telemetry data from the backend.
    /// </summary>
    public async Task RefreshTelemetryAsync()
    {
      await LoadTelemetryAsync(CancellationToken.None);
    }

    /// <summary>
    /// Load audit log entries from the audit service.
    /// </summary>
    public async Task LoadAuditEntriesAsync()
    {
      if (_auditLoggingService == null)
        return;

      IsAuditLoading = true;
      try
      {
        await Task.Run(() =>
        {
          var entries = _auditLoggingService.GetRecentEntries(100);
          Dispatcher?.TryEnqueue(() =>
          {
            AuditEntries.Clear();
            foreach (var entry in entries)
            {
              AuditEntries.Add(new AuditEntryViewModel(entry));
            }
            AuditEntryCount = AuditEntries.Count;
          });
        });
      }
      finally
      {
        IsAuditLoading = false;
      }
    }

    private void OnAuditEntryLogged(object? sender, AuditEntry entry)
    {
      // Add new entry to the top of the list on the UI thread
      Dispatcher?.TryEnqueue(() =>
      {
        AuditEntries.Insert(0, new AuditEntryViewModel(entry));

        // Keep only the most recent 500 entries in the UI
        while (AuditEntries.Count > 500)
        {
          AuditEntries.RemoveAt(AuditEntries.Count - 1);
        }

        AuditEntryCount = AuditEntries.Count;
      });
    }

    private void LoadEnvironmentInfo()
    {
      // Initialize system information properties
      var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? ResourceHelper.GetString("Diagnostics.Unknown", "Unknown");
      var dotNetVersion = System.Environment.Version.ToString();
      var osVersion = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

      // Set properties (these are now auto-properties)
      AppVersion = appVersion;
      DotNetVersion = dotNetVersion;
      OsVersion = osVersion;

      EnvironmentInfo.Clear();
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvAppVersion", "App Version"), Value = AppVersion });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvDotNetVersion", ".NET Version"), Value = DotNetVersion });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvOSVersion", "OS Version"), Value = OsVersion });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvOSArchitecture", "OS Architecture"), Value = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString() });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvProcessArchitecture", "Process Architecture"), Value = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvMachineName", "Machine Name"), Value = System.Environment.MachineName });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvUserName", "User Name"), Value = System.Environment.UserName });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvWorkingSet", "Working Set"), Value = $"{System.Environment.WorkingSet / (1024 * 1024)} MB" });
      EnvironmentInfo.Add(new EnvironmentInfoItem { Key = ResourceHelper.GetString("Diagnostics.EnvProcessorCount", "Processor Count"), Value = System.Environment.ProcessorCount.ToString() });
    }

    private void LoadErrorLogs()
    {
      if (_errorLoggingService == null)
        return;

      var entries = _errorLoggingService.GetRecentErrors(1000);
      ErrorLogs.Clear();

      foreach (var entry in entries.OrderByDescending(e => e.Timestamp))
      {
        ErrorLogs.Add(new ErrorLogEntryViewModel(entry));
      }

      UpdateErrorCounts();
      ApplyFilters();
    }

    private void ClearErrorLogs()
    {
      var count = ErrorLogs.Count;
      _errorLoggingService?.ClearLogs();
      ErrorLogs.Clear();
      UpdateErrorCounts();

      if (count > 0)
      {
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.ErrorLogsCleared", "Error Logs Cleared"),
            ResourceHelper.FormatString("Diagnostics.ErrorLogsCleared", count, count == 1 ? "y" : "ies"));
      }
    }

    private async Task ExportErrorLogsAsync(CancellationToken cancellationToken)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedFileName = $"voicestudio_errors_{DateTime.Now:yyyyMMdd_HHmmss}";
        picker.FileTypeChoices.Add(ResourceHelper.GetString("Diagnostics.FileTypeJSON", "JSON File"), new[] { ".json" });
        picker.FileTypeChoices.Add(ResourceHelper.GetString("Diagnostics.FileTypeKeyValue", "Key-Value File"), new[] { ".txt" });
        picker.FileTypeChoices.Add(ResourceHelper.GetString("Diagnostics.FileTypeText", "Text File"), new[] { ".txt" });

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (file == null)
          return;

        if (_errorLoggingService == null)
        {
          AddLog("ERROR", "Error logging service not available");
          return;
        }

        // Export based on file extension
        if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
          // Export as structured JSON
          var json = _errorLoggingService.ExportLogsAsJson();
          await Windows.Storage.FileIO.WriteTextAsync(file, json);
        }
        else
        {
          // Export as key-value or text format
          var content = _errorLoggingService.ExportLogsAsKeyValue();
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
        }

        AddLog("INFO", ResourceHelper.FormatString("Diagnostics.ErrorLogsExported", file.Name));

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"),
            ResourceHelper.FormatString("Diagnostics.ErrorLogsExported", file.Name));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        AddLog("ERROR", $"Failed to export error logs: {ex.Message}");
        await HandleErrorAsync(ex, "ExportErrorLogs");
      }
    }

    partial void OnErrorLogFilterChanged(string value)
    {
      ApplyFilters();
    }

    partial void OnSelectedErrorLevelChanged(string value)
    {
      ApplyFilters();
    }

    private void ApplyFilters()
    {
      // Phase 5.3.1: Apply all active filters to error logs
      IEnumerable<ErrorLogEntryViewModel> filtered = ErrorLogs;

      // Apply correlation ID filter
      if (!string.IsNullOrEmpty(CorrelationIdFilter))
      {
        filtered = filtered.Where(e =>
            e.Metadata?.ContainsKey("correlation_id") == true &&
            e.Metadata["correlation_id"]?.ToString()?.Contains(
                CorrelationIdFilter, StringComparison.OrdinalIgnoreCase) == true);
      }

      // Apply text filter
      if (!string.IsNullOrEmpty(ErrorLogFilter))
      {
        var filter = ErrorLogFilter;
        filtered = filtered.Where(e =>
            e.Message?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true ||
            e.Context?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true ||
            e.ExceptionType?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
      }

      // Apply level filter
      if (!string.Equals(SelectedErrorLevel, "All", StringComparison.OrdinalIgnoreCase))
      {
        filtered = filtered.Where(e =>
            string.Equals(e.Level, SelectedErrorLevel, StringComparison.OrdinalIgnoreCase));
      }

      // Update filtered collection
      var filteredList = filtered.ToList();
      FilteredErrorLogs.Clear();
      foreach (var entry in filteredList)
      {
        FilteredErrorLogs.Add(entry);
      }

      // Update correlation ID match count
      if (!string.IsNullOrEmpty(CorrelationIdFilter))
      {
        CorrelationIdMatchCount = ErrorLogs.Count(e =>
            e.Metadata?.ContainsKey("correlation_id") == true &&
            e.Metadata["correlation_id"]?.ToString()?.Contains(
                CorrelationIdFilter, StringComparison.OrdinalIgnoreCase) == true);
      }
      else
      {
        CorrelationIdMatchCount = 0;
      }

      OnPropertyChanged(nameof(HasCorrelationIdFilter));
      UpdateErrorCounts();
    }

    private void FilterByCorrelationId(string? correlationId)
    {
      if (string.IsNullOrWhiteSpace(correlationId))
        return;
      
      CorrelationIdFilter = correlationId;
      CurrentCorrelationId = correlationId;
      
      // Add to recent correlation IDs
      var existing = RecentCorrelationIds.FirstOrDefault(c => c.CorrelationId == correlationId);
      if (existing != null)
      {
        RecentCorrelationIds.Remove(existing);
      }
      RecentCorrelationIds.Insert(0, new CorrelationIdEntry 
      { 
        CorrelationId = correlationId, 
        Timestamp = DateTime.Now,
        Source = "Manual Filter"
      });
      
      // Keep only last 20 correlation IDs
      while (RecentCorrelationIds.Count > 20)
      {
        RecentCorrelationIds.RemoveAt(RecentCorrelationIds.Count - 1);
      }
      
      // Apply filter to logs
      ApplyCorrelationIdFilter();
      
      _toastNotificationService?.ShowSuccess(
          "Correlation ID Filter",
          $"Filtering logs by: {correlationId[..Math.Min(8, correlationId.Length)]}...");
    }
    
    private void ClearCorrelationIdFilter()
    {
      CorrelationIdFilter = string.Empty;
      CurrentCorrelationId = null;
      ApplyFilters();
    }
    
    private void ApplyCorrelationIdFilter()
    {
      // Phase 5.3.1: Apply correlation ID filter using the unified filtering
      ApplyFilters();

      // Log filter result if correlation ID is specified
      if (!string.IsNullOrEmpty(CorrelationIdFilter))
      {
        AddLog(
            "INFO",
            $"Filtered to {CorrelationIdMatchCount} entries matching " +
            $"correlation ID: {CorrelationIdFilter}");
      }
    }
    
    private void CopyCorrelationIdToClipboard(string? correlationId)
    {
      if (string.IsNullOrWhiteSpace(correlationId))
        return;
      
      try
      {
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(correlationId);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        
        _toastNotificationService?.ShowSuccess(
            "Copied",
            "Correlation ID copied to clipboard");
      }
      catch (Exception ex)
      {
        AddLog("WARNING", $"Failed to copy to clipboard: {ex.Message}");
      }
    }
    
    partial void OnCorrelationIdFilterChanged(string value)
    {
      ApplyCorrelationIdFilter();
    }

    /// <summary>
    /// Load distributed traces from the backend API.
    /// Phase 5.1.3: Trace Visualization
    /// </summary>
    public async Task LoadTracesAsync()
    {
      IsTracesLoading = true;
      try
      {
        // Request traces from backend
        var response = await _backendClient.GetAsync<TraceListResponse>(
            "/api/v1/diagnostics/traces",
            CancellationToken.None);

        if (response?.Traces != null)
        {
          Traces.Clear();
          foreach (var trace in response.Traces.OrderByDescending(t => t.StartTime))
          {
            Traces.Add(trace);
          }

          // Update summary statistics
          TotalTracesCount = response.Traces.Count;
          var successCount = response.Traces.Count(t => t.Status == "Success" || t.Status == "OK");
          var errorCount = response.Traces.Count(t => t.Status == "Error");
          TraceErrorCount = errorCount;

          if (TotalTracesCount > 0)
          {
            var rate = (double)successCount / TotalTracesCount * 100;
            TraceSuccessRate = $"{rate:F1}%";

            var avgDuration = response.Traces.Average(t => t.DurationMs);
            TraceAvgDuration = avgDuration < 1000
                ? $"{avgDuration:F0}ms"
                : $"{avgDuration / 1000:F2}s";
          }
          else
          {
            TraceSuccessRate = "N/A";
            TraceAvgDuration = "--";
          }
        }

        AddLog("INFO", $"Loaded {TotalTracesCount} traces from backend");
      }
      catch (Exception ex)
      {
        // Log warning but don't break - traces endpoint may not be available
        AddLog("WARNING", $"Failed to load traces: {ex.Message}");

        // Show sample data for demo/development
        LoadSampleTraces();
      }
      finally
      {
        IsTracesLoading = false;
      }
    }

    private void LoadSampleTraces()
    {
      // Provide sample traces for UI development when backend is not available
      Traces.Clear();

      var now = DateTime.Now;
      Traces.Add(new TraceEntry
      {
        TraceId = Guid.NewGuid().ToString("N"),
        StartTime = now.AddMinutes(-1),
        DurationMs = 245,
        Status = "Success",
        OperationName = "voice/synthesize",
        Spans = new ObservableCollection<SpanEntry>
        {
          new SpanEntry { SpanId = "span1", Name = "HTTP Request", DurationMs = 50, Status = "OK" },
          new SpanEntry { SpanId = "span2", Name = "Engine Process", DurationMs = 180, Status = "OK" },
          new SpanEntry { SpanId = "span3", Name = "Response", DurationMs = 15, Status = "OK" }
        }
      });

      Traces.Add(new TraceEntry
      {
        TraceId = Guid.NewGuid().ToString("N"),
        StartTime = now.AddMinutes(-3),
        DurationMs = 1250,
        Status = "Success",
        OperationName = "voice/clone",
        Spans = new ObservableCollection<SpanEntry>
        {
          new SpanEntry { SpanId = "span1", Name = "Upload", DurationMs = 200, Status = "OK" },
          new SpanEntry { SpanId = "span2", Name = "Model Load", DurationMs = 400, Status = "OK" },
          new SpanEntry { SpanId = "span3", Name = "Clone Process", DurationMs = 600, Status = "OK" },
          new SpanEntry { SpanId = "span4", Name = "Finalize", DurationMs = 50, Status = "OK" }
        }
      });

      Traces.Add(new TraceEntry
      {
        TraceId = Guid.NewGuid().ToString("N"),
        StartTime = now.AddMinutes(-5),
        DurationMs = 89,
        Status = "Error",
        OperationName = "health/check",
        Spans = new ObservableCollection<SpanEntry>
        {
          new SpanEntry { SpanId = "span1", Name = "Backend Check", DurationMs = 89, Status = "Error" }
        }
      });

      TotalTracesCount = Traces.Count;
      TraceErrorCount = Traces.Count(t => t.Status == "Error");
      var successCount = Traces.Count(t => t.Status == "Success");
      TraceSuccessRate = TotalTracesCount > 0
          ? $"{(double)successCount / TotalTracesCount * 100:F1}%"
          : "N/A";
      TraceAvgDuration = TotalTracesCount > 0
          ? $"{Traces.Average(t => t.DurationMs):F0}ms"
          : "--";
    }

    private async Task ExportTracesAsync(CancellationToken cancellationToken)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedFileName = $"voicestudio_traces_{DateTime.Now:yyyyMMdd_HHmmss}";
        picker.FileTypeChoices.Add("JSON File", new[] { ".json" });

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (file == null)
          return;

        var exportData = new
        {
          exported_at = DateTime.UtcNow,
          total_traces = TotalTracesCount,
          success_rate = TraceSuccessRate,
          avg_duration = TraceAvgDuration,
          traces = Traces.Select(t => new
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
          })
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            exportData,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await Windows.Storage.FileIO.WriteTextAsync(file, json);

        AddLog("INFO", $"Exported {TotalTracesCount} traces to {file.Name}");
        _toastNotificationService?.ShowSuccess("Export Complete", $"Traces exported to {file.Name}");
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        AddLog("ERROR", $"Failed to export traces: {ex.Message}");
        await HandleErrorAsync(ex, "ExportTraces");
      }
    }

    private void UpdateErrorCounts()
    {
      ErrorLogCount = ErrorLogs.Count;
      ErrorCount = ErrorLogs.Count(e => e.Level == "Error");
      WarningCount = ErrorLogs.Count(e => e.Level == "Warning");
      InfoCount = ErrorLogs.Count(e => e.Level == "Info");
    }

    public async Task ExportCrashBundleAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Create crash bundle with system information and recent logs
        var crashBundle = await CreateCrashBundleAsync();

        // Save to file
        var savePicker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

        savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("Crash Bundle", new List<string>() { ".json" });
        savePicker.SuggestedFileName = $"VoiceStudio_CrashBundle_{DateTime.Now:yyyyMMdd_HHmmss}.json";

        var file = await savePicker.PickSaveFileAsync();
        if (file != null)
        {
          await File.WriteAllTextAsync(file.Path, crashBundle);

          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Toast.Title.CrashBundleExported", "Crash Bundle Exported"),
              ResourceHelper.GetString("Diagnostics.CrashBundleExported", "Crash bundle has been saved successfully"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Diagnostics.ExportCrashBundleFailed", ex.Message);
        AddLog("ERROR", ResourceHelper.FormatString("Diagnostics.ExportCrashBundleFailed", ex.Message));
        await HandleErrorAsync(ex, "ExportCrashBundle");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private Task<string> CreateCrashBundleAsync()
    {
      var crashData = new
      {
        created_at = DateTime.UtcNow,
        app_version = AppVersion,
        machine_name = MachineName,
        user_name = UserName,
        contents = new object[]
          {
                    new
                    {
                        type = "system_info",
                        data = new
                        {
                            app_version = AppVersion,
                            dot_net_version = DotNetVersion,
                            os_version = OsVersion,
                            os_architecture = OsArchitecture,
                            process_architecture = ProcessArchitecture,
                            machine_name = MachineName,
                            user_name = UserName,
                            working_set_mb = WorkingSetMB,
                            processor_count = ProcessorCount,
                            timestamp = DateTime.UtcNow
                        }
                    },
                    new
                    {
                        type = "recent_logs",
                        data = Logs.Take(50).Select(log => new
                        {
                            timestamp = log.Timestamp,
                            level = log.Level,
                            message = log.Message
                        }).ToArray()
                    },
                    new
                    {
                        type = "recent_errors",
                        data = ErrorLogs.Take(20).Select(error => new
                        {
                            timestamp = error.Timestamp,
                            level = error.Level,
                            message = error.Message,
                            context = error.Context,
                            exception_type = error.ExceptionType
                        }).ToArray()
                    }
          }
      };

      return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(crashData, new System.Text.Json.JsonSerializerOptions
      {
        WriteIndented = true
      }));
    }

    protected override void Dispose(bool disposing)
    {
      if (IsDisposed)
      {
        return;
      }

      if (disposing)
      {
        // Stop telemetry refresh
        StopTelemetryRefresh();

        // Unsubscribe from error logging service
        if (_errorLoggingService != null)
        {
          _errorLoggingService.ErrorLogged -= OnErrorLogged;
        }

        // Unsubscribe from analytics service
        if (_analyticsService != null)
        {
          _analyticsService.EventTracked -= OnAnalyticsEventTracked;
        }

        // Unsubscribe from performance profiler
        PerformanceProfiler.BudgetViolated -= OnBudgetViolated;

        // Clear collections
        Logs.Clear();
        ErrorLogs.Clear();
        RecentAnalyticsEvents.Clear();
        BudgetViolations.Clear();
        FeatureFlags.Clear();
        EnvironmentInfo.Clear();
      }

      base.Dispose(disposing);
    }

    // Multi-select methods for logs
    private void SelectAllLogs()
    {
      if (_logsMultiSelectState == null)
        return;

      var state = _multiSelectService?.GetState($"{PanelId}_logs");
      if (state != null)
      {
        foreach (var log in Logs)
        {
          var logId = GetLogId(log);
          state.Add(logId);
        }
      }
      UpdateLogSelectionProperties();
      _multiSelectService?.OnSelectionChanged($"{PanelId}_logs", _logsMultiSelectState);
    }

    private void ClearLogSelection()
    {
      if (_logsMultiSelectState == null)
        return;

      _logsMultiSelectState.Clear();
      UpdateLogSelectionProperties();
      _multiSelectService?.OnSelectionChanged($"{PanelId}_logs", _logsMultiSelectState);
      DeleteSelectedLogsCommand.NotifyCanExecuteChanged();
    }

    private void DeleteSelectedLogs()
    {
      if (_logsMultiSelectState == null || _logsMultiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new System.Collections.Generic.List<string>(_logsMultiSelectState.SelectedIds);
      var logsToRemove = Logs.Where(log => selectedIds.Contains(GetLogId(log))).ToList();
      var count = logsToRemove.Count;

      foreach (var log in logsToRemove)
      {
        Logs.Remove(log);
      }

      ClearLogSelection();

      _toastNotificationService?.ShowSuccess(
          "Logs Deleted",
          $"Removed {count} selected log entr{(count == 1 ? "y" : "ies")}");
    }

    private void UpdateLogSelectionProperties()
    {
      if (_logsMultiSelectState == null)
      {
        SelectedLogCount = 0;
        HasMultipleLogSelection = false;
      }
      else
      {
        SelectedLogCount = _logsMultiSelectState.Count;
        HasMultipleLogSelection = _logsMultiSelectState.Count > 1;
      }
      DeleteSelectedLogsCommand.NotifyCanExecuteChanged();
    }

    private string GetLogId(LogEntry log)
    {
      return $"log_{log.Timestamp.Ticks}";
    }

    // Multi-select methods for error logs
    private void SelectAllErrorLogs()
    {
      if (_errorLogsMultiSelectState == null)
        return;

      foreach (var errorLog in ErrorLogs)
      {
        var errorLogId = GetErrorLogId(errorLog);
        _errorLogsMultiSelectState.Add(errorLogId);
      }
      UpdateErrorLogSelectionProperties();
      _multiSelectService.OnSelectionChanged($"{PanelId}_errorlogs", _errorLogsMultiSelectState);
    }

    private void ClearErrorLogSelection()
    {
      if (_errorLogsMultiSelectState == null)
        return;

      _errorLogsMultiSelectState.Clear();
      UpdateErrorLogSelectionProperties();
      _multiSelectService.OnSelectionChanged($"{PanelId}_errorlogs", _errorLogsMultiSelectState);
      DeleteSelectedErrorLogsCommand.NotifyCanExecuteChanged();
      ExportSelectedErrorLogsCommand.NotifyCanExecuteChanged();
    }

    private void DeleteSelectedErrorLogs()
    {
      if (_errorLogsMultiSelectState == null || _errorLogsMultiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new System.Collections.Generic.List<string>(_errorLogsMultiSelectState.SelectedIds);
      var errorLogsToRemove = ErrorLogs.Where(errorLog => selectedIds.Contains(GetErrorLogId(errorLog))).ToList();
      var count = errorLogsToRemove.Count;

      foreach (var errorLog in errorLogsToRemove)
      {
        ErrorLogs.Remove(errorLog);
      }

      UpdateErrorCounts();
      ClearErrorLogSelection();

      _toastNotificationService?.ShowSuccess(
          ResourceHelper.GetString("Toast.Title.ErrorLogsDeleted", "Error Logs Deleted"),
          ResourceHelper.FormatString("Diagnostics.ErrorLogsDeleted", count, count == 1 ? "y" : "ies"));
    }

    private async Task ExportSelectedErrorLogsAsync(CancellationToken cancellationToken)
    {
      if (_errorLogsMultiSelectState == null || _errorLogsMultiSelectState.SelectedIds.Count == 0)
        return;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedFileName = $"voicestudio_selected_errors_{DateTime.Now:yyyyMMdd_HHmmss}";
        picker.FileTypeChoices.Add(ResourceHelper.GetString("Diagnostics.FileTypeJSON", "JSON File"), new[] { ".json" });
        picker.FileTypeChoices.Add(ResourceHelper.GetString("Diagnostics.FileTypeText", "Text File"), new[] { ".txt" });

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (file == null)
          return;

        var selectedIds = new System.Collections.Generic.List<string>(_errorLogsMultiSelectState.SelectedIds);
        var selectedErrorLogs = ErrorLogs.Where(errorLog => selectedIds.Contains(GetErrorLogId(errorLog))).ToList();

        if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
          // Export selected logs as JSON
          var json = System.Text.Json.JsonSerializer.Serialize(
              selectedErrorLogs.Select(e => new
              {
                e.Timestamp,
                e.Level,
                e.Message,
                e.Context,
                e.ExceptionType,
                e.StackTrace,
                e.Metadata
              }),
              new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
          );
          await Windows.Storage.FileIO.WriteTextAsync(file, json);
        }
        else
        {
          // Export as text format
          var lines = selectedErrorLogs.Select(e => e.FormattedLineWithContext);
          await Windows.Storage.FileIO.WriteLinesAsync(file, lines);
        }

        var count = selectedErrorLogs.Count;
        AddLog("INFO", ResourceHelper.FormatString("Diagnostics.SelectedErrorLogsExported", count, file.Name));

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"),
            ResourceHelper.FormatString("Diagnostics.SelectedErrorLogsExported", count, file.Name));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        AddLog("ERROR", $"Failed to export selected error logs: {ex.Message}");
        await HandleErrorAsync(ex, "ExportSelectedErrorLogs");
      }
    }

    private async Task ExportCrashBundleAsyncInternal()
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedFileName = $"voicestudio_crash_bundle_{DateTime.Now:yyyyMMdd_HHmmss}";
        picker.FileTypeChoices.Add("ZIP Archive", new[] { ".zip" });

        var file = await picker.PickSaveFileAsync();
        if (file == null)
          return;

        // Create temporary directory for bundle contents
        var tempDir = Path.Combine(Path.GetTempPath(), $"VoiceStudio_CrashBundle_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
          // 1. Export logs
          var logsPath = Path.Combine(tempDir, "logs.json");
          var logsData = new
          {
            application_logs = Logs.Select(l => new
            {
              l.Timestamp,
              l.Level,
              l.Message
            }),
            error_logs = ErrorLogs.Select(e => new
            {
              e.Timestamp,
              e.Level,
              e.Message,
              e.Context,
              e.ExceptionType,
              e.StackTrace,
              e.Metadata
            })
          };
          await File.WriteAllTextAsync(logsPath, System.Text.Json.JsonSerializer.Serialize(logsData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

          // 2. Export environment report
          var envPath = Path.Combine(tempDir, "environment.json");
          var envData = new
          {
            app_version = AppVersion,
            dot_net_version = DotNetVersion,
            os_version = OsVersion,
            os_architecture = OsArchitecture,
            process_architecture = ProcessArchitecture,
            machine_name = MachineName,
            user_name = UserName,
            working_set_mb = WorkingSetMB,
            processor_count = ProcessorCount,
            timestamp = DateTime.UtcNow,
            environment_variables = new Dictionary<string, string>
            {
              ["DOTNET_ENVIRONMENT"] = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production",
              ["ASPNETCORE_ENVIRONMENT"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            }
          };
          await File.WriteAllTextAsync(envPath, System.Text.Json.JsonSerializer.Serialize(envData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

          // 3. Export recent actions (analytics events and telemetry)
          var actionsPath = Path.Combine(tempDir, "recent_actions.json");
          var actionsData = new
          {
            recent_analytics = RecentAnalyticsEvents.Select(a => new
            {
              a.Timestamp,
              a.EventName,
              a.FlowId,
              a.Properties
            }),
            telemetry = Telemetry != null ? new
            {
              CpuUsage = Telemetry.CpuPct,
              GpuUsage = Telemetry.VramPct,
              MemoryUsage = Telemetry.RamPct,
              // Timestamp not available in Telemetry object
            } : null,
            budget_violations = BudgetViolations.Select(b => new
            {
              b.Timestamp,
              operation_name = b.OperationName,
              budget_ms = b.BudgetMs,
              actual_ms = b.ActualMs,
              violation_amount = b.ViolationAmount,
              violation_percent = b.ViolationPercent
            })
          };
          await File.WriteAllTextAsync(actionsPath, System.Text.Json.JsonSerializer.Serialize(actionsData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

          // 4. Create bundle metadata
          var metadataPath = Path.Combine(tempDir, "bundle_metadata.json");
          var metadata = new
          {
            bundle_version = "1.0",
            created_at = DateTime.UtcNow,
            app_version = AppVersion,
            machine_name = MachineName,
            user_name = UserName,
            contents = new[]
              {
                            "logs.json - Application and error logs",
                            "environment.json - System environment information",
                            "recent_actions.json - Recent user actions and telemetry"
                        }
          };
          await File.WriteAllTextAsync(metadataPath, System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

          // 5. Create ZIP archive
          await using (var zipStream = await file.OpenStreamForWriteAsync())
          using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
          {
            foreach (var filePath in Directory.GetFiles(tempDir))
            {
              var entryName = Path.GetFileName(filePath);
              var entry = archive.CreateEntry(entryName);
              await using (var entryStream = entry.Open())
              await using (var fileStream = File.OpenRead(filePath))
              {
                await fileStream.CopyToAsync(entryStream);
              }
            }
          }

          AddLog("INFO", $"Crash bundle exported to {file.Name}");

          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"),
              $"Crash bundle exported to {file.Name}");
        }
        finally
        {
          // Clean up temp directory
          if (Directory.Exists(tempDir))
          {
            Directory.Delete(tempDir, true);
          }
        }
      }
      catch (Exception ex)
      {
        AddLog("ERROR", $"Failed to export crash bundle: {ex.Message}");
        await HandleErrorAsync(ex, "ExportCrashBundle");
      }
    }

    private void UpdateErrorLogSelectionProperties()
    {
      if (_errorLogsMultiSelectState == null)
      {
        SelectedErrorLogCount = 0;
        HasMultipleErrorLogSelection = false;
      }
      else
      {
        SelectedErrorLogCount = _errorLogsMultiSelectState.Count;
        HasMultipleErrorLogSelection = _errorLogsMultiSelectState.Count > 1;
      }
      DeleteSelectedErrorLogsCommand.NotifyCanExecuteChanged();
      ExportSelectedErrorLogsCommand.NotifyCanExecuteChanged();
    }

    private string GetErrorLogId(ErrorLogEntryViewModel errorLog)
    {
      return $"errorlog_{errorLog.Timestamp.Ticks}";
    }
  }

  /// <summary>
  /// Represents a single log entry.
  /// </summary>
  public class LogEntry : ObservableObject
  {
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string FormattedLine => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
  }

  /// <summary>
  /// ViewModel for error log entries.
  /// </summary>
  public class ErrorLogEntryViewModel : ObservableObject
  {
    public ErrorLogEntryViewModel(ErrorLogEntry entry)
    {
      Timestamp = entry.Timestamp;
      Level = entry.Level;
      Message = entry.Message;
      Context = entry.Context ?? string.Empty;
      ExceptionType = entry.ExceptionType ?? string.Empty;
      StackTrace = entry.StackTrace ?? string.Empty;
      Metadata = entry.Metadata;
    }

    public DateTime Timestamp { get; }
    public string Level { get; }
    public string Message { get; }
    public string Context { get; }
    public string ExceptionType { get; }
    public string StackTrace { get; }
    public System.Collections.Generic.Dictionary<string, object>? Metadata { get; }

    public string FormattedLine => $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";
    public string FormattedLineWithContext =>
        string.IsNullOrWhiteSpace(Context)
            ? FormattedLine
            : $"{FormattedLine} (Context: {Context})";
  }

  /// <summary>
  /// ViewModel for budget violation display.
  /// </summary>
  public class BudgetViolationViewModel
  {
    public BudgetViolationViewModel(BudgetViolationEventArgs args)
    {
      OperationName = args.OperationName;
      BudgetMs = args.BudgetMs;
      ActualMs = args.ActualMs;
      ViolationAmount = ActualMs - BudgetMs;
      ViolationPercent = args.ViolationPercent;
      Timestamp = DateTime.UtcNow; // Note: BudgetViolationEventArgs doesn't have Timestamp, so we use current time
    }

    public string OperationName { get; }
    public int BudgetMs { get; }
    public long ActualMs { get; }
    public long ViolationAmount { get; }
    public double ViolationPercent { get; }
    public DateTime Timestamp { get; }

    public string FormattedViolation => $"{OperationName}: {ActualMs}ms (budget: {BudgetMs}ms, exceeded by {ViolationAmount}ms / {ViolationPercent:F1}%)";
  }

  /// <summary>
  /// ViewModel for feature flag display.
  /// </summary>
  public class FeatureFlagViewModel : ObservableObject
  {
    private bool _isEnabled;
    private readonly IFeatureFlagsService? _featureFlagsService;

    public FeatureFlagViewModel(string flagName, bool isEnabled, string description, IFeatureFlagsService? featureFlagsService)
    {
      FlagName = flagName;
      _isEnabled = isEnabled;
      Description = description;
      _featureFlagsService = featureFlagsService;
    }

    public string FlagName { get; }
    public string Description { get; }

    public bool IsEnabled
    {
      get => _isEnabled;
      set
      {
        if (_isEnabled != value)
        {
          _isEnabled = value;
          _featureFlagsService?.SetFlag(FlagName, value);
          OnPropertyChanged();
        }
      }
    }
  }

  /// <summary>
  /// Item for environment info display.
  /// </summary>
  public class EnvironmentInfoItem
  {
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
  }

  /// <summary>
  /// ViewModel for audit log entry display.
  /// </summary>
  public class AuditEntryViewModel
  {
    public AuditEntryViewModel(AuditEntry entry)
    {
      EntryId = entry.EntryId;
      Timestamp = entry.Timestamp;
      EventType = entry.EventType;
      TaskId = entry.TaskId ?? "-";
      Role = entry.Role ?? "System";
      FilePath = entry.FilePath ?? "-";
      FileName = string.IsNullOrEmpty(entry.FilePath) ? "-" : Path.GetFileName(entry.FilePath);
      Summary = entry.Summary;
      Subsystem = entry.Subsystem ?? "unknown";
      Severity = entry.Severity ?? "info";
      ErrorCode = entry.ErrorCode ?? "";
      Message = entry.Message ?? "";
    }

    public string EntryId { get; }
    public string Timestamp { get; }
    public string EventType { get; }
    public string TaskId { get; }
    public string Role { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public string Summary { get; }
    public string Subsystem { get; }
    public string Severity { get; }
    public string ErrorCode { get; }
    public string Message { get; }

    public string TimestampFormatted => Timestamp.Length > 19
      ? Timestamp[..19].Replace("T", " ")
      : Timestamp;

    public string SeverityColor => Severity.ToLowerInvariant() switch
    {
      "error" => "#FF5252",
      "warning" => "#FFC107",
      "info" => "#2196F3",
      _ => "#9E9E9E"
    };
  }

  /// <summary>
  /// Entry for tracking correlation IDs.
  /// Used for distributed tracing and log correlation.
  /// </summary>
  public class CorrelationIdEntry
  {
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string ShortId => CorrelationId.Length > 8
        ? $"{CorrelationId[..8]}..."
        : CorrelationId;

    public string FormattedEntry => $"[{Timestamp:HH:mm:ss}] {ShortId} ({Source})";
  }

  /// <summary>
  /// Represents a span within a trace for timeline visualization.
  /// Phase 5.1.3: Distributed Tracing Visualization
  /// </summary>
  public class SpanEntry : ObservableObject
  {
    public string SpanId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public string Status { get; set; } = "Unknown";
    public string? ParentSpanId { get; set; }

    /// <summary>Gets the visual width percentage for timeline bar.</summary>
    public double WidthPercent => Math.Max(8, Math.Min(100, DurationMs / 10.0));

    /// <summary>Gets the status color for visualization.</summary>
    public string StatusColor => Status switch
    {
      "OK" or "Success" => "#4CAF50",
      "Error" => "#F44336",
      "Pending" => "#FFC107",
      _ => "#2196F3"
    };

    /// <summary>Gets the tooltip text for the span.</summary>
    public string TooltipText => $"{Name}: {DurationMs:F0}ms ({Status})";
  }

  /// <summary>
  /// Represents a distributed trace entry for timeline visualization.
  /// Phase 5.1.3: Distributed Tracing Visualization
  /// </summary>
  public class TraceEntry : ObservableObject
  {
    public string TraceId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public double DurationMs { get; set; }
    public string Status { get; set; } = "Unknown";
    public string OperationName { get; set; } = string.Empty;
    public ObservableCollection<SpanEntry> Spans { get; set; } = new();

    /// <summary>Gets the shortened trace ID for display.</summary>
    public string ShortTraceId => TraceId.Length > 12
        ? $"{TraceId[..12]}..."
        : TraceId;

    /// <summary>Gets the formatted start time.</summary>
    public string StartTimeFormatted => StartTime.ToString("HH:mm:ss.fff");

    /// <summary>Gets the formatted duration string.</summary>
    public string DurationFormatted => DurationMs < 1000
        ? $"{DurationMs:F0}ms"
        : $"{DurationMs / 1000:F2}s";

    /// <summary>Gets the duration as a percentage for progress bar (scaled for visibility).</summary>
    public double DurationPercent => Math.Min(100, DurationMs / 50.0);

    /// <summary>Gets the status color for visualization.</summary>
    public string StatusColor => Status switch
    {
      "Success" or "OK" => "#4CAF50",
      "Error" => "#F44336",
      "Pending" => "#FFC107",
      _ => "#9E9E9E"
    };
  }

  /// <summary>
  /// Response model for trace list API.
  /// Phase 5.1.3: Distributed Tracing Visualization
  /// </summary>
  public class TraceListResponse
  {
    public List<TraceEntry> Traces { get; set; } = new();
    public int TotalCount { get; set; }
  }
}