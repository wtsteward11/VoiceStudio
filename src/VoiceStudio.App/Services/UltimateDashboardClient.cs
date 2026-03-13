using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/ultimate-dashboard.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class UltimateDashboardClient : IUltimateDashboardClient
  {
    private readonly IBackendClient _backend;

    public UltimateDashboardClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<UltimateDashboardData?> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, UltimateDashboardData>(
        "/api/ultimate-dashboard",
        null,
        HttpMethod.Get,
        cancellationToken);
    }
  }
}
