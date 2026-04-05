using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

public sealed class MultitrackRecoveryApplyService : IMultitrackRecoveryApplyService
{
  private readonly IRecordingClient _recordingClient;
  private readonly IProjectAudioClient _projectAudioClient;
  private readonly IErrorLoggingService? _log;

  public MultitrackRecoveryApplyService(
      IRecordingClient recordingClient,
      IProjectAudioClient projectAudioClient,
      IErrorLoggingService? log = null)
  {
    _recordingClient = recordingClient ?? throw new ArgumentNullException(nameof(recordingClient));
    _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
    _log = log;
  }

  public async Task<MultitrackRecoveryApplyResult> TryRestoreCompletedTakesAsync(
      string? activeProjectId,
      MultitrackRecoveryPayload payload,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(payload);
    if (string.IsNullOrWhiteSpace(payload.ProjectId))
    {
      return new MultitrackRecoveryApplyResult
      {
        Success = false,
        ErrorMessage = "Recovery payload has no project id.",
      };
    }

    if (string.IsNullOrWhiteSpace(activeProjectId)
        || !string.Equals(activeProjectId, payload.ProjectId, StringComparison.Ordinal))
    {
      return new MultitrackRecoveryApplyResult
      {
        Success = false,
        ErrorMessage = "Active project does not match recovery payload — restore blocked.",
      };
    }

    var restored = 0;
    foreach (var leg in payload.Legs)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (leg.Status != MultitrackRecoveryLegStatus.Completed)
        continue;
      var path = leg.PreservedOutputPath;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        continue;

      try
      {
        var upload = await _recordingClient.UploadAudioFileAsync(path, cancellationToken).ConfigureAwait(false);
        var hint = $"{leg.TrackId}_{Path.GetFileName(path)}";
        await RecordingToProjectPersistence.TrySaveAfterUploadAsync(
                _projectAudioClient,
                _log,
                payload.ProjectId,
                upload.Id,
                path,
                cancellationToken,
                hint)
            .ConfigureAwait(false);
        restored++;
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        _log?.LogError(ex, "MultitrackRecoveryRestore");
        return new MultitrackRecoveryApplyResult
        {
          Success = false,
          ErrorMessage = $"Restore failed on track '{leg.TrackId}': {ex.Message}",
          RestoredLegCount = restored,
        };
      }
    }

    return new MultitrackRecoveryApplyResult { Success = true, RestoredLegCount = restored };
  }
}
