namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Request body for <c>POST /api/transcribe/jobs</c>.
  /// </summary>
  public class TranscriptionJobRequest
  {
    public string AudioId { get; set; } = string.Empty;

    public string Engine { get; set; } = "whisper";

    public string? Language { get; set; }

    public bool WordTimestamps { get; set; }

    public bool Simulate { get; set; }

    /// <summary>When true, backend returns immediately with <c>pending</c> and runs work in the background.</summary>
    public bool AsyncMode { get; set; }
  }

  /// <summary>
  /// Response from <c>POST /api/transcribe/jobs</c> or <c>GET /api/transcribe/jobs/{job_id}</c>.
  /// </summary>
  public class TranscriptionJobResponse
  {
    public string JobId { get; set; } = string.Empty;

    public string AudioId { get; set; } = string.Empty;

    public string? TranscriptId { get; set; }

    /// <summary>Backend values include <c>completed</c>, <c>unavailable</c>, <c>failed</c>, <c>pending</c>, <c>running</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Backend values include <c>real</c>, <c>simulation</c>, <c>unavailable</c>, <c>pending</c>.</summary>
    public string Mode { get; set; } = string.Empty;

    public bool IsSimulated { get; set; }

    public bool RealTranscriptionPerformed { get; set; }

    public string? Blocker { get; set; }

    public TranscriptionResponse? Transcript { get; set; }

    /// <summary>Canonical job progress 0..1 when durable polling is used.</summary>
    public float? Progress { get; set; }
  }
}
