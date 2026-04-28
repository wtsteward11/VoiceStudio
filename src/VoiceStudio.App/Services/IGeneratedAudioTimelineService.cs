using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Inserts voice synthesis output into the active project timeline via backend clip APIs (no MainWindow coupling).
/// </summary>
public interface IGeneratedAudioTimelineService
{
  Task<GeneratedAudioTimelineResult> AddGeneratedClipAsync(
      GeneratedAudioTimelineRequest request,
      CancellationToken cancellationToken = default);
}

/// <summary>Typed outcome for UI — distinguishes missing context from transport/backend failures.</summary>
public enum GeneratedAudioTimelineKind
{
  Added = 0,
  Unavailable = 1,
  Failed = 2,
}

/// <summary>Inputs required to create a timeline clip with synthesis provenance.</summary>
public sealed record GeneratedAudioTimelineRequest(
    string AudioId,
    string? AudioPathOrUrl,
    TimeSpan Duration,
    string? ProfileId,
    string? ProfileName,
    string? Engine,
    DateTime GeneratedAtLocal,
    double? QualityScore,
    /// <summary>Library asset id from <see cref="IGeneratedAudioLibraryService"/> when present.</summary>
    string? LibraryAssetId,
    /// <summary>Optional first-line snippet for clip naming.</summary>
    string? TextPreview);

/// <summary>Result of attempting to persist a generated-audio clip on the timeline.</summary>
public sealed record GeneratedAudioTimelineResult(
    bool Success,
    GeneratedAudioTimelineKind Kind,
    string? Message,
    string? ProjectId,
    string? TrackId,
    string? ClipId);
