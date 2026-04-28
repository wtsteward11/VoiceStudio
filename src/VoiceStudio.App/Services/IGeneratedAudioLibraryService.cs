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

  /// <summary>Classification of <see cref="GeneratedAudioSaveResult"/> outcomes.</summary>
  public enum GeneratedAudioSaveKind
  {
    /// <summary>Validation failed, upload failed, or an unexpected error occurred.</summary>
    Failed = 0,
    /// <summary>Only <c>AssetAddedEvent</c> was published; no durable library upload (e.g. API-only reference).</summary>
    EventNotified = 1,
    /// <summary>File was uploaded to the library; no project copy (no project or project save did not complete as project-backed).</summary>
    LibraryBacked = 2,
    /// <summary>Uploaded to the library and associated with the active project.</summary>
    ProjectBacked = 3,
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
  public sealed record GeneratedAudioSaveResult(
      bool Success,
      string? ErrorMessage,
      GeneratedAudioSaveKind SaveKind,
      string? AssetId,
      string? PlaybackAudioId,
      string? ProjectId,
      string? Message,
      string? FilePath)
  {
    /// <summary>Backward-compatible constructor: success maps to <see cref="GeneratedAudioSaveKind.EventNotified"/> (soft add).</summary>
    public GeneratedAudioSaveResult(bool success, string? errorMessage)
        : this(
            success,
            errorMessage,
            success ? GeneratedAudioSaveKind.EventNotified : GeneratedAudioSaveKind.Failed,
            null,
            null,
            null,
            null,
            null)
    {
    }
  }
}
