using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// A/B test service. Delegates to IBackendClient for RunABTestAsync and GetAudioStreamAsync.
  /// </summary>
  public sealed class ABTestService : IABTestService
  {
    private readonly IBackendClient _backend;

    public ABTestService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<ABTestResponse> RunABTestAsync(ABTestRequest request, CancellationToken cancellationToken = default)
      => _backend.RunABTestAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default)
      => _backend.GetAudioStreamAsync(audioId, cancellationToken);
  }
}
