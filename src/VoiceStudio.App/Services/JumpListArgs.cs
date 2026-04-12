using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Command-line tokens for Win32 taskbar jump list activations (unpackaged app; not MSIX JumpList APIs).
/// </summary>
public static class JumpListArgs
{
  public const string NewProject = "--jumplist-new";
  public const string OpenDialog = "--jumplist-open-dialog";
  public const string OpenProjectPrefix = "--jumplist-open";

  /// <summary>
  /// Builds argv for opening a recent project; quotes paths that contain whitespace.
  /// </summary>
  public static string FormatOpenProjectArgument(string projectPath)
  {
    if (string.IsNullOrEmpty(projectPath))
    {
      return OpenProjectPrefix;
    }

    if (projectPath.Contains(' ', StringComparison.Ordinal) || projectPath.Contains('\"', StringComparison.Ordinal))
    {
      var escaped = projectPath.Replace("\"", "\\\"", StringComparison.Ordinal);
      return OpenProjectPrefix + " \"" + escaped + "\"";
    }

    return OpenProjectPrefix + " " + projectPath;
  }
}
