using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  public class ClipTranscriptLink
  {
    public string ClipId { get; set; } = string.Empty;
    public string TranscriptionId { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public List<string> SegmentIds { get; set; } = new List<string>();
  }
}
