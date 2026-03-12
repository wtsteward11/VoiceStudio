using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Quality history service. Delegates to IBackendClient.
  /// </summary>
  public sealed class QualityHistoryService : IQualityHistoryService
  {
    private readonly IBackendClient _backend;

    public QualityHistoryService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<QualityHistoryEntry> StoreQualityHistoryAsync(QualityHistoryRequest request, CancellationToken ct = default)
      => _backend.StoreQualityHistoryAsync(request, ct);
  }
}
