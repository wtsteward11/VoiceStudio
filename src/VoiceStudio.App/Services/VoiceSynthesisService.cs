using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Voice synthesis service. Delegates to IBackendClient for SynthesizeVoiceAsync and GetAudioStreamAsync.
  /// Task 1.2 (deepen): Deferred — request shaping/response normalization blocked by VoiceSynthesisRequest/Response
  /// type location (App.Core.Models vs Core; Compile Remove may exclude). See SEAM_MATURITY_AUDIT.
  /// </summary>
  public sealed class VoiceSynthesisService : IVoiceSynthesisService
  {
    private readonly IBackendClient _backend;

    public VoiceSynthesisService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<VoiceSynthesisResponse> SynthesizeVoiceAsync(VoiceSynthesisRequest request, CancellationToken cancellationToken = default)
      => _backend.SynthesizeVoiceAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default)
      => _backend.GetAudioStreamAsync(audioId, cancellationToken);
  }
}
