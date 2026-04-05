using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Audio visualization service. Delegates to IBackendClient for waveform and spectrogram data.
  /// GAP-038 slice 0: bounded in-memory cache for waveform responses (same audioId/width/mode).
  /// </summary>
  public sealed class AudioVisualizationService : IAudioVisualizationService
  {
    private const int MaxWaveformCacheEntries = 64;

    private readonly IBackendClient _backend;
    private readonly object _waveformCacheLock = new();
    private readonly Dictionary<string, WaveformData> _waveformCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _waveformCacheOrder = new();

    public AudioVisualizationService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    public async Task<WaveformData> GetWaveformDataAsync(
        string audioId,
        int width = 1024,
        string mode = "peak",
        CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(audioId))
        return await _backend.GetWaveformDataAsync(audioId, width, mode, cancellationToken).ConfigureAwait(false);

      var key = BuildWaveformKey(audioId, width, mode);
      lock (_waveformCacheLock)
      {
        if (_waveformCache.TryGetValue(key, out var cached))
          return CloneWaveform(cached);
      }

      var fresh = await _backend.GetWaveformDataAsync(audioId, width, mode, cancellationToken).ConfigureAwait(false);
      var toStore = CloneWaveform(fresh);
      lock (_waveformCacheLock)
      {
        if (_waveformCache.Count >= MaxWaveformCacheEntries && _waveformCacheOrder.Count > 0)
        {
          var victim = _waveformCacheOrder.Dequeue();
          _waveformCache.Remove(victim);
        }

        _waveformCache[key] = toStore;
        _waveformCacheOrder.Enqueue(key);
      }

      return CloneWaveform(toStore);
    }

    private static string BuildWaveformKey(string audioId, int width, string mode) =>
        $"{audioId}\u001f{width}\u001f{mode}";

    private static WaveformData CloneWaveform(WaveformData source)
    {
      var samples = source.Samples == null
          ? new List<float>()
          : source.Samples.Select(s => s).ToList();
      return new WaveformData
      {
        Samples = samples,
        SampleRate = source.SampleRate,
        Duration = source.Duration,
        Channels = source.Channels,
        Width = source.Width,
        Mode = source.Mode,
      };
    }

    public Task<SpectrogramData> GetSpectrogramDataAsync(
        string audioId,
        int width = 512,
        int height = 256,
        CancellationToken cancellationToken = default)
      => _backend.GetSpectrogramDataAsync(audioId, width, height, cancellationToken);
  }
}
