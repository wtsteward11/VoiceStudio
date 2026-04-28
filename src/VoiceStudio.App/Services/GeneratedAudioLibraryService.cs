using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Publishes <see cref="AssetAddedEvent"/> so library/timeline consumers can react.
  /// This slice does not call <c>ILibraryClient.UploadLibraryAssetAsync</c> or project audio APIs
  /// (no project id in-panel; URL-only assets may not be local files). Documented product limitation.
  /// </summary>
  public sealed class GeneratedAudioLibraryService : IGeneratedAudioLibraryService
  {
    private readonly IEventAggregator _eventAggregator;

    public GeneratedAudioLibraryService(IEventAggregator eventAggregator)
    {
      _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    }

    /// <inheritdoc />
    public Task<GeneratedAudioSaveResult> SaveAsync(
        GeneratedAudioSaveRequest request,
        CancellationToken cancellationToken = default)
    {
      if (request == null)
        throw new ArgumentNullException(nameof(request));

      var primaryId = string.IsNullOrWhiteSpace(request.AudioId)
          ? (request.AudioReference ?? string.Empty)
          : request.AudioId;
      if (string.IsNullOrWhiteSpace(primaryId))
        return Task.FromResult(new GeneratedAudioSaveResult(false, "No audio ID or reference."));

      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        _eventAggregator.Publish(new AssetAddedEvent(
            request.SourcePanelId,
            primaryId.Trim(),
            "audio",
            string.IsNullOrWhiteSpace(request.AudioReference) ? null : request.AudioReference));
        return Task.FromResult(new GeneratedAudioSaveResult(true, null));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return Task.FromResult(new GeneratedAudioSaveResult(false, ex.Message));
      }
    }
  }
}
