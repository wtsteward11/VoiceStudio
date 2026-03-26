using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Text Highlighting API models. Matches backend /api/text-highlighting.
  /// </summary>
  public class TextHighlightingSession
  {
    public string Id { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public TextHighlightingSegment[] Segments { get; set; } = System.Array.Empty<TextHighlightingSegment>();
    public double CurrentTime { get; set; }
    public string Created { get; set; } = string.Empty;
  }

  public class TextHighlightingSegment
  {
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public Dictionary<string, object>[]? WordTimings { get; set; }
  }

  public class TextHighlightingSyncResponse
  {
    public string? ActiveSegmentId { get; set; }
    public int? ActiveWordIndex { get; set; }
    public TextHighlightingSegment[] Segments { get; set; } = System.Array.Empty<TextHighlightingSegment>();
  }

  public class TextHighlightingCreateRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string HighlightType { get; set; } = "word";
    public TextHighlightingSegment[]? Segments { get; set; }
  }

  public class TextHighlightingSyncRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public double CurrentTime { get; set; }
  }

  public class TextHighlightingUpdateSegmentDto
  {
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string HighlightType { get; set; } = "word";
    public Dictionary<string, object>[]? WordTimings { get; set; }
  }

  public class TextHighlightingUpdateRequest
  {
    public double CurrentTime { get; set; }
    public TextHighlightingUpdateSegmentDto[] Segments { get; set; } = System.Array.Empty<TextHighlightingUpdateSegmentDto>();
  }

  public class TextHighlightingPersistRequest
  {
    public string SessionId { get; set; } = string.Empty;
    public string? AudioId { get; set; }
    public string Text { get; set; } = string.Empty;
    public TextHighlightingUpdateSegmentDto[] Segments { get; set; } = System.Array.Empty<TextHighlightingUpdateSegmentDto>();
    public string Created { get; set; } = string.Empty;
  }
}
