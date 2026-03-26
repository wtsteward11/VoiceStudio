using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/health and /api/version. PR-5: extracted from IBackendClient.
  /// </summary>
  public interface IHealthVersionClient
  {
    /// <summary>
    /// Checks backend health via GET /api/health.
    /// </summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks API version compatibility via GET /api/version/compatibility.
    /// </summary>
    Task<ApiVersionCheckResult> CheckApiVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets version information via GET /api/version/.
    /// </summary>
    Task<ApiVersionInfo?> GetApiVersionInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates API version on startup; logs warnings if incompatible.
    /// </summary>
    Task<bool> ValidateApiVersionOnStartupAsync(CancellationToken cancellationToken = default);
  }
}
