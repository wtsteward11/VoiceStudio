using System;
using System.Reflection;

namespace VoiceStudio.App.Diagnostics;

/// <summary>
/// Unwraps reflection and aggregate exceptions for user-facing panel load errors and support logs.
/// </summary>
public static class ExceptionDiagnostics
{
  public static Exception GetRootException(Exception ex)
  {
    ArgumentNullException.ThrowIfNull(ex);
    var current = ex;
    for (var depth = 0; depth < 32; depth++)
    {
      if (current is TargetInvocationException { InnerException: { } inner })
      {
        current = inner;
        continue;
      }

      if (current is AggregateException ae)
      {
        if (ae.InnerExceptions.Count == 1)
        {
          current = ae.InnerExceptions[0];
          continue;
        }

        break;
      }

      break;
    }

    return current;
  }

  public static string FormatPanelCreateUserMessage(string panelId, Exception ex)
  {
    var root = GetRootException(ex);
    return $"Failed to create panel '{panelId}': {root.GetType().Name}: {root.Message}";
  }

  public static string FormatPanelLoadUserMessage(string panelId, Exception ex)
  {
    var root = GetRootException(ex);
    return $"Failed to load panel '{panelId}': {root.GetType().Name}: {root.Message}";
  }

  /// <summary>
  /// Appends full exception text (including stack) for support; not DEBUG-only.
  /// </summary>
  public static void AppendPanelLoadFailureDiagnosticsFile(string panelId, Exception ex)
  {
    try
    {
      var diagDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceStudio", "crashes");
      System.IO.Directory.CreateDirectory(diagDir);
      var path = System.IO.Path.Combine(diagDir, "panel_load_failure_diag.txt");
      var text = $"[{DateTime.UtcNow:O}] panelId={panelId}\n{ex}\n---\n";
      System.IO.File.AppendAllText(path, text);
    }
    catch (Exception writeEx)
    {
      System.Diagnostics.Debug.WriteLine(
        $"[ExceptionDiagnostics] panel_load_failure_diag write failed: {writeEx.Message}");
    }
  }
}
