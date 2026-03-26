using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Advanced Spectrogram API (/api/advanced-spectrogram).
  /// Use instead of IBackendClient for AdvancedSpectrogramVisualization panel.
  /// </summary>
  public interface IAdvancedSpectrogramClient
  {
    Task<AdvancedSpectrogramViewTypesResponse?> GetViewTypesAsync(CancellationToken cancellationToken = default);

    Task<AdvancedSpectrogramGenerateResponse?> GenerateSpectrogramAsync(
      AdvancedSpectrogramGenerateRequest request,
      CancellationToken cancellationToken = default);

    Task<AdvancedSpectrogramCompareResponse?> CompareSpectrogramsAsync(
      AdvancedSpectrogramCompareRequest request,
      CancellationToken cancellationToken = default);
  }
}
