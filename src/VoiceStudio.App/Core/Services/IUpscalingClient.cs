using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/upscaling (image and video upscaling).
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface IUpscalingClient
  {
    /// <summary>
    /// Gets available upscaling engines.
    /// </summary>
    Task<UpscalingEngineResponse[]?> GetEnginesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file and starts an upscaling job.
    /// </summary>
    Task<UpscalingJobResponse?> UploadAndUpscaleAsync(
      string filePath,
      UpscalingUpscaleRequest request,
      System.IProgress<double>? progress = null,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets upscaling jobs.
    /// </summary>
    Task<UpscalingJobResponse[]?> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an upscaling job.
    /// </summary>
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);
  }
}
