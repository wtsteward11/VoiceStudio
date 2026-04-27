using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Suppresses identical workspace-restore failure toasts when multiple code paths
/// (e.g. startup init and profile-changed restore) run in quick succession.
/// </summary>
internal static class WorkspaceRestoreFailureToastSuppressor
{
  private static readonly object Gate = new();
  private static string? _lastKey;
  private static DateTime _lastUtc;

  /// <summary>
  /// Returns true if this (title, message) should not be shown because an identical toast was shown within the window.
  /// </summary>
  public static bool ShouldSuppressDuplicate(string title, string message, DateTime utcNow, TimeSpan window)
  {
    var key = title + "\0" + message;
    lock (Gate)
    {
      if (_lastKey != null
          && string.Equals(_lastKey, key, StringComparison.Ordinal)
          && (utcNow - _lastUtc) < window)
      {
        return true;
      }

      _lastKey = key;
      _lastUtc = utcNow;
      return false;
    }
  }

  internal static void ResetForTests()
  {
    lock (Gate)
    {
      _lastKey = null;
    }
  }
}
