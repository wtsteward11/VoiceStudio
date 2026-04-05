using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;

namespace VoiceStudio.App.Core.Services;

/// <summary>
/// GAP-046: canonical HTTP seam for transcript segment regeneration (starts backend job).
/// </summary>
public interface ITranscriptRegenerationClient
{
  /// <summary>POST /api/transcribe/regenerate-segment — returns job id (202 Accepted).</summary>
  Task<RegenerateSegmentJobStartResponse?> StartRegenerateSegmentAsync(
      RegenerateSegmentStartRequest request,
      CancellationToken cancellationToken = default);
}
