using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for automation API (/api/automation).
  /// Use instead of IBackendClient for automation curves, tracks, and parameters.
  /// </summary>
  public interface IAutomationClient
  {
    Task<AutomationTrackInfo[]> GetTracksAsync(CancellationToken cancellationToken = default);
    Task<AutomationCurve[]> GetCurvesAsync(string? trackId = null, string? parameterId = null, CancellationToken cancellationToken = default);
    Task<AutomationCurve?> CreateCurveAsync(AutomationCreateRequest request, CancellationToken cancellationToken = default);
    Task<AutomationCurve?> UpdateCurveAsync(string curveId, AutomationUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteCurveAsync(string curveId, CancellationToken cancellationToken = default);
    Task<AutomationTrackParametersResponse?> GetTrackParametersAsync(string trackId, CancellationToken cancellationToken = default);
  }
}
