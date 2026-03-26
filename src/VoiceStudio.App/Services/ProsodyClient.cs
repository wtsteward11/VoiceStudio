using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for prosody API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class ProsodyClient : IProsodyClient
  {
    private readonly IBackendClient _backend;

    public ProsodyClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Task<ProsodyViewModel.ProsodyConfig[]?> GetConfigsAsync(CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<object, ProsodyViewModel.ProsodyConfig[]>("/api/prosody/configs", null, HttpMethod.Get, cancellationToken);

    public Task<ProsodyViewModel.ProsodyConfig?> CreateConfigAsync(string name, double pitch, double rate, double volume, string? intonation, CancellationToken cancellationToken = default)
    {
      var request = new { name, pitch, rate, volume, intonation };
      return _backend.SendRequestAsync<object, ProsodyViewModel.ProsodyConfig>("/api/prosody/configs", request, HttpMethod.Post, cancellationToken);
    }

    public Task<ProsodyViewModel.ProsodyConfig?> UpdateConfigAsync(string configId, string name, double pitch, double rate, double volume, string? intonation, CancellationToken cancellationToken = default)
    {
      var request = new { name, pitch, rate, volume, intonation };
      var url = $"/api/prosody/configs/{Uri.EscapeDataString(configId)}";
      return _backend.SendRequestAsync<object, ProsodyViewModel.ProsodyConfig>(url, request, HttpMethod.Put, cancellationToken);
    }

    public Task DeleteConfigAsync(string configId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/prosody/configs/{Uri.EscapeDataString(configId)}";
      return _backend.SendRequestAsync<object, object>(url, null, HttpMethod.Delete, cancellationToken);
    }

    public Task<ProsodyViewModel.PhonemeAnalysisResponse?> AnalyzePhonemesAsync(string text, string language, CancellationToken cancellationToken = default)
    {
      var url = $"/api/prosody/phonemes/analyze?text={Uri.EscapeDataString(text)}&language={Uri.EscapeDataString(language)}";
      return _backend.SendRequestAsync<object, ProsodyViewModel.PhonemeAnalysisResponse>(url, null, HttpMethod.Post, cancellationToken);
    }

    public Task<ProsodyViewModel.ProsodyApplyResponse?> ApplyProsodyAsync(string configId, string text, string voiceProfileId, string? engine, CancellationToken cancellationToken = default)
    {
      var request = new { config_id = configId, text, voice_profile_id = voiceProfileId, engine };
      return _backend.SendRequestAsync<object, ProsodyViewModel.ProsodyApplyResponse>("/api/prosody/apply", request, HttpMethod.Post, cancellationToken);
    }
  }
}
