using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.App
{
    public sealed partial class MainWindow
    {
        private Microsoft.UI.Xaml.DispatcherTimer? _statusBarTimer;
        private TimeSpan _lastProcessorTime;
        private DateTime _lastCpuCheck = DateTime.MinValue;
        private int _lastCpuPercent;
        private int _lastGpuPercent;
        private int _lastLatencyMs = -1;

        private void StartStatusBarTimer()
        {
            _statusBarTimer = new Microsoft.UI.Xaml.DispatcherTimer();
            _statusBarTimer.Interval = TimeSpan.FromSeconds(2);
            _statusBarTimer.Tick += (_, _) => UpdateStatusBarMetrics();
            _statusBarTimer.Start();

            // Initialize CPU tracking
            try
            {
                var process = Process.GetCurrentProcess();
                _lastProcessorTime = process.TotalProcessorTime;
                _lastCpuCheck = DateTime.UtcNow;
            }
            // ALLOWED: empty catch - CPU telemetry is non-critical
            catch (Exception ex) { Debug.WriteLine($"CPU telemetry init failed: {ex.Message}"); }

            // Update immediately
            UpdateStatusBarMetrics();
        }

        private void UpdateStatusBarMetrics()
        {
            try
            {
                var process = Process.GetCurrentProcess();

                // Calculate RAM usage
                var ramMb = process.WorkingSet64 / (1024 * 1024);
                var totalRamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                var ramPct = totalRamMb > 0 ? (int)(ramMb * 100 / totalRamMb) : 0;

                // Calculate CPU usage based on process time delta
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

                // GPU usage: fetched from backend via UpdateGpuAndLatencyAsync()
                // Phase 9 Gap Resolution (2026-02-10): GPU telemetry is now integrated.
                // Real metrics are retrieved from /api/engine/telemetry endpoint.
                // See UpdateGpuAndLatencyAsync() below for the actual implementation.

                var cpuText = FindNameOnContent("CpuText") as TextBlock;
                var gpuText = FindNameOnContent("GpuText") as TextBlock;
                var ramText = FindNameOnContent("RamText") as TextBlock;
                var clockText = FindNameOnContent("ClockText") as TextBlock;
                var latencyText = FindNameOnContent("LatencyText") as TextBlock;

                if (cpuText != null) cpuText.Text = $"CPU {_lastCpuPercent}%";
                if (gpuText != null) gpuText.Text = $"GPU {_lastGpuPercent}%";
                if (ramText != null) ramText.Text = $"RAM {ramPct}%";
                if (clockText != null) clockText.Text = DateTime.Now.ToString("HH:mm");
                if (latencyText != null && _lastLatencyMs >= 0) latencyText.Text = $"{_lastLatencyMs}ms";

                // Async update for GPU and latency (non-blocking)
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
                // Ping backend to get latency
                var healthClient = ServiceProvider.GetHealthVersionClient();
                var telemetryClient = ServiceProvider.GetTelemetryClient();
                if (healthClient != null && telemetryClient != null)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var isConnected = await healthClient.CheckHealthAsync();
                    stopwatch.Stop();

                    if (isConnected)
                    {
                        _lastLatencyMs = (int)stopwatch.ElapsedMilliseconds;

                        // Try to get GPU/VRAM usage from backend telemetry
                        try
                        {
                            var telemetry = await telemetryClient.GetTelemetryAsync();
                            if (telemetry != null)
                            {
                                _lastGpuPercent = (int)telemetry.VramPct;
                            }
                        }
                        // ALLOWED: empty catch - GPU telemetry is best-effort
                        catch
                        {
                        }
                    }
                }
            }
            // ALLOWED: empty catch - network errors are non-critical for telemetry
            catch
            {
            }
        }

        /// <summary>
        /// Updates the clock display in the status bar.
        /// </summary>
        private void UpdateClock()
        {
            var clockText = FindNameOnContent("ClockText") as TextBlock;
            if (clockText != null)
            {
                clockText.Text = DateTime.Now.ToString("h:mm tt");
            }
        }
    }
}
