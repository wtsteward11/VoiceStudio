using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/style-transfer (job-based style transfer: presets, transfer, jobs).
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface IStyleTransferClient
  {
    /// <summary>
    /// Gets style transfer presets.
    /// </summary>
    Task<StyleTransferPresetResponse[]?> GetPresetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a style transfer job.
    /// </summary>
    Task<StyleTransferJobResponse?> CreateTransferAsync(StyleTransferCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets style transfer jobs.
    /// </summary>
    Task<StyleTransferJobResponse[]?> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a style transfer job.
    /// </summary>
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);
  }
}
