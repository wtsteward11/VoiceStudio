using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Advanced Waveform API (/api/waveform/data, config, analysis).
  /// Use instead of IBackendClient for AdvancedWaveformVisualization panel.
  /// </summary>
  public interface IAdvancedWaveformClient
  {
    Task<AdvancedWaveformData?> GetWaveformDataAsync(
      string audioId,
      double? zoomLevel = null,
      double? timeStart = null,
      double? timeEnd = null,
      CancellationToken cancellationToken = default);

    Task<AdvancedWaveformConfigResponse?> UpdateConfigAsync(
      string audioId,
      AdvancedWaveformConfigRequest request,
      CancellationToken cancellationToken = default);

    Task<AdvancedWaveformAnalysis?> GetAnalysisAsync(
      string audioId,
      CancellationToken cancellationToken = default);
  }
}
