using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Persists multitrack recording recovery payloads in <see cref="CrashRecoveryService.SessionState.CustomState"/>.
/// </summary>
public interface IMultitrackRecoveryStateService
{
  /// <summary>True if current pending peek or live session state contains a v1 multitrack recovery payload.</summary>
  bool HasPendingPayload(SessionState? peekedState);

  bool TryReadPayload(SessionState? state, out MultitrackRecoveryPayload? payload);

  void WritePending(MultitrackRecoveryPayload payload);

  Task WritePendingAndSaveAsync(MultitrackRecoveryPayload payload);

  void ClearPending();

  Task ClearPendingAndSaveAsync();

  /// <summary>Deletes on-disk WAVs referenced by completed legs (used before discarding a cold snapshot).</summary>
  void DeletePreservedLegFiles(MultitrackRecoveryPayload payload);
}
