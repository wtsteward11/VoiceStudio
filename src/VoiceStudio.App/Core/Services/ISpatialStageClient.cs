using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Spatial Audio API (/api/spatial-audio).
  /// Use instead of IBackendClient for SpatialStage panel.
  /// </summary>
  public interface ISpatialStageClient
  {
    Task<SpatialConfigInfo[]?> GetConfigsAsync(CancellationToken cancellationToken = default);

    Task<SpatialConfigInfo?> CreateConfigAsync(SpatialConfigCreateRequest request, CancellationToken cancellationToken = default);

    Task<SpatialConfigInfo?> UpdateConfigAsync(string configId, SpatialConfigUpdateRequest request, CancellationToken cancellationToken = default);

    Task DeleteConfigAsync(string configId, CancellationToken cancellationToken = default);

    Task<SpatialApplyResponse?> ApplySpatialAsync(string configId, string outputFormat = "wav", CancellationToken cancellationToken = default);

    Task<SpatialPreviewResponse?> PreviewSpatialAsync(string audioId, double x, double y, double z, double distance, CancellationToken cancellationToken = default);
  }
}
