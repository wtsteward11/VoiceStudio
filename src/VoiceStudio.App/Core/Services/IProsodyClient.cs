using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for prosody API (/api/prosody/configs, phonemes, apply).
  /// Use instead of IBackendClient for Prosody panel.
  /// </summary>
  public interface IProsodyClient
  {
    Task<ProsodyViewModel.ProsodyConfig[]?> GetConfigsAsync(CancellationToken cancellationToken = default);
    Task<ProsodyViewModel.ProsodyConfig?> CreateConfigAsync(string name, double pitch, double rate, double volume, string? intonation, CancellationToken cancellationToken = default);
    Task<ProsodyViewModel.ProsodyConfig?> UpdateConfigAsync(string configId, string name, double pitch, double rate, double volume, string? intonation, CancellationToken cancellationToken = default);
    Task DeleteConfigAsync(string configId, CancellationToken cancellationToken = default);
    Task<ProsodyViewModel.PhonemeAnalysisResponse?> AnalyzePhonemesAsync(string text, string language, CancellationToken cancellationToken = default);
    Task<ProsodyViewModel.ProsodyApplyResponse?> ApplyProsodyAsync(string configId, string text, string voiceProfileId, string? engine, CancellationToken cancellationToken = default);
  }
}
