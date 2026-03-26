using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

/// <summary>
/// Manages the backend Python process lifecycle.
/// Auto-starts the backend on app launch and monitors health.
/// </summary>
public sealed class BackendProcessManager : IDisposable
{
    private readonly string _backendUrl;
    private readonly HttpClient _httpClient;
    private readonly IStartupDiagnosticsWriter? _diagnostics;
    private Process? _backendProcess;
    private bool _isStarting;
    private bool _disposed;

    /// <summary>
    /// Event raised when backend starts successfully.
    /// </summary>
    public event EventHandler? BackendStarted;

    /// <summary>
    /// Event raised when backend fails to start.
    /// </summary>
    public event EventHandler<BackendStartFailedEventArgs>? BackendStartFailed;

    /// <summary>
    /// Last failure info, set when BackendStartFailed is raised. Used by StartupRetryCoordinator for category-aware retry.
    /// </summary>
    public BackendStartFailedEventArgs? LastFailure { get; private set; }

    /// <summary>
    /// Event raised when backend process exits unexpectedly.
    /// </summary>
    public event EventHandler? BackendExited;

    /// <summary>
    /// Gets whether the backend process is currently running.
    /// </summary>
    public bool IsRunning => _backendProcess is { HasExited: false };

    /// <summary>
    /// Gets whether the backend is starting.
    /// </summary>
    public bool IsStarting => _isStarting;

    public BackendProcessManager(string backendUrl = "http://localhost:8000", IStartupDiagnosticsWriter? diagnostics = null)
    {
        _backendUrl = backendUrl;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(backendUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Starts the backend process if not already running.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if backend is running, false if failed to start.</returns>
    public async Task<bool> EnsureBackendRunningAsync(CancellationToken cancellationToken = default)
    {
        // Check if already running
        if (await IsBackendHealthyAsync(cancellationToken))
        {
            Debug.WriteLine("[BackendProcessManager] Backend already running");
            LastFailure = null;
            BackendStarted?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // Check if process is running but not responding
        if (IsRunning)
        {
            Debug.WriteLine("[BackendProcessManager] Process running but not healthy, waiting...");
            // Give it more time
            if (await WaitForHealthAsync(TimeSpan.FromSeconds(10), cancellationToken))
            {
                BackendStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }

            // Kill unresponsive process
            try
            {
                _backendProcess?.Kill();
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Failed to kill unresponsive backend: {ex.Message}", "BackendProcessManager");
            }
        }

        // Start new process
        return await StartBackendProcessAsync(cancellationToken);
    }

    /// <summary>
    /// Starts the backend Python process.
    /// </summary>
    private async Task<bool> StartBackendProcessAsync(CancellationToken cancellationToken = default)
    {
        if (_isStarting)
        {
            Debug.WriteLine("[BackendProcessManager] Already starting");
            return false;
        }

        _isStarting = true;
        _diagnostics?.BeginSession();
        try
        {
            var port = GetBackendPort();
            _diagnostics?.Log("backend_port", port.ToString());

            var portInUse = await IsPortInUseAsync(port, cancellationToken);
            _diagnostics?.Log("port_occupied", portInUse.ToString());

            if (portInUse)
            {
            if (await IsBackendHealthyAsync(cancellationToken))
            {
                Debug.WriteLine($"[BackendProcessManager] Port {port} in use and backend healthy");
                LastFailure = null;
                BackendStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }

                var error = $"Port {port} is in use by another process. Stop the other process or set VOICESTUDIO_API_PORT to use a different port.";
                Debug.WriteLine($"[BackendProcessManager] {error}");
                _diagnostics?.LogFailure("port_collision", error);
                _diagnostics?.EndSession();
                LastFailure = new BackendStartFailedEventArgs(BackendStartFailureCategory.PortCollision, error);
                BackendStartFailed?.Invoke(this, LastFailure);
                return false;
            }

            // Find app/runtime root (production-grade: env, installed, portable; dev fallback only in Debug)
            var appRoot = FindAppRoot(out var rootSource);
            _diagnostics?.Log("app_root_source", rootSource);
            _diagnostics?.Log("app_root_path", appRoot ?? "(null)");

            if (appRoot == null)
            {
                var error = "Could not find VoiceStudio app root. Set VOICESTUDIO_APP_ROOT to the app directory.";
                Debug.WriteLine($"[BackendProcessManager] {error}");
                _diagnostics?.LogFailure("invalid_app_root", error);
                _diagnostics?.EndSession();
                LastFailure = new BackendStartFailedEventArgs(BackendStartFailureCategory.InvalidAppRoot, error);
                BackendStartFailed?.Invoke(this, LastFailure);
                return false;
            }

            // Search for Python in priority order:
            // 1. Bundled runtime (installed by installer/prepare-runtime.ps1)
            // 2. Local venv
            // 3. Alternate venv (.venv)
            var pythonCandidates = new[]
            {
                Path.Combine(appRoot, "Runtime", "python", "python.exe"),
                Path.Combine(appRoot, "venv", "Scripts", "python.exe"),
                Path.Combine(appRoot, ".venv", "Scripts", "python.exe"),
            };

            var venvPython = Array.Find(pythonCandidates, File.Exists);
            _diagnostics?.Log("python_candidates", string.Join(";", pythonCandidates));
            _diagnostics?.Log("python_chosen", venvPython ?? "(none)");

            if (venvPython == null)
            {
                var error = "Python runtime not found. Checked: " +
                    string.Join(", ", pythonCandidates.Select(p => Path.GetDirectoryName(p) ?? p));
                Debug.WriteLine($"[BackendProcessManager] {error}");
                _diagnostics?.LogFailure("missing_python_runtime", error);
                _diagnostics?.EndSession();
                LastFailure = new BackendStartFailedEventArgs(BackendStartFailureCategory.RuntimeMissing, error);
                BackendStartFailed?.Invoke(this, LastFailure);
                return false;
            }

            Debug.WriteLine($"[BackendProcessManager] App root: {appRoot} (source: {rootSource})");
            Debug.WriteLine($"[BackendProcessManager] Python: {venvPython}");

            // Prepare process
            var psi = new ProcessStartInfo
            {
                FileName = venvPython,
                Arguments = $"-m uvicorn backend.api.main:app --host 127.0.0.1 --port {port}",
                WorkingDirectory = appRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set environment
            psi.Environment["PYTHONPATH"] = appRoot;
            psi.Environment["PYTHONUNBUFFERED"] = "1";

            // Point to bundled FFmpeg if available
            var bundledFfmpeg = Path.Combine(appRoot, "Runtime", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(bundledFfmpeg))
            {
                psi.Environment["VOICESTUDIO_FFMPEG_PATH"] = bundledFfmpeg;
            }

            // Detect portable mode
            var portableFlag = Path.Combine(appRoot, "portable.flag");
            if (File.Exists(portableFlag))
            {
                psi.Environment["VOICESTUDIO_DATA_DIR"] = Path.Combine(appRoot, "data");
                psi.Environment["VOICESTUDIO_MODELS_DIR"] = Path.Combine(appRoot, "models");
                psi.Environment["VOICESTUDIO_DB_PATH"] = Path.Combine(appRoot, "data", "voicestudio.db");
                Debug.WriteLine("[BackendProcessManager] Portable mode active - data stored relative to app root");
            }

            // Item 26: Safe Demo Mode - pass through so backend can disable cloning/export
            var demoMode = Environment.GetEnvironmentVariable("VOICESTUDIO_DEMO_MODE");
            if (!string.IsNullOrEmpty(demoMode))
            {
                psi.Environment["VOICESTUDIO_DEMO_MODE"] = demoMode;
            }

            Debug.WriteLine($"[BackendProcessManager] Starting backend: {psi.FileName} {psi.Arguments}");
            Debug.WriteLine($"[BackendProcessManager] Working directory: {appRoot}");

            var sessionStart = Stopwatch.StartNew();
            var firstStdoutLogged = 0;
            var firstStderrLogged = 0;

            _backendProcess = new Process { StartInfo = psi };
            _backendProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"[Backend] {e.Data}");
                    if (Interlocked.Exchange(ref firstStdoutLogged, 1) == 0)
                    {
                        _diagnostics?.Log("milestone_first_stdout_ms", sessionStart.ElapsedMilliseconds.ToString());
                    }
                }
            };
            _backendProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"[Backend ERR] {e.Data}");
                    if (Interlocked.Exchange(ref firstStderrLogged, 1) == 0)
                    {
                        _diagnostics?.Log("milestone_first_stderr_ms", sessionStart.ElapsedMilliseconds.ToString());
                    }
                }
            };
            _backendProcess.EnableRaisingEvents = true;
            _backendProcess.Exited += (s, e) =>
            {
                Debug.WriteLine("[BackendProcessManager] Backend process exited");
                BackendExited?.Invoke(this, EventArgs.Empty);
            };

            _backendProcess.Start();
            _backendProcess.BeginOutputReadLine();
            _backendProcess.BeginErrorReadLine();

            Debug.WriteLine($"[BackendProcessManager] Backend process started (PID: {_backendProcess.Id})");
            _diagnostics?.Log("milestone_process_started_ms", sessionStart.ElapsedMilliseconds.ToString());
            _diagnostics?.Log("process_started", $"PID={_backendProcess.Id}");

            // Wait for backend to become healthy (45s for first launch to allow cold Python/uvicorn startup)
            var healthTimeout = TimeSpan.FromSeconds(45);
            var (healthy, attempts, elapsed) = await WaitForHealthWithMetricsAsync(healthTimeout, cancellationToken, sessionStart, port);
            _diagnostics?.Log("health_probe_attempts", attempts.ToString());
            _diagnostics?.Log("health_probe_elapsed_ms", elapsed.TotalMilliseconds.ToString("F0"));

            if (healthy)
            {
                Debug.WriteLine("[BackendProcessManager] Backend is healthy");
                _diagnostics?.Log("result", "success");
                _diagnostics?.EndSession();
                LastFailure = null;
                BackendStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }
            else
            {
                var error = "Backend started but did not become healthy within timeout";
                Debug.WriteLine($"[BackendProcessManager] {error}");
                _diagnostics?.LogFailure("health_timeout", error);
                _diagnostics?.EndSession();
                LastFailure = new BackendStartFailedEventArgs(BackendStartFailureCategory.HealthTimeout, error);
                BackendStartFailed?.Invoke(this, LastFailure);
                return false;
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to start backend: {ex.Message}";
            Debug.WriteLine($"[BackendProcessManager] {error}");
            _diagnostics?.LogFailure("spawn_failure", error);
            _diagnostics?.EndSession();
            ErrorLogger.LogError($"Failed to start backend: {ex.Message}", "BackendProcessManager.StartBackendProcessAsync");
            LastFailure = new BackendStartFailedEventArgs(BackendStartFailureCategory.SpawnFailure, error);
            BackendStartFailed?.Invoke(this, LastFailure);
            return false;
        }
        finally
        {
            _isStarting = false;
        }
    }

    /// <summary>
    /// Waits for the backend to become healthy.
    /// </summary>
    private async Task<bool> WaitForHealthAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var (healthy, _, _) = await WaitForHealthWithMetricsAsync(timeout, cancellationToken, null, 0);
        return healthy;
    }

    /// <summary>
    /// Waits for the backend to become healthy, returning attempt count and elapsed time.
    /// Logs milestone_first_tcp_ms and milestone_first_health_ms when diagnostics and port are provided.
    /// </summary>
    private async Task<(bool Success, int Attempts, TimeSpan Elapsed)> WaitForHealthWithMetricsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Stopwatch? sessionStart = null,
        int port = 0)
    {
        var sw = Stopwatch.StartNew();
        var attempts = 0;
        var firstTcpLogged = 0;
        while (sw.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            attempts++;

            // Log first TCP reachability milestone (once, when TCP first becomes reachable)
            if (port > 0 && sessionStart != null && Volatile.Read(ref firstTcpLogged) == 0)
            {
                if (await IsTcpReachableAsync(port, cancellationToken) && Interlocked.Exchange(ref firstTcpLogged, 1) == 0)
                {
                    _diagnostics?.Log("milestone_first_tcp_ms", sessionStart.ElapsedMilliseconds.ToString());
                }
            }

            if (await IsBackendHealthyAsync(cancellationToken))
            {
                if (sessionStart != null)
                {
                    _diagnostics?.Log("milestone_first_health_ms", sessionStart.ElapsedMilliseconds.ToString());
                }
                return (true, attempts, sw.Elapsed);
            }

            await Task.Delay(500, cancellationToken);
        }

        return (false, attempts, sw.Elapsed);
    }

    private static async Task<bool> IsTcpReachableAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the backend is healthy by calling /health endpoint.
    /// </summary>
    public async Task<bool> IsBackendHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets backend port from URL or VOICESTUDIO_API_PORT env.
    /// </summary>
    private int GetBackendPort()
    {
        try
        {
            var envPort = Environment.GetEnvironmentVariable("VOICESTUDIO_API_PORT");
            if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out var p) && p > 0 && p < 65536)
            {
                return p;
            }
            var uri = new Uri(_backendUrl);
            return uri.Port > 0 ? uri.Port : 8000;
        }
        catch
        {
            return 8000;
        }
    }

    /// <summary>
    /// Checks if the given port is in use (TCP connect).
    /// </summary>
    private async Task<bool> IsPortInUseAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cancellationToken);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the backend process.
    /// </summary>
    public void StopBackend()
    {
        if (_backendProcess == null || _backendProcess.HasExited)
        {
            return;
        }

        try
        {
            Debug.WriteLine("[BackendProcessManager] Stopping backend...");
            _backendProcess.Kill(entireProcessTree: true);
            _backendProcess.WaitForExit(5000);
            Debug.WriteLine("[BackendProcessManager] Backend stopped");
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Failed to stop backend: {ex.Message}", "BackendProcessManager");
        }
    }

    /// <summary>
    /// Finds the VoiceStudio app root using production-grade strategy.
    /// Order: VOICESTUDIO_APP_ROOT env, exe directory (installed/portable), dev walk-up (Debug only).
    /// No hardcoded paths.
    /// </summary>
    private static string? FindAppRoot(out string source)
    {
        source = "unknown";

        // 1. Explicit override via environment
        var envRoot = Environment.GetEnvironmentVariable("VOICESTUDIO_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            if (HasBackendMarker(envRoot))
            {
                source = "VOICESTUDIO_APP_ROOT";
                return Path.GetFullPath(envRoot);
            }
            // Explicit override set but invalid (no backend marker) — do not fall through to other strategies
            source = "VOICESTUDIO_APP_ROOT";
            return null;
        }

        // 2. Exe directory (installed: exe is in app root; portable: portable.flag next to exe)
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = AppContext.BaseDirectory;
        }

        var exeDir = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrEmpty(exeDir) && HasBackendMarker(exeDir))
        {
            source = "exe_dir";
            return exeDir;
        }

        // 3. Parent of exe (e.g. exe in bin/ subdir)
        var parentDir = Directory.GetParent(exeDir)?.FullName;
        if (!string.IsNullOrEmpty(parentDir) && HasBackendMarker(parentDir))
        {
            source = "exe_parent";
            return parentDir;
        }

#if DEBUG
        // 4. Dev fallback: walk up for .git or VoiceStudio.sln (Debug only)
        var dir = exeDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "VoiceStudio.sln")) ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                if (HasBackendMarker(dir))
                {
                    source = "dev_walk";
                    return dir;
                }
            }

            var parent = Directory.GetParent(dir);
            if (parent == null)
            {
                break;
            }

            dir = parent.FullName;
        }
#endif

        return null;
    }

    private static bool HasBackendMarker(string dir)
    {
        return Directory.Exists(Path.Combine(dir, "backend")) &&
               File.Exists(Path.Combine(dir, "backend", "api", "main.py"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopBackend();
        _backendProcess?.Dispose();
        _httpClient.Dispose();
    }
}
