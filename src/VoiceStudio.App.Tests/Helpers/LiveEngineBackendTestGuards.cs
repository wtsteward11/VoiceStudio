using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Tests.Helpers;

/// <summary>
/// Shared predicates for opt-in live-backend engine proofs when the machine has no engine / model.
/// </summary>
internal static class LiveEngineBackendTestGuards
{
  /// <summary>
  /// True when the backend reports the given engine cannot run — use with Assert.Inconclusive or Assert.Fail per test policy.
  /// </summary>
  public static bool IsLiveEngineUnavailable(BackendException ex, string engineId)
  {
    if (ex.StatusCode is not (500 or 503))
    {
      return false;
    }

    var m = ex.Message ?? "";
    var mentions = m.Contains(engineId, StringComparison.OrdinalIgnoreCase);
    if (!mentions && engineId.Equals("xtts_v2", StringComparison.OrdinalIgnoreCase))
    {
      mentions = m.Contains("xtts", StringComparison.OrdinalIgnoreCase);
    }

    if (!mentions)
    {
      return false;
    }

    return m.Contains("not available", StringComparison.OrdinalIgnoreCase)
           || m.Contains("failed to initialize", StringComparison.OrdinalIgnoreCase)
           || m.Contains("503", StringComparison.OrdinalIgnoreCase);
  }
}

/// <summary>Backward-compatible name for XTTS-only call sites.</summary>
internal static class LiveXttsBackendTestGuards
{
  public static bool IsLiveXttsEngineUnavailable(BackendException ex) =>
    LiveEngineBackendTestGuards.IsLiveEngineUnavailable(ex, "xtts_v2");
}
