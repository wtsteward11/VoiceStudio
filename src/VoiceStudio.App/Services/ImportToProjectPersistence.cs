using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Pass 05 P05-Persist-A2/A3: after library import succeeds, optionally copy asset(s) into the active project via <see cref="IProjectAudioClient"/>.
/// </summary>
public static class ImportToProjectPersistence
{
  /// <summary>
  /// When <paramref name="projectId"/> is set, persists library <paramref name="libraryAudioId"/> to the project. Failures are logged and not rethrown.
  /// </summary>
  public static Task TrySaveAfterSingleFileImportAsync(
    IProjectAudioClient projectAudioClient,
    IErrorLoggingService? log,
    string? projectId,
    string libraryAudioId,
    string? localSourcePath,
    CancellationToken cancellationToken) =>
    TrySaveAfterSingleFileImportAsync(
      projectAudioClient,
      log,
      projectId,
      libraryAudioId,
      localSourcePath,
      cancellationToken,
      logContext: "SaveImportToProject");

  /// <summary>
  /// P05-Persist-A3: after batch <c>/api/library/import</c> returns <see cref="LibraryItem"/> rows, optionally copy each eligible item when <paramref name="projectId"/> is set.
  /// Pairing: index <paramref name="i"/> with <paramref name="orderedSourcePaths"/>[<paramref name="i"/>] when <paramref name="orderedSourcePaths"/>.Count allows; otherwise filename hint falls back to <see cref="LibraryItem.Name"/>.
  /// No <c>AssetAddedEvent</c> contract change in this slice.
  /// </summary>
  public static async Task TrySaveAfterBatchLibraryImportAsync(
    IProjectAudioClient projectAudioClient,
    IErrorLoggingService? log,
    string? projectId,
    IReadOnlyList<string> orderedSourcePaths,
    IReadOnlyList<LibraryItem> importedItems,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(projectAudioClient);
    ArgumentNullException.ThrowIfNull(importedItems);
    if (importedItems.Count == 0 || string.IsNullOrWhiteSpace(projectId))
      return;

    ArgumentNullException.ThrowIfNull(orderedSourcePaths);

    for (var i = 0; i < importedItems.Count; i++)
    {
      var item = importedItems[i];
      if (string.IsNullOrWhiteSpace(item.Id))
        continue;

      string? pathHint = i < orderedSourcePaths.Count ? orderedSourcePaths[i] : null;
      if (string.IsNullOrWhiteSpace(pathHint) && !string.IsNullOrWhiteSpace(item.Name))
        pathHint = item.Name;

      await TrySaveAfterSingleFileImportAsync(
          projectAudioClient,
          log,
          projectId,
          item.Id,
          pathHint,
          cancellationToken,
          logContext: "SaveBatchImportToProject")
        .ConfigureAwait(false);
    }
  }

  internal static async Task TrySaveAfterSingleFileImportAsync(
    IProjectAudioClient projectAudioClient,
    IErrorLoggingService? log,
    string? projectId,
    string libraryAudioId,
    string? localSourcePath,
    CancellationToken cancellationToken,
    string logContext)
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
      log?.LogError(ex, logContext);
    }
  }
}
