using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Core.ErrorHandling;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Views;

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
      ColdStartTimingCollector.SetAppStartUtc(_appStartTime);
      _startupProfiler = PerformanceProfiler.Start("Application Startup");
      _startupProfiler.Checkpoint("App Constructor Start");

      this.InitializeComponent();
      _startupProfiler.Checkpoint("InitializeComponent");

      // Initialize service provider
      ServiceProvider.Initialize();
      _startupProfiler.Checkpoint("ServiceProvider.Initialize");

      // Command handlers are bootstrapped in OnLaunched after MainWindow is created
      // (DialogService requires Window which is only available after window creation)
      // Backend startup is an explicit phase in OnLaunched (see STARTUP_ORCHESTRATION_HARDENING_PLAN.md)

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

      var smokeExit = IsSmokeExit(args);
      var uiSmoke = IsUiSmoke(args);
      var isSmokeMode = smokeExit || uiSmoke;

      if (!isSmokeMode && !IsIconLaunchSmokeRequested())
      {
        try
        {
          JumpListActivation.SetPendingIfParsed(args.Arguments, Environment.GetCommandLineArgs());
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[JumpList] Activation parse failed: {ex.Message}");
        }

        if (!JumpListActivation.HasPending())
        {
          try
          {
            FileActivation.SetPendingIfParsed(args.Arguments, Environment.GetCommandLineArgs());
          }
          catch (Exception ex)
          {
            Debug.WriteLine($"[FileActivation] Parse failed: {ex.Message}");
          }
        }
      }

      // For UI smoke, ensure backend is ready before MainWindow/smoke (backend-dependent actions)
      if (uiSmoke)
      {
        await EnsureBackendWithTrackingAsync();
        _startupProfiler?.Checkpoint("Backend Ready (UI Smoke)");
      }

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "onlaunched_enter", args: args, smokeExit: null, uiSmoke: null);
      }

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

      // Plugin loading is deferred to DeferredServiceInitializer (runs ~500ms after MainWindow)
      // to avoid duplicate/race with PluginDiscovery and improve startup time.

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

          // Eager init: ensure IAudioPlayerService exists so PlaybackRequestedEvent subscription is active before any panel publishes
          _ = AppServices.GetAudioPlayerService();
          Debug.WriteLine("[App] IAudioPlayerService eagerly resolved for playback subscription");

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
                SynthesisStepRan = false,
                PlaybackInvoked = false,
                Failures = [],
              };
            }

            var exitCode = WriteGateCUiSmokeSummary(crashDir, result);
            Environment.Exit(exitCode);
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
            SynthesisStepRan = false,
            PlaybackInvoked = false,
            Failures = [],
          };

          var exitCode = WriteGateCUiSmokeSummary(crashDir, result);
          Environment.Exit(exitCode);
          return;
        }
      }

      // GAP-X02 / GAP-063: Check if first-run wizard should be shown (skip for smoke modes)
      if (!isSmokeMode && !IsIconLaunchSmokeRequested() && !IsSmokeFailurePortRequested() && await FirstRunWizard.ShouldShowWizardAsync())
      {
        _startupProfiler?.Checkpoint("FirstRunWizard Check - Should Show");

        var firstRunCompleteBeforeWizard = UnpackagedSettingsHelper.GetValue<bool>("FirstRunComplete", false);
        var isFirstRun = !firstRunCompleteBeforeWizard;

        // Show wizard as modal before main window
        var wizard = new FirstRunWizard(isFirstRun: isFirstRun);
        wizard.Activate();

        // Wait for wizard completion
        var tcs = new TaskCompletionSource<bool>();
        wizard.Closed += (_, _) => tcs.TrySetResult(wizard.WasCompleted);
        await tcs.Task;

        _startupProfiler?.Checkpoint($"FirstRunWizard Closed (Completed: {wizard.WasCompleted})");

        // GAP-063: Exit only on true first-run cancel (not when re-shown via ShowWizardOnStartup)
        if (!wizard.WasCompleted && isFirstRun)
        {
          ErrorLogger.LogInfo("First-run wizard cancelled by user, exiting application.");
          Application.Current.Exit();
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

      // Eager init: ensure IAudioPlayerService exists so PlaybackRequestedEvent subscription is active before any panel publishes
      _ = AppServices.GetAudioPlayerService();
      Debug.WriteLine("[App] IAudioPlayerService eagerly resolved for playback subscription");

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "mainwindow_created", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
      }

      m_window.Activate();
      _startupProfiler?.Checkpoint("MainWindow Activated");
      ErrorDialogService.ResetStartupDialogDiagnostics();

      if (IsSmokeHinted())
      {
        WriteUiSmokeDebugSnapshot(phase: "mainwindow_activated", args: args, smokeExit: smokeExit, uiSmoke: uiSmoke);
      }

      // Explicit backend startup with tracked state (STARTUP_ORCHESTRATION_HARDENING_PLAN)
      // MainWindow shows startup overlay until BackendReady or BackendFailed
      if (!isSmokeMode)
      {
        if (IsSmokeFailurePortRequested())
        {
          var startupState = ServiceProvider.GetStartupStateService();
          void Handler(object? s, StartupStateChangedEventArgs e)
          {
            if (e.NewState == StartupState.BackendFailed)
            {
              startupState.StateChanged -= Handler;
              var msg = e.FailureMessage ?? "";
              var hasPortMsg = msg.IndexOf("port", StringComparison.OrdinalIgnoreCase) >= 0
                  || msg.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0;
              var payload = new
              {
                status = hasPortMsg ? "PASS" : "FAIL",
                timestamp_utc = DateTime.UtcNow.ToString("o"),
                backend_failed = true,
                failure_message = msg,
                expected_port_message = hasPortMsg,
                startup_dialog = ErrorDialogService.GetStartupDialogDiagnostics(),
              };
              WriteFailureSmokeSummary(GetCrashDir(), payload);
              Environment.Exit(hasPortMsg ? 0 : 1);
            }
            else if (e.NewState == StartupState.BackendReady)
            {
              startupState.StateChanged -= Handler;
              var payload = new
              {
                status = "FAIL",
                timestamp_utc = DateTime.UtcNow.ToString("o"),
                backend_failed = false,
                failure_message = (string?)null,
                expected_port_message = false,
                error = "Expected BackendFailed (port occupied) but got BackendReady",
                startup_dialog = ErrorDialogService.GetStartupDialogDiagnostics(),
              };
              WriteFailureSmokeSummary(GetCrashDir(), payload);
              Environment.Exit(1);
            }
          }
          startupState.StateChanged += Handler;
          _ = Task.Run(async () =>
          {
            await Task.Delay(30_000).ConfigureAwait(false);
            startupState.StateChanged -= Handler;
            var payload = new
            {
              status = "FAIL",
              timestamp_utc = DateTime.UtcNow.ToString("o"),
              backend_failed = false,
              failure_message = (string?)null,
              expected_port_message = false,
              error = "Timeout: did not get BackendFailed within 30s",
              startup_dialog = ErrorDialogService.GetStartupDialogDiagnostics(),
            };
            WriteFailureSmokeSummary(GetCrashDir(), payload);
            Environment.Exit(1);
          });
        }
        else if (IsSmokeFailureRuntimeRequested())
        {
          var tempDir = Path.Combine(Path.GetTempPath(), "VoiceStudio_RuntimeSmoke_" + Guid.NewGuid().ToString("N")[..8]);
          Directory.CreateDirectory(tempDir);
          Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", tempDir);
          var startupState = ServiceProvider.GetStartupStateService();
          void Handler(object? s, StartupStateChangedEventArgs e)
          {
            if (e.NewState == StartupState.BackendFailed)
            {
              startupState.StateChanged -= Handler;
              var msg = e.FailureMessage ?? "";
              var hasRuntimeMsg = msg.IndexOf("app root", StringComparison.OrdinalIgnoreCase) >= 0
                  || msg.IndexOf("VOICESTUDIO_APP_ROOT", StringComparison.OrdinalIgnoreCase) >= 0
                  || msg.IndexOf("Python", StringComparison.OrdinalIgnoreCase) >= 0
                  || msg.IndexOf("runtime", StringComparison.OrdinalIgnoreCase) >= 0;
              var payload = new
              {
                status = hasRuntimeMsg ? "PASS" : "FAIL",
                timestamp_utc = DateTime.UtcNow.ToString("o"),
                backend_failed = true,
                failure_message = msg,
                expected_runtime_message = hasRuntimeMsg,
                startup_dialog = ErrorDialogService.GetStartupDialogDiagnostics(),
              };
              WriteFailureRuntimeSmokeSummary(GetCrashDir(), payload);
              _ = ErrorBoundary.TryExecute(() => Directory.Delete(tempDir, recursive: false), "cleanup temp dir before exit");
              Environment.Exit(hasRuntimeMsg ? 0 : 1);
            }
            else if (e.NewState == StartupState.BackendReady)
            {
              startupState.StateChanged -= Handler;
              var payload = new
              {
                status = "FAIL",
                timestamp_utc = DateTime.UtcNow.ToString("o"),
                backend_failed = false,
                failure_message = (string?)null,
                expected_runtime_message = false,
                error = "Expected BackendFailed (runtime missing) but got BackendReady",
                startup_dialog = ErrorDialogService.GetStartupDialogDiagnostics(),
              };
              WriteFailureRuntimeSmokeSummary(GetCrashDir(), payload);
              _ = ErrorBoundary.TryExecute(() => Directory.Delete(tempDir, recursive: false), "cleanup temp dir before exit");
              Environment.Exit(1);
            }
          }
          startupState.StateChanged += Handler;
          _ = Task.Run(async () =>
          {
            await Task.Delay(30_000).ConfigureAwait(false);
            startupState.StateChanged -= Handler;
            var payload = new
            {
              status = "FAIL",
              timestamp_utc = DateTime.UtcNow.ToString("o"),
              backend_failed = false,
              failure_message = (string?)null,
              expected_runtime_message = false,
              error = "Timeout: did not get BackendFailed within 30s",
              startup_dialog = ErrorDialogService.GetStartupDialogDiagnostics(),
            };
            WriteFailureRuntimeSmokeSummary(GetCrashDir(), payload);
            _ = ErrorBoundary.TryExecute(() => Directory.Delete(tempDir, recursive: false), "cleanup temp dir before exit");
            Environment.Exit(1);
          });
        }
        StartBackendWithTracking();
      }

      // Icon-launch smoke: wait for overlay to clear, run one backend action, write summary, exit
      if (IsIconLaunchSmokeRequested())
      {
        _ = Task.Run(async () =>
        {
          try
          {
            var exitCode = await RunIconLaunchSmokeAsync();
            Environment.Exit(exitCode);
          }
          catch (Exception ex)
          {
            _ = ErrorBoundary.TryExecute(() =>
            {
              var crashDir = GetCrashDir();
              Directory.CreateDirectory(crashDir);
              var summaryPath = Path.Combine(crashDir, "icon_launch_smoke_summary.json");
              var payload = new
              {
                status = "FAIL",
                timestamp_utc = DateTime.UtcNow.ToString("o"),
                backend_ready = false,
                overlay_cleared_ms = (double?)null,
                action_succeeded = false,
                action_name = "profiles",
                failures = new[] { new { step = "exception", error = ex.ToString() } },
              };
              File.WriteAllText(summaryPath, System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions));
            }, "write icon launch smoke failure summary");
            Environment.Exit(1);
          }
        });
      }

      // Start deferred initialization in background after window is visible
      // This improves perceived startup time by delaying non-critical services
      if (!isSmokeMode)
      {
        _ = Task.Run(async () =>
        {
          ColdStartTimingCollector.RecordDeferredInitStart();
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
          finally
          {
            ColdStartTimingCollector.RecordDeferredInitEnd();
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
        ColdStartTimingCollector.CaptureApplicationStartupCheckpoints(_startupProfiler, _appStartTime);
        var totalTime = _startupProfiler.ElapsedMilliseconds;
        AppServices.AppStartupMs = totalTime;
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

    private static bool IsIconLaunchSmokeRequested()
    {
      try
      {
        var env = Environment.GetEnvironmentVariable("VOICE_STUDIO_ICON_LAUNCH_SMOKE");
        return !string.IsNullOrWhiteSpace(env) && (env.Equals("1", StringComparison.OrdinalIgnoreCase) || env.Equals("true", StringComparison.OrdinalIgnoreCase));
      }
      catch
      {
        return false;
      }
    }

    private static bool IsSmokeFailurePortRequested()
    {
      try
      {
        var env = Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_FAILURE_PORT");
        return !string.IsNullOrWhiteSpace(env) && (env.Equals("1", StringComparison.OrdinalIgnoreCase) || env.Equals("true", StringComparison.OrdinalIgnoreCase));
      }
      catch
      {
        return false;
      }
    }

    private static bool IsSmokeFailureRuntimeRequested()
    {
      try
      {
        var env = Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_FAILURE_RUNTIME");
        return !string.IsNullOrWhiteSpace(env) && (env.Equals("1", StringComparison.OrdinalIgnoreCase) || env.Equals("true", StringComparison.OrdinalIgnoreCase));
      }
      catch
      {
        return false;
      }
    }

    private static void WriteFailureSmokeSummary(string crashDir, object payload)
    {
      try
      {
        Directory.CreateDirectory(crashDir);
        var summaryPath = Path.Combine(crashDir, "failure_smoke_summary.json");
        var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);
        File.WriteAllText(summaryPath, json);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to write failure smoke summary: {ex.Message}", "App.FailureSmoke");
      }
    }

    private static void WriteFailureRuntimeSmokeSummary(string crashDir, object payload)
    {
      try
      {
        Directory.CreateDirectory(crashDir);
        var summaryPath = Path.Combine(crashDir, "failure_runtime_smoke_summary.json");
        var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);
        File.WriteAllText(summaryPath, json);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to write runtime failure smoke summary: {ex.Message}", "App.FailureRuntimeSmoke");
      }
    }

    /// <summary>
    /// Icon-launch smoke: poll for backend ready, run one backend action, write summary.
    /// Returns exit code (0 = PASS, 1 = FAIL).
    /// </summary>
    private static async Task<int> RunIconLaunchSmokeAsync()
    {
      var startupState = ServiceProvider.GetStartupStateService();
      var crashDir = GetCrashDir();
      Directory.CreateDirectory(crashDir);
      var sw = System.Diagnostics.Stopwatch.StartNew();

      // Poll for IsReady, up to 60s
      const int timeoutMs = 60_000;
      const int pollIntervalMs = 500;
      while (sw.ElapsedMilliseconds < timeoutMs)
      {
        if (startupState.IsReady)
          break;
        if (startupState.CurrentState == StartupState.BackendFailed)
        {
          var payload = new
          {
            status = "FAIL",
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            backend_ready = false,
            overlay_cleared_ms = (double?)null,
            action_succeeded = false,
            action_name = "profiles",
            failures = new[] { new { step = "backend_failed", error = startupState.FailureMessage ?? "Backend failed to start" } },
          };
          WriteIconLaunchSmokeSummary(crashDir, payload);
          return 1;
        }
        await Task.Delay(pollIntervalMs).ConfigureAwait(false);
      }

      if (!startupState.IsReady)
      {
        var payload = new
        {
          status = "FAIL",
          timestamp_utc = DateTime.UtcNow.ToString("o"),
          backend_ready = false,
          overlay_cleared_ms = sw.Elapsed.TotalMilliseconds,
          action_succeeded = false,
          action_name = "profiles",
          failures = new[] { new { step = "timeout", error = "Backend did not become ready within 60s" } },
        };
        WriteIconLaunchSmokeSummary(crashDir, payload);
        return 1;
      }

      var overlayClearedMs = sw.Elapsed.TotalMilliseconds;

      // Run backend-dependent actions: profiles + library folders (Round 4 Task 3)
      bool action1Succeeded = false;
      string? action1Error = null;
      try
      {
        var backend = ServiceProvider.GetBackendClient();
        var profiles = await backend.GetProfilesAsync().ConfigureAwait(false);
        action1Succeeded = profiles != null;
      }
      catch (Exception ex)
      {
        action1Error = ex.ToString();
      }

      bool action2Succeeded = false;
      string? action2Error = null;
      try
      {
        var libraryClient = AppServices.GetRequiredService<ILibraryClient>();
        var folders = await libraryClient.GetLibraryFoldersAsync(parentId: null).ConfigureAwait(false);
        action2Succeeded = folders != null;
      }
      catch (Exception ex)
      {
        action2Error = ex.ToString();
      }

      // Round 6 Task 6: Shell-level action — prove one gated command executes post-ready
      bool action3Succeeded = false;
      string? action3Error = null;
      try
      {
        var router = AppServices.TryGetCommandRouter();
        if (router != null)
        {
          action3Succeeded = await router.ExecuteSafeAsync("nav.library").ConfigureAwait(false);
        }
        else
        {
          action3Error = "CommandRouter not available";
        }
      }
      catch (Exception ex)
      {
        action3Error = ex.ToString();
      }

      var allSucceeded = action1Succeeded && action2Succeeded && action3Succeeded;
      var startupDialog = ErrorDialogService.GetStartupDialogDiagnostics();
      var startupDialogClean = startupDialog.StartupPendingDialogShown == 0;
      var failures = new List<object>();
      if (!action1Succeeded) failures.Add(new { step = "profiles_fetch", error = action1Error ?? "Unknown" });
      if (!action2Succeeded) failures.Add(new { step = "library_folders", error = action2Error ?? "Unknown" });
      if (!action3Succeeded) failures.Add(new { step = "nav_library", error = action3Error ?? "Unknown" });
      if (!startupDialogClean)
      {
        failures.Add(new { step = "startup_modal_dialog_race", error = $"Dialogs shown during startup authority window: {startupDialog.StartupPendingDialogShown}" });
      }

      var resultPayload = new
      {
        status = (allSucceeded && startupDialogClean) ? "PASS" : "FAIL",
        timestamp_utc = DateTime.UtcNow.ToString("o"),
        backend_ready = true,
        overlay_cleared_ms = overlayClearedMs,
        action_succeeded = action1Succeeded,
        action_name = "profiles",
        action_2_succeeded = action2Succeeded,
        action_2_name = "library_folders",
        action_3_succeeded = action3Succeeded,
        action_3_name = "nav_library",
        failures = failures,
        startup_dialog = startupDialog,
      };
      WriteIconLaunchSmokeSummary(crashDir, resultPayload);
      return (allSucceeded && startupDialogClean) ? 0 : 1;
    }

    private static void WriteIconLaunchSmokeSummary(string crashDir, object payload)
    {
      try
      {
        Directory.CreateDirectory(crashDir);
        var summaryPath = Path.Combine(crashDir, "icon_launch_smoke_summary.json");
        var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);
        File.WriteAllText(summaryPath, json);

        var outPath = Environment.GetEnvironmentVariable("VOICE_STUDIO_ICON_LAUNCH_SMOKE_OUT");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
          Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? crashDir);
          File.WriteAllText(outPath, json);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to write icon launch smoke summary: {ex.Message}", "App.IconLaunchSmoke");
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

    /// <summary>
    /// Ensures backend is running with explicit startup state tracking.
    /// Awaited for UI smoke so backend is ready before synthesis.
    /// </summary>
    private static async Task EnsureBackendWithTrackingAsync()
    {
      var startupState = ServiceProvider.GetStartupStateService();
      var backendManager = ServiceProvider.TryGetBackendProcessManager();
      if (backendManager == null)
      {
        startupState.SetBackendFailed("Backend manager not available");
        return;
      }

      startupState.SetBackendStarting();
      backendManager.BackendStarted += OnBackendStarted;
      backendManager.BackendStartFailed += OnBackendStartFailed;
      try
      {
        var started = await backendManager.EnsureBackendRunningAsync();
        if (!started && startupState.CurrentState == StartupState.BackendStarting)
        {
          startupState.SetBackendFailed("Backend failed to start");
        }
      }
      finally
      {
        backendManager.BackendStarted -= OnBackendStarted;
        backendManager.BackendStartFailed -= OnBackendStartFailed;
      }
    }

    /// <summary>
    /// Starts backend with tracked state (fire-and-forget but state-driven).
    /// MainWindow overlay hides when BackendReady or BackendFailed.
    /// </summary>
    private static void StartBackendWithTracking()
    {
      var startupState = ServiceProvider.GetStartupStateService();
      var backendManager = ServiceProvider.TryGetBackendProcessManager();
      if (backendManager == null)
      {
        startupState.SetBackendFailed("Backend manager not available");
        return;
      }

      startupState.SetBackendStarting();
      backendManager.BackendStarted += OnBackendStarted;
      backendManager.BackendStartFailed += OnBackendStartFailed;
      ColdStartTimingCollector.RecordWallClockMarker("backend_start_ms");
      _ = Task.Run(async () =>
      {
        try
        {
          var started = await backendManager.EnsureBackendRunningAsync();
          if (!started)
          {
            var state = ServiceProvider.GetStartupStateService();
            if (state.CurrentState == StartupState.BackendStarting)
            {
              state.SetBackendFailed("Backend failed to start");
            }
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[App] Backend auto-start error: {ex.Message}");
          ErrorLogger.LogWarning($"Backend auto-start failed: {ex.Message}", "App.StartBackendWithTracking");
          ServiceProvider.GetStartupStateService().SetBackendFailed(ex.Message);
        }
      });
    }

    private static void OnBackendStarted(object? sender, EventArgs e)
    {
      ColdStartTimingCollector.RecordWallClockMarker("backend_ready_ms");
      if (sender is BackendProcessManager mgr)
      {
        mgr.BackendStarted -= OnBackendStarted;
        mgr.BackendStartFailed -= OnBackendStartFailed;
      }
      ServiceProvider.GetStartupStateService().SetBackendReady();
    }

    private static void OnBackendStartFailed(object? sender, BackendStartFailedEventArgs e)
    {
      if (sender is BackendProcessManager mgr)
      {
        mgr.BackendStarted -= OnBackendStarted;
        mgr.BackendStartFailed -= OnBackendStartFailed;
      }
      ServiceProvider.GetStartupStateService().SetBackendFailed(e.Message);
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
      public bool SynthesisStepRan { get; init; }
      public bool PlaybackInvoked { get; init; }
      public string? AudioId { get; init; }
      public bool StreamCheckPassed { get; init; }
      public bool TempFileCreated { get; init; }
      public bool PlaybackStarted { get; init; }
      public double PlaybackPositionAdvancedMs { get; init; }
      public bool LibraryPlaybackTempFileCreated { get; init; }
      public bool LibraryPlaybackStarted { get; init; }
      public double LibraryPlaybackPositionAdvancedMs { get; init; }
      public bool LibraryImportTempFileCreated { get; init; }
      public bool LibraryImportPlaybackStarted { get; init; }
      public double LibraryImportPlaybackPositionAdvancedMs { get; init; }
      public (string Step, string Error)[] Failures { get; init; } = [];
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

        var (steps, timedOut, timedOutStep, synthesisStepRan, playbackInvoked, audioId, streamCheckPassed, tempFileCreated, playbackStarted, playbackPositionAdvancedMs, libraryPlaybackTempFileCreated, libraryPlaybackStarted, libraryPlaybackPositionAdvancedMs, libraryImportTempFileCreated, libraryImportPlaybackStarted, libraryImportPlaybackPositionAdvancedMs, synthesisFailures) = await mainWindow.RunGateCUiSmokeNavigationAsync(crashDir).ConfigureAwait(false);

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
          SynthesisStepRan = synthesisStepRan,
          PlaybackInvoked = playbackInvoked,
          AudioId = audioId,
          StreamCheckPassed = streamCheckPassed,
          TempFileCreated = tempFileCreated,
          PlaybackStarted = playbackStarted,
          PlaybackPositionAdvancedMs = playbackPositionAdvancedMs,
          LibraryPlaybackTempFileCreated = libraryPlaybackTempFileCreated,
          LibraryPlaybackStarted = libraryPlaybackStarted,
          LibraryPlaybackPositionAdvancedMs = libraryPlaybackPositionAdvancedMs,
          LibraryImportTempFileCreated = libraryImportTempFileCreated,
          LibraryImportPlaybackStarted = libraryImportPlaybackStarted,
          LibraryImportPlaybackPositionAdvancedMs = libraryImportPlaybackPositionAdvancedMs,
          Failures = synthesisFailures.ToArray(),
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

    private static int WriteGateCUiSmokeSummary(string crashDir, GateCUiSmokeResult result)
    {
      var effectiveExitCode = result.ExitCode;
      try
      {
        Directory.CreateDirectory(crashDir);
        var summaryPath = Path.Combine(crashDir, "ui_smoke_summary.json");

        bool? backendReachable = null;
        string? gitCommit = null;
        var isUiSelfTest = IsUiSelfTestRequested();

        if (isUiSelfTest)
        {
          var baseUrl = GetBackendBaseUrl();
          backendReachable = BackendClient.TryCheckHealthAsync(baseUrl).GetAwaiter().GetResult();
          gitCommit = Environment.GetEnvironmentVariable("GIT_COMMIT")
              ?? Environment.GetEnvironmentVariable("VOICESTUDIO_GIT_COMMIT")
              ?? "unknown";

          if (RequireBackendForSelfTest() && backendReachable == false)
          {
            effectiveExitCode = 4;
          }
        }

        object payload;
        var status = effectiveExitCode == 0 ? "PASS" : "FAIL";
        if (isUiSelfTest)
        {
          payload = new
          {
            status,
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            exe = result.ExePath,
            exit_code = effectiveExitCode,
            nav_steps_completed = result.NavSteps.Length,
            nav_steps = result.NavSteps,
            synthesis_step_ran = result.SynthesisStepRan,
            playback_invoked = result.PlaybackInvoked,
            audio_id = result.AudioId,
            stream_check_passed = result.StreamCheckPassed,
            temp_file_created = result.TempFileCreated,
            playback_started = result.PlaybackStarted,
            playback_position_advanced_ms = result.PlaybackPositionAdvancedMs,
            library_playback_temp_file_created = result.LibraryPlaybackTempFileCreated,
            library_playback_started = result.LibraryPlaybackStarted,
            library_playback_position_advanced_ms = result.LibraryPlaybackPositionAdvancedMs,
            library_import_temp_file_created = result.LibraryImportTempFileCreated,
            library_import_playback_started = result.LibraryImportPlaybackStarted,
            library_import_playback_position_advanced_ms = result.LibraryImportPlaybackPositionAdvancedMs,
            failures = result.Failures.Select(f => new { step = f.Step, error = f.Error }).ToArray(),
            binding_log = result.BindingLogPath,
            binding_failure_count = result.BindingFailures.Length,
            binding_failures = result.BindingFailures,
            backend_reachable = backendReachable,
            git_commit = gitCommit,
            mode = "ui-self-test",
          };
        }
        else
        {
          payload = new
          {
            status,
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            exe = result.ExePath,
            exit_code = result.ExitCode,
            nav_steps = result.NavSteps,
            binding_log = result.BindingLogPath,
            binding_failure_count = result.BindingFailures.Length,
            binding_failures = result.BindingFailures,
          };
        }

        var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);

        File.WriteAllText(summaryPath, json, Encoding.UTF8);

        var outPath = Environment.GetEnvironmentVariable("VOICE_STUDIO_UI_SELF_TEST_OUT");
        if (isUiSelfTest && !string.IsNullOrWhiteSpace(outPath))
        {
          try
          {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? crashDir);
            File.WriteAllText(outPath, json, Encoding.UTF8);
          }
          catch (Exception ex)
          {
            ErrorLogger.LogWarning($"Failed to copy UI self-test report to {outPath}: {ex.Message}", "detailed.WriteGateCUiSmokeSummary");
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "detailed.WriteGateCUiSmokeSummary");
      }

      return effectiveExitCode;
    }

    private static bool IsUiSelfTestRequested()
    {
      var env = Environment.GetEnvironmentVariable("VOICE_STUDIO_UI_SELF_TEST");
      return !string.IsNullOrWhiteSpace(env) && (env.Equals("1", StringComparison.OrdinalIgnoreCase) || env.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequireBackendForSelfTest()
    {
      var env = Environment.GetEnvironmentVariable("VOICE_STUDIO_UI_SELF_TEST_REQUIRE_BACKEND");
      return !string.IsNullOrWhiteSpace(env) && (env.Equals("1", StringComparison.OrdinalIgnoreCase) || env.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetBackendBaseUrl()
    {
      return BackendClientConfig.FromEnvironment().BaseUrl;
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
        return ServiceProvider.GetCrashRecoveryService();

      if (serviceType == typeof(VoiceStudio.Core.Services.IBackendClient))
        return ServiceProvider.GetBackendClient();

      // Default: return null (service not available)
      return null;
    }
  }
}
