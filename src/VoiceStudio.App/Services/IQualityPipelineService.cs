using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for engine-specific quality pipeline operations. Delegates to IBackendClient.
  /// </summary>
  public interface IQualityPipelineService
  {
    Task<List<string>> ListQualityPipelinePresetsAsync(string engineId, CancellationToken ct = default);
    Task<PipelineConfiguration?> GetQualityPipelineAsync(string engineId, string presetName, CancellationToken ct = default);
    Task<PreviewPipelineResponse> PreviewQualityPipelineAsync(string audioId, string engineId, string? presetName, PipelineConfiguration? config, CancellationToken ct = default);
    Task<PipelineComparisonResponse> CompareQualityPipelineAsync(string audioId, string engineId, string? presetName, CancellationToken ct = default);
  }
}
