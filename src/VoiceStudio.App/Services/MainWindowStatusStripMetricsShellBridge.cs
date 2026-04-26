using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 18: 2-second status-strip CPU/RAM/GPU/latency metrics (DispatcherTimer).
/// Does not own <see cref="MainWindowStatusStripClockShellBridge"/> / ClockText.
/// </summary>
public sealed class MainWindowStatusStripMetricsShellBridge
{
    private readonly Func<TextBlock?> _getCpuText;
    private readonly Func<TextBlock?> _getGpuText;
    private readonly Func<TextBlock?> _getRamText;
    private readonly Func<TextBlock?> _getLatencyText;
    private readonly Func<IHealthVersionClient?> _getHealthClient;
    private readonly Func<ITelemetryClient?> _getTelemetryClient;

    private DispatcherTimer? _metricsTimer;
    private TimeSpan _lastProcessorTime;
    private DateTime _lastCpuCheck = DateTime.MinValue;
    private int _lastCpuPercent;
    private int _lastGpuPercent;
    private int _lastLatencyMs = -1;

    public MainWindowStatusStripMetricsShellBridge(
        Func<TextBlock?> getCpuText,
        Func<TextBlock?> getGpuText,
        Func<TextBlock?> getRamText,
        Func<TextBlock?> getLatencyText,
        Func<IHealthVersionClient?> getHealthClient,
        Func<ITelemetryClient?> getTelemetryClient)
    {
        _getCpuText = getCpuText ?? throw new ArgumentNullException(nameof(getCpuText));
        _getGpuText = getGpuText ?? throw new ArgumentNullException(nameof(getGpuText));
        _getRamText = getRamText ?? throw new ArgumentNullException(nameof(getRamText));
        _getLatencyText = getLatencyText ?? throw new ArgumentNullException(nameof(getLatencyText));
        _getHealthClient = getHealthClient ?? throw new ArgumentNullException(nameof(getHealthClient));
        _getTelemetryClient = getTelemetryClient ?? throw new ArgumentNullException(nameof(getTelemetryClient));
    }

    /// <summary>
    /// Starts the 2-second metrics dispatcher timer. Idempotent: stops any prior timer first.
    /// </summary>
    public void BeginMetricsTimer()
    {
        StopMetricsTimer();
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _metricsTimer.Tick += OnMetricsDispatcherTick;
        _metricsTimer.Start();

        try
        {
            var process = Process.GetCurrentProcess();
            _lastProcessorTime = process.TotalProcessorTime;
            _lastCpuCheck = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CPU telemetry init failed: {ex.Message}");
        }

        OnMetricsTick();
    }

    public void StopMetricsTimer()
    {
        if (_metricsTimer != null)
        {
            _metricsTimer.Tick -= OnMetricsDispatcherTick;
            _metricsTimer.Stop();
            _metricsTimer = null;
        }
    }

    private void OnMetricsDispatcherTick(object? sender, object e) => OnMetricsTick();

    private void OnMetricsTick()
    {
        try
        {
            var process = Process.GetCurrentProcess();

            var ramMb = process.WorkingSet64 / (1024 * 1024);
            var totalRamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            var ramPct = totalRamMb > 0 ? (int)(ramMb * 100 / totalRamMb) : 0;

            var now = DateTime.UtcNow;
            var currentProcessorTime = process.TotalProcessorTime;
            if (_lastCpuCheck != DateTime.MinValue)
            {
                var timeDelta = (now - _lastCpuCheck).TotalMilliseconds;
                if (timeDelta > 0)
                {
                    var cpuTimeDelta = (currentProcessorTime - _lastProcessorTime).TotalMilliseconds;
                    var cpuPct = (int)(cpuTimeDelta / timeDelta / Environment.ProcessorCount * 100);
                    _lastCpuPercent = Math.Clamp(cpuPct, 0, 100);
                }
            }

            _lastProcessorTime = currentProcessorTime;
            _lastCpuCheck = now;

            var cpuText = _getCpuText();
            var gpuText = _getGpuText();
            var ramText = _getRamText();
            var latencyText = _getLatencyText();

            if (cpuText != null)
            {
                cpuText.Text = $"CPU {_lastCpuPercent}%";
            }

            if (gpuText != null)
            {
                gpuText.Text = $"GPU {_lastGpuPercent}%";
            }

            if (ramText != null)
            {
                ramText.Text = $"RAM {ramPct}%";
            }

            if (latencyText != null && _lastLatencyMs >= 0)
            {
                latencyText.Text = $"{_lastLatencyMs}ms";
            }

            _ = UpdateGpuAndLatencyAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Status bar update error: {ex.Message}");
        }
    }

    private async Task UpdateGpuAndLatencyAsync()
    {
        try
        {
            var healthClient = _getHealthClient();
            var telemetryClient = _getTelemetryClient();
            if (healthClient != null && telemetryClient != null)
            {
                var stopwatch = Stopwatch.StartNew();
                var isConnected = await healthClient.CheckHealthAsync();
                stopwatch.Stop();

                if (isConnected)
                {
                    _lastLatencyMs = (int)stopwatch.ElapsedMilliseconds;

                    try
                    {
                        var telemetry = await telemetryClient.GetTelemetryAsync();
                        if (telemetry != null)
                        {
                            _lastGpuPercent = (int)telemetry.VramPct;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GPU telemetry fetch: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GPU/latency update: {ex.Message}");
        }
    }
}
