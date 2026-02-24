using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App
{
  public static class Program
  {
    private const uint WindowsAppSdkMajorMinorVersion = 0x00010008; // Windows App SDK 1.8
    private const string SingleInstanceMutexName = "VoiceStudio_SingleInstance_Mutex_v1";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
      // Single-instance enforcement: prevent multiple app instances
      bool createdNew;
      _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
      if (!createdNew)
      {
        // Another instance is already running - exit silently
        // Optionally: could bring existing window to foreground via IPC
        return;
      }

      var crashDir = GetCrashDir();
      WriteBootMarker(crashDir, "main_entered", args);
      ApplySmokeArgsToEnvironment(args);
      RegisterEarlyUnhandledHandlers(crashDir);

      try
      {
        MainImpl(args, crashDir);
      }
      catch (Exception ex)
      {
        // NOTE: If we crash before App.xaml.cs is constructed, this is the only
        // deterministic crash artifact we can capture from managed code.
        WriteStartupException(crashDir, ex);

        // Re-throw to ensure process exit code reflects failure.
        throw;
      }
      finally
      {
        // Release mutex when app exits
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
      }
    }

    private static void ApplySmokeArgsToEnvironment(string[] args)
    {
      try
      {
        static bool HasFlag(string[] a, string flag)
        {
          foreach (var item in a)
          {
            if (string.Equals(item, flag, StringComparison.OrdinalIgnoreCase))
            {
              return true;
            }
          }

          return false;
        }

        if (HasFlag(args, "--smoke-ui") || HasFlag(args, "--ui-smoke"))
        {
          Environment.SetEnvironmentVariable("VOICE_STUDIO_SMOKE_UI", "1");
        }

        if (HasFlag(args, "--smoke-exit"))
        {
          Environment.SetEnvironmentVariable("VOICE_STUDIO_SMOKE_EXIT", "1");
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "Program.HasFlag");
      }
    }

    private static void MainImpl(string[] args, string crashDir)
    {
      var bootstrapInitialized = false;

      try
      {
        // For unpackaged apps, initialize the Windows App SDK runtime before any WinUI types are activated.
        // This prevents COM "Class not registered" failures in environments where the runtime isn't loaded yet.
        if (!IsPackaged())
        {
          WriteBootMarker(crashDir, "bootstrap_initialize_begin", args);
          Bootstrap.Initialize(WindowsAppSdkMajorMinorVersion);
          bootstrapInitialized = true;
          WriteBootMarker(crashDir, "bootstrap_initialize_done", args);
        }

        Application.Start((_) =>
        {
          WriteBootMarker(crashDir, "application_start_callback_entered", args);
          var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
          SynchronizationContext.SetSynchronizationContext(context);
          WinRT.ComWrappersSupport.InitializeComWrappers();
          WriteBootMarker(crashDir, "com_wrappers_done", args);
          new App();
          WriteBootMarker(crashDir, "app_created", args);
        });
      }
      finally
      {
        if (bootstrapInitialized)
        {
          Bootstrap.Shutdown();
        }
      }
    }

    private static bool IsPackaged()
    {
      try
      {
        _ = Package.Current;
        return true;
      }
      catch
      {
        return false;
      }
    }

    private static string GetCrashDir()
    {
      return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceStudio",
        "crashes");
    }

    private static void RegisterEarlyUnhandledHandlers(string crashDir)
    {
      try
      {
        AppDomain.CurrentDomain.UnhandledException += (sender, evt) =>
        {
          try
          {
            var ex = evt.ExceptionObject as Exception;
            var payload = ex != null ? ex.ToString() : $"Unhandled: {evt.ExceptionObject}";
            WriteTextBestEffort(crashDir, "appdomain_unhandled", payload);
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "Program.RegisterEarlyUnhandledHandlers");
      }
        };

        TaskScheduler.UnobservedTaskException += (sender, evt) =>
        {
          try
          {
            WriteTextBestEffort(crashDir, "unobserved_task", evt.Exception.ToString());
            evt.SetObserved();
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "Program.RegisterEarlyUnhandledHandlers");
      }
        };
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "Program.RegisterEarlyUnhandledHandlers");
      }
    }

    private static void WriteBootMarker(string crashDir, string stage, string[] args)
    {
      try
      {
        Directory.CreateDirectory(crashDir);

        var payload = new
        {
          timestamp_utc = DateTime.UtcNow.ToString("o"),
          stage,
          pid = Environment.ProcessId,
          process_path = Environment.ProcessPath,
          packaged = SafeIsPackaged(),
          working_dir = Environment.CurrentDirectory,
          args = args ?? Array.Empty<string>(),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
          payload,
          new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(Path.Combine(crashDir, "boot_latest.json"), json);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "Program.WriteBootMarker");
      }
    }

    private static bool SafeIsPackaged()
    {
      try
      {
        _ = Package.Current;
        return true;
      }
      catch
      {
        return false;
      }
    }

    private static void WriteStartupException(string crashDir, Exception ex)
    {
      try
      {
        Directory.CreateDirectory(crashDir);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var logPath = Path.Combine(crashDir, $"startup_exception_{timestamp}.log");
        File.WriteAllText(logPath, ex.ToString());

        File.WriteAllText(
          Path.Combine(crashDir, "latest_startup_exception.log"),
          $"See: {logPath}");
      }
      catch (Exception logEx)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {logEx.Message}", "Program.WriteStartupException");
      }
    }

    private static void WriteTextBestEffort(string crashDir, string prefix, string payload)
    {
      try
      {
        Directory.CreateDirectory(crashDir);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var logPath = Path.Combine(crashDir, $"{prefix}_{timestamp}.log");
        File.WriteAllText(logPath, payload);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "Program.WriteTextBestEffort");
      }
    }
  }
}