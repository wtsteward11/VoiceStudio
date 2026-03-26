using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Pass 05 Option C: after a recording is uploaded to the library, optionally copy it into the active project via <see cref="IProjectAudioClient"/>.
/// </summary>
public static class RecordingToProjectPersistence
{
  /// <summary>
  /// When <paramref name="projectId"/> is set, persists library <paramref name="libraryAudioId"/> to the project directory.
  /// Failures are logged and not rethrown — the library upload has already succeeded.
  /// </summary>
  public static async Task TrySaveAfterUploadAsync(
    IProjectAudioClient projectAudioClient,
    IErrorLoggingService? log,
    string? projectId,
    string libraryAudioId,
    string? localSourcePath,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(projectAudioClient);
    if (string.IsNullOrWhiteSpace(projectId))
      return;
    if (string.IsNullOrWhiteSpace(libraryAudioId))
      return;

    try
    {
      var filename = string.IsNullOrWhiteSpace(localSourcePath)
        ? null
        : Path.GetFileName(localSourcePath);
      await projectAudioClient
        .SaveAudioToProjectAsync(projectId!, libraryAudioId, filename, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      log?.LogError(ex, "SaveRecordingToProject");
    }
  }
}
