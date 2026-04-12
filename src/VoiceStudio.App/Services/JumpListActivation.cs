using System;
using System.Linq;

namespace VoiceStudio.App.Services;

public enum JumpListPendingKind
{
  NewProject,
  OpenDialog,
  OpenProject,
}

/// <summary>
/// Pending jump list activation consumed once after shell and startup are ready (ADR-047).
/// </summary>
public sealed class JumpListPendingAction
{
  public JumpListPendingAction(JumpListPendingKind kind, string? projectPath)
  {
    Kind = kind;
    ProjectPath = projectPath;
  }

  public JumpListPendingKind Kind { get; }
  public string? ProjectPath { get; }
}

/// <summary>
/// Parses taskbar jump list command lines and holds at most one pending action for the main window.
/// </summary>
public static class JumpListActivation
{
  private static readonly object Gate = new();
  private static JumpListPendingAction? _pending;

  /// <summary>
  /// Records a parsed pending action when jump list flags are present (replaces any prior pending).
  /// </summary>
  public static void SetPendingIfParsed(string? launchArgs, string[]? commandLineArgs)
  {
    var parsed = TryParse(launchArgs, commandLineArgs);
    if (parsed == null)
    {
      return;
    }

    lock (Gate)
    {
      _pending = parsed;
    }
  }

  /// <summary>
  /// Returns and clears the pending action, if any.
  /// </summary>
  public static JumpListPendingAction? TryConsumePending()
  {
    lock (Gate)
    {
      var p = _pending;
      _pending = null;
      return p;
    }
  }

  /// <summary>
  /// Exposed for unit tests; parses the same rules as shell activation.
  /// </summary>
  public static JumpListPendingAction? TryParse(string? launchArgs, string[]? commandLineArgs)
  {
    var blob = BuildArgumentBlob(launchArgs, commandLineArgs);
    if (string.IsNullOrWhiteSpace(blob))
    {
      return null;
    }

    if (ContainsWholeToken(blob, JumpListArgs.NewProject))
    {
      return new JumpListPendingAction(JumpListPendingKind.NewProject, null);
    }

    if (ContainsWholeToken(blob, JumpListArgs.OpenDialog))
    {
      return new JumpListPendingAction(JumpListPendingKind.OpenDialog, null);
    }

    var marker = JumpListArgs.OpenProjectPrefix;
    var idx = blob.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (idx < 0)
    {
      return null;
    }

    var rest = blob.Substring(idx + marker.Length).TrimStart();
    if (rest.Length == 0)
    {
      return null;
    }

    if (rest[0] == '\"')
    {
      var end = rest.IndexOf('\"', 1);
      if (end > 1)
      {
        var path = rest.Substring(1, end - 1).Replace("\\\"", "\"", StringComparison.Ordinal);
        return new JumpListPendingAction(JumpListPendingKind.OpenProject, path);
      }

      return null;
    }

    var space = rest.IndexOf(' ');
    var pathToken = space < 0 ? rest : rest.Substring(0, space);
    return string.IsNullOrWhiteSpace(pathToken)
      ? null
      : new JumpListPendingAction(JumpListPendingKind.OpenProject, pathToken.Trim());
  }

  private static string BuildArgumentBlob(string? launchArgs, string[]? commandLineArgs)
  {
    if (!string.IsNullOrWhiteSpace(launchArgs))
    {
      return launchArgs.Trim();
    }

    var argv = commandLineArgs ?? Environment.GetCommandLineArgs();
    if (argv.Length <= 1)
    {
      return string.Empty;
    }

    return string.Join(" ", argv.Skip(1)).Trim();
  }

  private static bool ContainsWholeToken(string blob, string token)
  {
    var i = 0;
    while ((i = blob.IndexOf(token, i, StringComparison.OrdinalIgnoreCase)) >= 0)
    {
      var before = i == 0 || char.IsWhiteSpace(blob[i - 1]);
      var after = i + token.Length >= blob.Length || char.IsWhiteSpace(blob[i + token.Length]);
      if (before && after)
      {
        return true;
      }

      i++;
    }

    return false;
  }
}
