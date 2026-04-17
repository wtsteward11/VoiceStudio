using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Tests.Helpers;

/// <summary>
/// Shared predicates for opt-in live-backend XTTS proofs when the machine has no engine / model.
/// </summary>
internal static class LiveXttsBackendTestGuards
{
  /// <summary>
  /// True when the backend reports XTTS (or engine router) cannot run — inconclusive, not a seam failure.
  /// </summary>
  public static bool IsLiveXttsEngineUnavailable(BackendException ex)
  {
    if (ex.StatusCode is not (500 or 503))
    {
      return false;
    }

    var m = ex.Message ?? "";
    return m.Contains("xtts", StringComparison.OrdinalIgnoreCase)
           && (m.Contains("not available", StringComparison.OrdinalIgnoreCase)
               || m.Contains("failed to initialize", StringComparison.OrdinalIgnoreCase)
               || m.Contains("503", StringComparison.OrdinalIgnoreCase));
  }
}
