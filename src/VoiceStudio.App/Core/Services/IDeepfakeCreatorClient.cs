using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Deepfake Creator API (/api/deepfake-creator).
  /// Use instead of IBackendClient for DeepfakeCreator panel.
  /// </summary>
  public interface IDeepfakeCreatorClient
  {
    Task<DeepfakeEngine[]?> GetEnginesAsync(CancellationToken cancellationToken = default);

    Task<DeepfakeJob[]?> GetJobsAsync(CancellationToken cancellationToken = default);

    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);

    Task<DeepfakeJobResponse?> CreateDeepfakeAsync(
      string sourceFacePath,
      string targetMediaPath,
      DeepfakeCreateRequest request,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Request payload for deepfake creation.
  /// </summary>
  public class DeepfakeCreateRequest
  {
    public string MediaType { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public bool ConsentGiven { get; set; }
    public bool ApplyWatermark { get; set; } = true;
    public string Quality { get; set; } = "high";
  }
}
