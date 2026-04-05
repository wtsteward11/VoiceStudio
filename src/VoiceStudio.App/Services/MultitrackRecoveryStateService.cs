using System;
using System.IO;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

public sealed class MultitrackRecoveryStateService : IMultitrackRecoveryStateService
{
  private readonly CrashRecoveryService _crash;
  private const string LogCategory = "MultitrackRecovery";

  public MultitrackRecoveryStateService(CrashRecoveryService crash)
  {
    _crash = crash ?? throw new ArgumentNullException(nameof(crash));
  }

  public bool HasPendingPayload(SessionState? peekedState)
  {
    if (peekedState == null)
      return false;
    return peekedState.CustomState.TryGetValue(MultitrackRecoveryKeys.PayloadV1, out var raw)
           && MultitrackRecoveryPayloadJson.TryParseFromCustomStateValue(raw, out var p)
           && p != null
           && !p.EndedCleanly;
  }

  public bool TryReadPayload(SessionState? state, out MultitrackRecoveryPayload? payload)
  {
    payload = null;
    if (state == null)
      return false;
    if (!state.CustomState.TryGetValue(MultitrackRecoveryKeys.PayloadV1, out var raw))
      return false;
    return MultitrackRecoveryPayloadJson.TryParseFromCustomStateValue(raw, out payload) && payload != null;
  }

  public void WritePending(MultitrackRecoveryPayload payload)
  {
    ArgumentNullException.ThrowIfNull(payload);
    var json = MultitrackRecoveryPayloadJson.Serialize(payload);
    _crash.UpdateState(state => state.CustomState[MultitrackRecoveryKeys.PayloadV1] = json);
  }

  public async Task WritePendingAndSaveAsync(MultitrackRecoveryPayload payload)
  {
    WritePending(payload);
    await _crash.SaveSessionAsync().ConfigureAwait(false);
  }

  public void ClearPending()
  {
    _crash.UpdateState(state =>
    {
      state.CustomState.Remove(MultitrackRecoveryKeys.PayloadV1);
    });
  }

  public async Task ClearPendingAndSaveAsync()
  {
    ClearPending();
    await _crash.SaveSessionAsync().ConfigureAwait(false);
  }

  public void DeletePreservedLegFiles(MultitrackRecoveryPayload payload)
  {
    ArgumentNullException.ThrowIfNull(payload);
    foreach (var leg in payload.Legs)
    {
      if (leg.Status != MultitrackRecoveryLegStatus.Completed)
        continue;
      var path = leg.PreservedOutputPath;
      if (string.IsNullOrWhiteSpace(path))
        continue;
      try
      {
        if (File.Exists(path))
          File.Delete(path);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Multitrack recovery discard: could not delete '{path}': {ex.Message}", LogCategory);
      }
    }
  }
}
