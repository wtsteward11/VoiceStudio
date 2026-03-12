using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Quality pipeline service. Delegates to IBackendClient.
  /// </summary>
  public sealed class QualityPipelineService : IQualityPipelineService
  {
    private readonly IBackendClient _backend;

    public QualityPipelineService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<string>> ListQualityPipelinePresetsAsync(string engineId, CancellationToken ct = default)
      => _backend.ListQualityPipelinePresetsAsync(engineId, ct);

    /// <inheritdoc />
    public Task<PipelineConfiguration?> GetQualityPipelineAsync(string engineId, string presetName, CancellationToken ct = default)
      => _backend.GetQualityPipelineAsync(engineId, presetName, ct);

    /// <inheritdoc />
    public Task<PreviewPipelineResponse> PreviewQualityPipelineAsync(string audioId, string engineId, string? presetName, PipelineConfiguration? config, CancellationToken ct = default)
      => _backend.PreviewQualityPipelineAsync(audioId, engineId, presetName, config, ct);

    /// <inheritdoc />
    public Task<PipelineComparisonResponse> CompareQualityPipelineAsync(string audioId, string engineId, string? presetName, CancellationToken ct = default)
      => _backend.CompareQualityPipelineAsync(audioId, engineId, presetName, ct);
  }
}
