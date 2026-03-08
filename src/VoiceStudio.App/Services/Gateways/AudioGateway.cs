using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Gateways;

namespace VoiceStudio.App.Services.Gateways
{
  /// <summary>
  /// Gateway implementation for audio file operations, streaming, and waveform analysis.
  /// </summary>
  public sealed class AudioGateway : IAudioGateway
  {
    private readonly IBackendTransport _transport;

    public AudioGateway(IBackendTransport transport)
    {
      _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<GatewayResult<AudioFileInfo>> UploadAsync(
        Stream fileStream,
        string fileName,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
      var contentType = GetContentType(fileName);
      return await _transport.UploadAsync<AudioFileInfo>(
          "/api/audio/upload",
          fileStream,
          fileName,
          "file",
          contentType,
          progress,
          cancellationToken);
    }

    public async Task<GatewayResult<bool>> DownloadAsync(
        string audioId,
        Stream outputStream,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
      return await _transport.DownloadAsync(
          $"/api/audio/{Uri.EscapeDataString(audioId)}/download",
          outputStream,
          progress,
          cancellationToken);
    }

    public async Task<GatewayResult<AudioFileInfo>> GetInfoAsync(
        string audioId,
        CancellationToken cancellationToken = default)
    {
      return await _transport.GetAsync<AudioFileInfo>(
          $"/api/audio/{Uri.EscapeDataString(audioId)}",
          cancellationToken);
    }

    public async Task<GatewayResult<WaveformData>> GetWaveformAsync(
        string audioId,
        int samplesPerSecond = 100,
        CancellationToken cancellationToken = default)
    {
      return await _transport.GetAsync<WaveformData>(
          $"/api/audio/{Uri.EscapeDataString(audioId)}/waveform?samples_per_second={samplesPerSecond}",
          cancellationToken);
    }

    public async Task<GatewayResult<bool>> DeleteAsync(
        string audioId,
        CancellationToken cancellationToken = default)
    {
      return await _transport.DeleteAsync(
          $"/api/audio/{Uri.EscapeDataString(audioId)}",
          cancellationToken);
    }

    public async Task<GatewayResult<AudioFileInfo>> ConvertAsync(
        string audioId,
        string targetFormat,
        CancellationToken cancellationToken = default)
    {
      return await _transport.PostAsync<object, AudioFileInfo>(
          $"/api/audio/{Uri.EscapeDataString(audioId)}/convert",
          new { format = targetFormat },
          cancellationToken);
    }

    public async Task<GatewayResult<Stream>> StreamAsync(
        string audioId,
        CancellationToken cancellationToken = default)
    {
      return await _transport.GetStreamAsync(
          $"/api/audio/file/{Uri.EscapeDataString(audioId)}",
          cancellationToken);
    }

    private static string GetContentType(string fileName)
    {
      var ext = Path.GetExtension(fileName).ToLowerInvariant();
      return ext switch
      {
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        _ => "application/octet-stream"
      };
    }
  }
}
