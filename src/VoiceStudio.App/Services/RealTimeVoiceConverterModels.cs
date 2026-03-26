using System.Collections.Generic;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Request/response models for the real-time voice converter API (/api/realtime-converter).
  /// </summary>
  public class ConverterSession
  {
    public string SessionId { get; set; } = string.Empty;
    public string SourceProfileId { get; set; } = string.Empty;
    public string TargetProfileId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Created { get; set; } = string.Empty;
  }

  public class ConverterSessionListResponse
  {
    public List<ConverterSession> Sessions { get; set; } = new();
  }

  public class ConverterStartResponse
  {
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }
}
