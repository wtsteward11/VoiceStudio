using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Outcome of attempting to copy transcribed source library audio into the active project.
/// </summary>
public enum TranscribeProjectAudioSaveOutcome
{
  SkippedNoProject,
  SkippedNoAudioId,
  Saved,
  Failed
}

/// <summary>
/// Pass 05 Option A: after transcription succeeds, optionally persist the **source library audio id** to the project.
/// </summary>
public static class TranscribeToProjectPersistence
{
  /// <summary>
  /// When <paramref name="projectId"/> and <paramref name="libraryAudioId"/> are set, copies the library asset into project audio via <see cref="IProjectAudioClient"/>.
  /// On exception, logs and returns <see cref="TranscribeProjectAudioSaveOutcome.Failed"/> — transcription success is independent.
  /// </summary>
  public static async Task<TranscribeProjectAudioSaveOutcome> TrySaveLibraryAudioToProjectAsync(
    IProjectAudioClient projectAudioClient,
    IErrorLoggingService? log,
    string? projectId,
    string? libraryAudioId,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(projectAudioClient);
    if (string.IsNullOrWhiteSpace(projectId))
      return TranscribeProjectAudioSaveOutcome.SkippedNoProject;
    if (string.IsNullOrWhiteSpace(libraryAudioId))
      return TranscribeProjectAudioSaveOutcome.SkippedNoAudioId;

    try
    {
      await projectAudioClient
          .SaveAudioToProjectAsync(projectId!, libraryAudioId!, filename: null, cancellationToken)
          .ConfigureAwait(false);
      return TranscribeProjectAudioSaveOutcome.Saved;
    }
    catch (Exception ex)
    {
      log?.LogError(ex, "SaveTranscribeSourceToProject");
      return TranscribeProjectAudioSaveOutcome.Failed;
    }
  }
}
