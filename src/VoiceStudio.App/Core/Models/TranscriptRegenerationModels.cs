namespace VoiceStudio.App.Core.Models;

public sealed class RegenerateSegmentStartRequest
{
  public string ProjectId { get; set; } = string.Empty;
  public string TrackId { get; set; } = string.Empty;
  public string ClipId { get; set; } = string.Empty;
  public string TranscriptionId { get; set; } = string.Empty;
  public string SegmentId { get; set; } = string.Empty;
  public string? ReplacementText { get; set; }
  public string? ProfileId { get; set; }
  public string? Engine { get; set; }
}

public sealed class RegenerateSegmentJobStartResponse
{
  public string JobId { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
}
