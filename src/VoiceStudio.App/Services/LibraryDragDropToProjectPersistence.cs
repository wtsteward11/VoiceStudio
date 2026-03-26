using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Pass 05 P05-Persist-A4: after external-file drag-drop library upload succeeds, optionally copy the asset into the active project.
/// </summary>
public static class LibraryDragDropToProjectPersistence
{
  /// <summary>
  /// When <paramref name="contextManager"/> has a non-empty <see cref="IContextManager.ActiveProjectId"/>, persists <paramref name="libraryAudioId"/> to the project. Failures are logged and not rethrown.
  /// </summary>
  public static async Task TrySaveAfterLibraryDragDropUploadAsync(
    IProjectAudioClient projectAudioClient,
    IErrorLoggingService? log,
    IContextManager? contextManager,
    string libraryAudioId,
    string localSourcePath,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(projectAudioClient);
    var projectId = contextManager?.ActiveProjectId;
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
      log?.LogError(ex, "SaveDragDropToProject");
    }
  }
}
