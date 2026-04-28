using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// When a local synthesis output file exists, uploads via <see cref="ILibraryClient"/>, updates transport context,
  /// optionally copies into the active project, and publishes <see cref="AssetAddedEvent"/>. API-only assets publish
  /// the event only (<see cref="GeneratedAudioSaveKind.EventNotified"/>).
  /// </summary>
  public sealed class GeneratedAudioLibraryService : IGeneratedAudioLibraryService
  {
    private readonly IEventAggregator _eventAggregator;
    private readonly ILibraryClient _libraryClient;
    private readonly IContextManager _contextManager;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IErrorLoggingService? _errorLoggingService;

    public GeneratedAudioLibraryService(
        IEventAggregator eventAggregator,
        ILibraryClient libraryClient,
        IContextManager contextManager,
        IProjectAudioClient projectAudioClient,
        IErrorLoggingService? errorLoggingService = null)
    {
      _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
      _libraryClient = libraryClient ?? throw new ArgumentNullException(nameof(libraryClient));
      _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _errorLoggingService = errorLoggingService;
    }

    /// <inheritdoc />
    public async Task<GeneratedAudioSaveResult> SaveAsync(
        GeneratedAudioSaveRequest request,
        CancellationToken cancellationToken = default)
    {
      if (request == null)
        throw new ArgumentNullException(nameof(request));

      if (TryResolveLocalFileForUpload(request, out var localPath) &&
          !string.IsNullOrWhiteSpace(localPath))
      {
        return await SaveWithLocalFileAsync(request, localPath, cancellationToken).ConfigureAwait(false);
      }

      var primaryId = string.IsNullOrWhiteSpace(request.AudioId)
          ? (request.AudioReference ?? string.Empty)
          : request.AudioId;
      if (string.IsNullOrWhiteSpace(primaryId))
      {
        return new GeneratedAudioSaveResult(
            false,
            "No audio ID or reference.",
            GeneratedAudioSaveKind.Failed,
            null,
            null,
            null,
            null,
            null);
      }

      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        var trimmed = primaryId.Trim();
        _eventAggregator.Publish(new AssetAddedEvent(
            request.SourcePanelId,
            trimmed,
            "audio",
            string.IsNullOrWhiteSpace(request.AudioReference) ? null : request.AudioReference));
        return new GeneratedAudioSaveResult(
            true,
            null,
            GeneratedAudioSaveKind.EventNotified,
            null,
            trimmed,
            null,
            "Library notified; project-backed save requires a local generated audio file.",
            null);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return new GeneratedAudioSaveResult(
            false,
            ex.Message,
            GeneratedAudioSaveKind.Failed,
            null,
            null,
            null,
            null,
            null);
      }
    }

    private async Task<GeneratedAudioSaveResult> SaveWithLocalFileAsync(
        GeneratedAudioSaveRequest request,
        string localPath,
        CancellationToken cancellationToken)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        var uploadedAsset = await _libraryClient
            .UploadLibraryAssetAsync(localPath, cancellationToken)
            .ConfigureAwait(false);
        if (uploadedAsset == null)
        {
          return new GeneratedAudioSaveResult(
              false,
              "Library upload returned no asset.",
              GeneratedAudioSaveKind.Failed,
              null,
              null,
              null,
              null,
              localPath);
        }

        var playbackId = GetPlaybackAudioId(uploadedAsset) ?? uploadedAsset.Id;
        var fileName = Path.GetFileName(localPath);

        _eventAggregator.Publish(new AssetAddedEvent(request.SourcePanelId, playbackId, "audio", localPath));
        _contextManager.SetCurrentPlayable(playbackId, TransportSource.Library, fileName);
        _contextManager.SetActiveAsset(uploadedAsset.Id, "audio", fileName);

        var projectId = _contextManager.ActiveProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
        {
          return new GeneratedAudioSaveResult(
              true,
              null,
              GeneratedAudioSaveKind.LibraryBacked,
              uploadedAsset.Id,
              playbackId,
              null,
              "No active project; asset is in the library.",
              localPath);
        }

        try
        {
          await _projectAudioClient
              .SaveAudioToProjectAsync(projectId!, playbackId, fileName, cancellationToken)
              .ConfigureAwait(false);
          return new GeneratedAudioSaveResult(
              true,
              null,
              GeneratedAudioSaveKind.ProjectBacked,
              uploadedAsset.Id,
              playbackId,
              projectId,
              null,
              localPath);
        }
        catch (Exception ex)
        {
          _errorLoggingService?.LogError(ex, "GeneratedAudioLibrary.SaveToProject");
          return new GeneratedAudioSaveResult(
              true,
              null,
              GeneratedAudioSaveKind.LibraryBacked,
              uploadedAsset.Id,
              playbackId,
              projectId,
              $"Saved to library; project save failed: {ex.Message}",
              localPath);
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return new GeneratedAudioSaveResult(
            false,
            ex.Message,
            GeneratedAudioSaveKind.Failed,
            null,
            null,
            null,
            null,
            localPath);
      }
    }

    /// <summary>
    /// Resolves a user-accessible local file for durable upload. Rejects API URLs, relative paths, and directories.
    /// </summary>
    internal static bool TryResolveLocalFileForUpload(GeneratedAudioSaveRequest request, out string? path)
    {
      path = null;
      if (TryResolveFromReference(request.AudioReference, out path))
        return true;
      if (!string.IsNullOrWhiteSpace(request.AudioId) && TryResolveFromReference(request.AudioId, out path))
        return true;
      return false;
    }

    private static bool TryResolveFromReference(string? reference, out string? path)
    {
      path = null;
      if (string.IsNullOrWhiteSpace(reference))
        return false;

      var candidate = reference.Trim();
      if (candidate.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
          candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
          candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
      {
        if (uri.IsFile)
          candidate = uri.LocalPath;
        else
          return false;
      }
      else if (!Path.IsPathFullyQualified(candidate))
      {
        return false;
      }

      if (File.Exists(candidate))
      {
        path = candidate;
        return true;
      }

      return false;
    }

    private static string? GetPlaybackAudioId(LibraryAsset asset)
    {
      if (asset == null)
        return null;
      if (!string.IsNullOrEmpty(asset.AudioId))
        return asset.AudioId;
      if (asset.Metadata != null && asset.Metadata.TryGetValue("upload_id", out var v))
      {
        var s = v as string;
#if NET6_0_OR_GREATER
        if (s == null && v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
          s = je.GetString();
#endif
        if (!string.IsNullOrEmpty(s))
          return s;
      }

      return asset.Id;
    }
  }
}
