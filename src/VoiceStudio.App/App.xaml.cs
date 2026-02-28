using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App
{
  public partial class App : Application
  {
    private static PerformanceProfiler? _startupProfiler;
    private static DateTime _appStartTime;
    public static Window? MainWindowInstance { get; private set; }
    private static readonly object _bindingFailureLock = new();
    private static readonly List<string> _bindingFailures = [];
    private static bool _bindingFailureLoggingEnabled;
    private static string? _bindingFailureLogPath;
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public App()
    {
      this.UnhandledException += App_UnhandledException;
      _appStartTime = DateTime.UtcNow;
      _startupProfiler = PerformanceProfiler.Start("Application Startup");
      _startupProfiler.Checkpoint("App Constructor Start");

      this.InitializeComponent();
      _startupProfiler.Checkpoint("InitializeComponent");

      // Initialize service provider
      ServiceProvider.Initialize();
      _startupProfiler.Checkpoint("ServiceProvider.Initialize");

      // Command handlers are bootstrapped in OnLaunched after MainWindow is created
      // (DialogService requires Window which is only available after window creation)

      // Start backend process in background (non-blocking)
      _ = Task.Run(async () =>
      {
        try
        {
          var backendManager = ServiceProvider.TryGetBackendProcessManager();
          if (backendManager != null)
          {
            var started = await backendManager.EnsureBackendRunningAsync();
            Debug.WriteLine($"[App] Backend auto-start: {(started ? "SUCCESS" : "FAILED")}");
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[App] Backend auto-start error: {ex.Message}");
          ErrorLogger.LogWarning($"Backend auto-start failed: {ex.Message}", "App.Constructor");
        }
      });

      // Gate C UI smoke relies on capturing binding failures deterministically.
      if (IsUiSmokeRequested())
      {
        EnableBindingFailureLogging();
      }

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "app_ctor", args: null, smokeExit: null, uiSmoke: null);
      }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
      try
      {
        // Write to deterministic location: %LOCALAPPDATA%\VoiceStudio\crashes\
        var crashDir = System.IO.Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "VoiceStudio", "crashes");

        System.IO.Directory.CreateDirectory(crashDir);

        // Use timestamp + sequence to preserve crash history
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var logPath = System.IO.Path.Combine(crashDir, $"crash_{timestamp}.log");

        // Construct detailed crash log
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════")
          .AppendLine("VoiceStudio Unhandled Exception Report")
          .AppendLine("═══════════════════════════════════════════════════")
          .AppendLine()
          .AppendLine($"Timestamp (UTC): {timestamp}")
          .AppendLine($"Process ID: {Environment.ProcessId}")
          .AppendLine($"Thread ID: {Environment.CurrentManagedThreadId}")
          .AppendLine()
          .AppendLine("--- Startup Stage ---")
          .AppendLine($"App Startup Time: {_appStartTime:yyyy-MM-dd_HH:mm:ss.fff}")
          .AppendLine($"Uptime at crash: {(DateTime.UtcNow - _appStartTime).TotalSeconds:F3}s");

        // Startup stage indicator

        if (_startupProfiler != null)
        {
          sb.AppendLine("Startup Profiler: Active (within startup phase)");
        }
        sb.AppendLine();

        // Environment
        sb.AppendLine("--- Environment ---");
        sb.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($".NET Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Working Dir: {Environment.CurrentDirectory}");
        sb.AppendLine();

        // Exception details
        sb.AppendLine("--- Exception Details ---");
        sb.AppendLine($"Exception Type: {e.Exception?.GetType().FullName}");
        sb.AppendLine($"Message: {e.Message}");
        sb.AppendLine($"HResult: 0x{e.Exception?.HResult:X8}");
        sb.AppendLine();

        // Stack trace
        sb.AppendLine("--- Stack Trace ---");
        sb.AppendLine(e.Exception?.StackTrace ?? "(no stack trace)");
        sb.AppendLine();

        // Inner exception (if any)
        if (e.Exception?.InnerException != null)
        {
          sb.AppendLine("--- Inner Exception ---");
          sb.AppendLine($"Type: {e.Exception.InnerException.GetType().FullName}");
          sb.AppendLine($"Message: {e.Exception.InnerException.Message}");
          sb.AppendLine($"Stack Trace: {e.Exception.InnerException.StackTrace}");
          sb.AppendLine();
        }

        sb.AppendLine("═══════════════════════════════════════════════════");

        // Write to file
        System.IO.File.WriteAllText(logPath, sb.ToString());

        // Also write symbolic link to "latest crash" for easy access
        var latestLink = System.IO.Path.Combine(crashDir, "latest.log");
        try
        {
          if (System.IO.File.Exists(latestLink))
          {
            System.IO.File.Delete(latestLink);
          }
          System.IO.File.WriteAllText(latestLink, $"See: {logPath}");
        }
        catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.Unknown"); }

        // Debug output
        Debug.WriteLine($"Unhandled exception logged to: {logPath}");
      }
      catch (Exception logEx)
      {
        // Fallback to debug output if file writing fails
        Debug.WriteLine($"Failed to write crash log: {logEx.Message}");
      }

      // Mark as handled to prevent app termination for non-fatal exceptions
      // This allows the UI to continue operating even when individual operations fail
      e.Handled = true;
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
      _startupProfiler?.Checkpoint("OnLaunched Start");

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "onlaunched_enter", args: args, smokeExit: null, uiSmoke: null);
      }

      var smokeExit = IsSmokeExit(args);
      var uiSmoke = IsUiSmoke(args);
      var isSmokeMode = smokeExit || uiSmoke;

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "onlaunched_flags", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
      }

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "before_mainwindow_create", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
      }

      if (uiSmoke)
      {
        EnableBindingFailureLogging();
        ClearBindingFailures();
      }

      // Load plugins in background (non-blocking)
      if (!isSmokeMode)
      {
        _ = Task.Run(async () =>
        {
          try
          {
            var pluginManager = ServiceProvider.GetPluginManager();
            await pluginManager.LoadPluginsAsync();
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.OnLaunched");
      }
        });
      }

      if (uiSmoke)
      {
        var crashDir = GetCrashDir();
        Directory.CreateDirectory(crashDir);

        try
        {
          // Clear stale artifacts from prior runs so a PASS doesn't leave confusing leftovers.
          // (The Gate C script only copies artifacts updated during the current run, but the crash dir can
          // still contain old ui_smoke_exception.log from a previous failure.)
          try
          {
            var staleException = Path.Combine(crashDir, "ui_smoke_exception.log");
            if (File.Exists(staleException)) File.Delete(staleException);

            var staleSummary = Path.Combine(crashDir, "ui_smoke_summary.json");
            if (File.Exists(staleSummary)) File.Delete(staleSummary);

            var staleSteps = Path.Combine(crashDir, "ui_smoke_steps_latest.log");
            if (File.Exists(staleSteps)) File.Delete(staleSteps);
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.Unknown");
      }

          m_window = new MainWindow();
          MainWindowInstance = m_window;
          _startupProfiler?.Checkpoint("MainWindow Created");

          // Bootstrap command handlers now that MainWindow is available (DialogService requires Window)
          try
          {
            CommandHandlerBootstrapper.Initialize();
            _startupProfiler?.Checkpoint("CommandHandlerBootstrapper.Initialize");
          }
          catch (Exception ex)
          {
            Debug.WriteLine($"[App] Command handler initialization failed: {ex.Message}");
          }

          if (IsSmokeHinted())
          {
            WriteUiSmokeDebugSnapshot(phase: "mainwindow_created", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
          }

          m_window.Activate();
          _startupProfiler?.Checkpoint("MainWindow Activated");

          if (IsSmokeHinted())
          {
            WriteUiSmokeDebugSnapshot(phase: "mainwindow_activated", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
          }

          // Run smoke on a background thread so we can time out + write artifacts even if the UI thread blocks.
          _ = Task.Run(async () =>
          {
            GateCUiSmokeResult result;
            try
            {
              result = await RunGateCUiSmokeAsync(m_window, crashDir).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
              try
              {
                Directory.CreateDirectory(crashDir);
                File.WriteAllText(Path.Combine(crashDir, "ui_smoke_exception.log"), ex.ToString());
              }
              catch (Exception logEx)
              {
                ErrorLogger.LogWarning($"Best effort operation failed: {logEx.Message}", "App.UiSmoke");
              }

              result = new GateCUiSmokeResult
              {
                ExitCode = 3,
                ExePath = Environment.ProcessPath ?? string.Empty,
                BindingLogPath = _bindingFailureLogPath ?? Path.Combine(crashDir, "binding_failures_latest.log"),
                NavSteps = [],
                BindingFailures = [],
              };
            }

            WriteGateCUiSmokeSummary(crashDir, result);
            Environment.Exit(result.ExitCode);
          });
          return;
        }
        catch (Exception ex)
        {
          try
          {
            Directory.CreateDirectory(crashDir);
            File.WriteAllText(Path.Combine(crashDir, "ui_smoke_exception.log"), ex.ToString());
          }
          catch (Exception logEx)
          {
            ErrorLogger.LogWarning($"Best effort operation failed: {logEx.Message}", "App.UiSmoke");
          }

          // Ensure the automation always gets a summary file, even if MainWindow cannot be created.
          var result = new GateCUiSmokeResult
          {
            ExitCode = 4,
            ExePath = Environment.ProcessPath ?? string.Empty,
            BindingLogPath = _bindingFailureLogPath ?? Path.Combine(crashDir, "binding_failures_latest.log"),
            NavSteps = [],
            BindingFailures = [],
          };

          WriteGateCUiSmokeSummary(crashDir, result);
          Environment.Exit(result.ExitCode);
          return;
        }
      }

      m_window = new MainWindow();
      MainWindowInstance = m_window;
      _startupProfiler?.Checkpoint("MainWindow Created");

      // Bootstrap command handlers now that MainWindow is available (DialogService requires Window)
      try
      {
        CommandHandlerBootstrapper.Initialize();
        _startupProfiler?.Checkpoint("CommandHandlerBootstrapper.Initialize");
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[App] Command handler initialization failed: {ex.Message}");
        // Non-fatal - app can continue without command handlers
      }

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "mainwindow_created", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
      }

      m_window.Activate();
      _startupProfiler?.Checkpoint("MainWindow Activated");

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "mainwindow_activated", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
      }

      // Start deferred initialization in background after window is visible
      // This improves perceived startup time by delaying non-critical services
      if (!isSmokeMode)
      {
        _ = Task.Run(async () =>
        {
          try
          {
            // Small delay to let the window fully render
            await Task.Delay(500);

            var initializer = DeferredServiceInitializer.CreateDefault(new ServiceProviderAdapter());
            await initializer.InitializeAllAsync();
            Debug.WriteLine("[App] Deferred service initialization completed");
          }
          catch (Exception ex)
          {
            Debug.WriteLine($"[App] Deferred initialization error: {ex.Message}");
            ErrorLogger.LogWarning($"Deferred initialization failed: {ex.Message}", "App.DeferredInit");
          }
        });
      }

      if (smokeExit)
      {
        _startupProfiler?.Checkpoint("SmokeExit Requested");

        // Give WinUI a moment to finish initial render and resource resolution.
        await Task.Delay(250);

        try
        {
          m_window.Close();
        }
        catch
        {
          try
          {
            Microsoft.UI.Xaml.Application.Current.Exit();
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.Unknown");
      }
        }
      }

      // Log startup performance
      if (_startupProfiler != null)
      {
        var totalTime = _startupProfiler.ElapsedMilliseconds;
        Debug.WriteLine(_startupProfiler.GetReport());

        // Target: < 3 seconds
        if (totalTime > 3000)
        {
          Debug.WriteLine($"⚠️ WARNING: Startup time ({totalTime}ms) exceeds target (3000ms)");
        }
        else
        {
          Debug.WriteLine($"✅ Startup time: {totalTime}ms (target: <3000ms)");
        }

        _startupProfiler.Dispose();
        _startupProfiler = null;
      }
    }

    private static bool IsSmokeExit(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
      try
      {
        var arguments = args?.Arguments ?? string.Empty;
        if (arguments.Contains("--smoke-exit", StringComparison.OrdinalIgnoreCase)
            || HasCommandLineFlag("--smoke-exit"))
        {
          return true;
        }

        var env = Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_EXIT");
        if (string.IsNullOrWhiteSpace(env))
        {
          return false;
        }

        return env.Equals("1", StringComparison.OrdinalIgnoreCase)
            || env.Equals("true", StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    private static bool IsUiSmoke(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
      try
      {
        var arguments = args?.Arguments ?? string.Empty;
        if (arguments.Contains("--smoke-ui", StringComparison.OrdinalIgnoreCase)
            || arguments.Contains("--ui-smoke", StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }

        return IsUiSmokeRequested();
      }
      catch
      {
        return IsUiSmokeRequested();
      }
    }

    private static bool IsUiSmokeRequested()
    {
      return HasCommandLineFlag("--smoke-ui")
          || HasCommandLineFlag("--ui-smoke")
          || IsUiSmokeRequestedFromEnv();
    }

    private static bool HasCommandLineFlag(string flag)
    {
      try
      {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
          if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
          {
            return true;
          }
        }

        var raw = Environment.CommandLine ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(raw)
            && raw.Contains(flag, StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.HasCommandLineFlag");
      }

      return false;
    }

    private static bool IsSmokeHinted()
    {
      try
      {
        if (IsUiSmokeRequestedFromEnv())
        {
          return true;
        }

        var exitEnv = Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_EXIT") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(exitEnv)
            && (exitEnv.Equals("1", StringComparison.OrdinalIgnoreCase)
                || exitEnv.Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
          return true;
        }

        var raw = Environment.CommandLine ?? string.Empty;
        return raw.Contains("--smoke", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("--ui-smoke", StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    private static bool IsUiSmokeRequestedFromEnv()
    {
      try
      {
        var env = Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_UI");
        if (string.IsNullOrWhiteSpace(env))
        {
          return false;
        }

        return env.Equals("1", StringComparison.OrdinalIgnoreCase)
            || env.Equals("true", StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    private static void WriteUiSmokeDebugSnapshot(
      string phase,
      Microsoft.UI.Xaml.LaunchActivatedEventArgs? args,
      bool? smokeExit,
      bool? uiSmoke)
    {
      try
      {
        var crashDir = GetCrashDir();
        Directory.CreateDirectory(crashDir);

        var path = Path.Combine(crashDir, "ui_smoke_debug_latest.json");
        var payload = new
        {
          timestamp_utc = DateTime.UtcNow.ToString("o"),
          phase,
          env_smoke_ui = Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_UI"),
          env_smoke_exit = Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_EXIT"),
          raw_command_line = Environment.CommandLine,
          command_line_args = Environment.GetCommandLineArgs(),
          launch_args = args?.Arguments,
          computed_smoke_exit = smokeExit,
          computed_ui_smoke = uiSmoke,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);

        File.WriteAllText(path, json, Encoding.UTF8);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.WriteUiSmokeDebugSnapshot");
      }
    }

    private static string GetCrashDir()
    {
      return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceStudio",
        "crashes");
    }

    private void EnableBindingFailureLogging()
    {
      if (_bindingFailureLoggingEnabled)
      {
        return;
      }

      _bindingFailureLoggingEnabled = true;
      _bindingFailureLogPath = Path.Combine(GetCrashDir(), "binding_failures_latest.log");

      try
      {
        // Enable binding tracing so failures surface deterministically (Gate C proof).
        this.DebugSettings.IsBindingTracingEnabled = true;
        this.DebugSettings.BindingFailed += OnBindingFailed;
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.EnableBindingFailureLogging");
      }
    }

    private static void ClearBindingFailures()
    {
      lock (_bindingFailureLock)
      {
        _bindingFailures.Clear();
      }

      try
      {
        var path = _bindingFailureLogPath ?? Path.Combine(GetCrashDir(), "binding_failures_latest.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GetCrashDir());
        File.WriteAllText(path, string.Empty);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.ClearBindingFailures");
      }
    }

    private void OnBindingFailed(object sender, BindingFailedEventArgs e)
    {
      try
      {
        var message = e?.Message ?? "(binding failed: no message)";

        lock (_bindingFailureLock)
        {
          _bindingFailures.Add(message);
        }

        var path = _bindingFailureLogPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
          Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GetCrashDir());
          File.AppendAllText(path, message + Environment.NewLine);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.OnBindingFailed");
      }
    }

    private sealed record GateCUiSmokeResult
    {
      public int ExitCode { get; init; }
      public string ExePath { get; init; } = string.Empty;
      public string[] NavSteps { get; init; } = [];
      public string BindingLogPath { get; init; } = string.Empty;
      public string[] BindingFailures { get; init; } = [];
    }

    private static async Task<GateCUiSmokeResult> RunGateCUiSmokeAsync(Window window, string crashDir)
    {
      var result = new GateCUiSmokeResult
      {
        ExePath = Environment.ProcessPath ?? string.Empty,
        BindingLogPath = _bindingFailureLogPath ?? Path.Combine(crashDir, "binding_failures_latest.log"),
      };

      try
      {
        // Allow initial layout/render.
        await Task.Delay(350).ConfigureAwait(false);

        if (window is not MainWindow mainWindow)
        {
          return result with { ExitCode = 2 };
        }

        var (steps, timedOut, timedOutStep) = await mainWindow.RunGateCUiSmokeNavigationAsync(crashDir).ConfigureAwait(false);

        if (timedOut)
        {
          try
          {
            Directory.CreateDirectory(crashDir);
            File.WriteAllText(
              Path.Combine(crashDir, "ui_smoke_exception.log"),
              $"UI smoke timed out after a panel switch. Step: {timedOutStep ?? "(unknown)"}{Environment.NewLine}See: ui_smoke_steps_latest.log");
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.Task");
      }
        }

        // Allow any async binding/visual tree work to flush.
        await Task.Delay(250).ConfigureAwait(false);

        string[] failures;
        lock (_bindingFailureLock)
        {
          failures = _bindingFailures.ToArray();
        }

        int exitCode;
        if (timedOut)
        {
          exitCode = 5;
        }
        else if (failures.Length == 0)
        {
          exitCode = 0;
        }
        else
        {
          exitCode = 1;
        }

        return new GateCUiSmokeResult
        {
          ExitCode = exitCode,
          ExePath = result.ExePath,
          NavSteps = steps,
          BindingLogPath = result.BindingLogPath,
          BindingFailures = failures,
        };
      }
      catch (Exception ex)
      {
        try
        {
          Directory.CreateDirectory(crashDir);
          File.WriteAllText(Path.Combine(crashDir, "ui_smoke_exception.log"), ex.ToString());
        }
        catch (Exception logEx)
        {
          ErrorLogger.LogWarning($"Best effort operation failed: {logEx.Message}", "App.UiSmokeResult");
        }

        return result with { ExitCode = 3 };
      }
    }

    private static void WriteGateCUiSmokeSummary(string crashDir, GateCUiSmokeResult result)
    {
      try
      {
        Directory.CreateDirectory(crashDir);
        var summaryPath = Path.Combine(crashDir, "ui_smoke_summary.json");

        var payload = new
        {
          timestamp_utc = DateTime.UtcNow.ToString("o"),
          exe = result.ExePath,
          exit_code = result.ExitCode,
          nav_steps = result.NavSteps,
          binding_log = result.BindingLogPath,
          binding_failure_count = result.BindingFailures.Length,
          binding_failures = result.BindingFailures,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);

        File.WriteAllText(summaryPath, json, Encoding.UTF8);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.WriteGateCUiSmokeSummary");
      }
    }

    // WinUI 3 doesn't have OnSuspending - cleanup happens on app exit
    // ServiceProvider cleanup should be handled elsewhere if needed

    private Window? m_window;
  }

  /// <summary>
  /// Adapter to expose the static ServiceProvider as an IServiceProvider.
  /// Used by DeferredServiceInitializer to resolve services.
  /// </summary>
  internal class ServiceProviderAdapter : IServiceProvider
  {
    public object? GetService(Type serviceType)
    {
      // Map service types to static ServiceProvider methods
      if (serviceType == typeof(PluginManager))
        return ServiceProvider.GetPluginManager();

      if (serviceType == typeof(RecentProjectsService))
        return ServiceProvider.TryGetRecentProjectsService();

      if (serviceType == typeof(CrashRecoveryService))
        return null; // CrashRecoveryService not exposed via ServiceProvider; init handled elsewhere

      if (serviceType == typeof(VoiceStudio.Core.Services.IBackendClient))
        return ServiceProvider.GetBackendClient();

      // Default: return null (service not available)
      return null;
    }
  }
}
