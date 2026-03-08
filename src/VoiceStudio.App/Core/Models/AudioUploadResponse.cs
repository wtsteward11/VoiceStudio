using System.Text.Json.Serialization;

namespace VoiceStudio.App.Core.Models
{
  /// <summary>
  /// Response from audio file upload.
  /// </summary>
  public class AudioUploadResponse
  {
    /// <summary>
    /// Unique identifier for the uploaded audio.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Alias for Id (backend returns "id"). Use for playback/streaming.
    /// </summary>
    public string AudioId => Id;

    /// <summary>
    /// Original filename.
    /// </summary>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// Server-side storage path.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Content type (MIME type) of the audio file.
    /// </summary>
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
  }
}
