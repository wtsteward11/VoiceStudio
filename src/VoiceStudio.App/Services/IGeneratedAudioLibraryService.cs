using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Integrates a generated synthesis output with the app library surface (see implementation for scope).
  /// </summary>
  public interface IGeneratedAudioLibraryService
  {
    Task<GeneratedAudioSaveResult> SaveAsync(
        GeneratedAudioSaveRequest request,
        CancellationToken cancellationToken = default);
  }

  /// <summary>Metadata for registering generated audio with the library workflow.</summary>
  public sealed record GeneratedAudioSaveRequest(
      string SourcePanelId,
      string AudioId,
      string? AudioReference,
      TimeSpan Duration,
      string? ProfileId,
      string? ProfileName,
      string? Engine,
      DateTime GeneratedAtLocal);

  /// <summary>Outcome of <see cref="IGeneratedAudioLibraryService.SaveAsync"/>.</summary>
  public sealed record GeneratedAudioSaveResult(bool Success, string? ErrorMessage);
}
