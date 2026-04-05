using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

public sealed class MultitrackRecoveryApplyResult
{
  public bool Success { get; init; }

  public string? ErrorMessage { get; init; }

  public int RestoredLegCount { get; init; }
}

/// <summary>
/// Restores completed multitrack takes after user confirms recovery (upload + project save).
/// </summary>
public interface IMultitrackRecoveryApplyService
{
  /// <summary>
  /// Validates <paramref name="activeProjectId"/> matches payload, then uploads each completed leg with an on-disk file.
  /// </summary>
  Task<MultitrackRecoveryApplyResult> TryRestoreCompletedTakesAsync(
      string? activeProjectId,
      MultitrackRecoveryPayload payload,
      CancellationToken cancellationToken = default);
}
