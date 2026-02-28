using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
    public event EventHandler<string>? BackendStartFailed;

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

    public BackendProcessManager(string backendUrl = "http://localhost:8001")
    {
        _backendUrl = backendUrl;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(backendUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
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
        try
        {
            // Find the venv Python executable
            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                var error = "Could not find VoiceStudio repository root";
                Debug.WriteLine($"[BackendProcessManager] {error}");
                BackendStartFailed?.Invoke(this, error);
                return false;
            }

            // Search for Python in priority order:
            // 1. Bundled runtime (installed by installer/prepare-runtime.ps1)
            // 2. Local venv
            // 3. Alternate venv (.venv)
            var pythonCandidates = new[]
            {
                Path.Combine(repoRoot, "Runtime", "python", "python.exe"),
                Path.Combine(repoRoot, "venv", "Scripts", "python.exe"),
                Path.Combine(repoRoot, ".venv", "Scripts", "python.exe"),
            };

            var venvPython = Array.Find(pythonCandidates, File.Exists);

            if (venvPython == null)
            {
                var error = "Python runtime not found. Checked: " +
                    string.Join(", ", pythonCandidates.Select(p => Path.GetDirectoryName(p) ?? p));
                Debug.WriteLine($"[BackendProcessManager] {error}");
                BackendStartFailed?.Invoke(this, error);
                return false;
            }

            // Prepare process
            var psi = new ProcessStartInfo
            {
                FileName = venvPython,
                Arguments = "-m uvicorn backend.api.main:app --host 127.0.0.1 --port 8001",
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set environment
            psi.Environment["PYTHONPATH"] = repoRoot;
            psi.Environment["PYTHONUNBUFFERED"] = "1";

            // Point to bundled FFmpeg if available
            var bundledFfmpeg = Path.Combine(repoRoot, "Runtime", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(bundledFfmpeg))
            {
                psi.Environment["VOICESTUDIO_FFMPEG_PATH"] = bundledFfmpeg;
            }

            // Detect portable mode
            var portableFlag = Path.Combine(repoRoot, "portable.flag");
            if (File.Exists(portableFlag))
            {
                psi.Environment["VOICESTUDIO_DATA_DIR"] = Path.Combine(repoRoot, "data");
                psi.Environment["VOICESTUDIO_MODELS_DIR"] = Path.Combine(repoRoot, "models");
                psi.Environment["VOICESTUDIO_DB_PATH"] = Path.Combine(repoRoot, "data", "voicestudio.db");
                Debug.WriteLine("[BackendProcessManager] Portable mode active - data stored relative to app root");
            }

            Debug.WriteLine($"[BackendProcessManager] Starting backend: {psi.FileName} {psi.Arguments}");
            Debug.WriteLine($"[BackendProcessManager] Working directory: {repoRoot}");

            _backendProcess = new Process { StartInfo = psi };
            _backendProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"[Backend] {e.Data}");
                }
            };
            _backendProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"[Backend ERR] {e.Data}");
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

            // Wait for backend to become healthy
            if (await WaitForHealthAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                Debug.WriteLine("[BackendProcessManager] Backend is healthy");
                BackendStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }
            else
            {
                var error = "Backend started but did not become healthy within timeout";
                Debug.WriteLine($"[BackendProcessManager] {error}");
                BackendStartFailed?.Invoke(this, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to start backend: {ex.Message}";
            Debug.WriteLine($"[BackendProcessManager] {error}");
            ErrorLogger.LogError($"Failed to start backend: {ex.Message}", "BackendProcessManager.StartBackendProcessAsync");
            BackendStartFailed?.Invoke(this, error);
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
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (await IsBackendHealthyAsync(cancellationToken))
            {
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
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
    /// Finds the VoiceStudio repository root.
    /// </summary>
    private static string? FindRepoRoot()
    {
        // Start from executable location
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = AppContext.BaseDirectory;
        }

        var dir = Path.GetDirectoryName(exePath);

        // Walk up looking for VoiceStudio.sln or .git
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "VoiceStudio.sln")) ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null)
            {
                break;
            }

            dir = parent.FullName;
        }

        // Fallback: try known development path
        if (Directory.Exists(@"E:\VoiceStudio"))
        {
            return @"E:\VoiceStudio";
        }

        return null;
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
