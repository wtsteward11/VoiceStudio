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
  /// <summary>Legacy success marker; prefer <see cref="ExactAppend"/> or <see cref="DefaultAtZeroBecauseTrackEmpty"/>.</summary>
  Added = 0,
  Unavailable = 1,
  Failed = 2,
  /// <summary>Clip placed immediately after the latest valid existing clip end on the target track.</summary>
  ExactAppend = 3,
  /// <summary>Clip placed at 0 s because the track&apos;s clip list was present and explicitly empty.</summary>
  DefaultAtZeroBecauseTrackEmpty = 4,
  /// <summary>Cannot determine a safe start time (e.g. clip payload missing or all existing clips lack valid timing).</summary>
  PlacementUnavailable = 5,
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
    string? ClipId,
    double? PlacementStartSeconds = null);
