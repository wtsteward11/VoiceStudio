using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/style-transfer (voice style transfer).
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface IVoiceStyleTransferClient
  {
    /// <summary>
    /// Extracts style from reference audio.
    /// </summary>
    Task<VoiceStyleTransferProfileResponse?> ExtractStyleAsync(
      VoiceStyleTransferExtractRequest request,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes style characteristics of reference audio.
    /// </summary>
    Task<VoiceStyleTransferAnalyzeResponse?> AnalyzeStyleAsync(
      VoiceStyleTransferAnalyzeRequest request,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesizes with style transfer.
    /// </summary>
    Task<VoiceStyleTransferSynthesizeResponse?> SynthesizeStyleAsync(
      VoiceStyleTransferSynthesizeRequest request,
      CancellationToken cancellationToken = default);
  }
}
