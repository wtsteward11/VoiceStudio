namespace VoiceStudio.Core.Models
{
  public readonly struct TranscriptionSegmentLinkInput
  {
    public TranscriptionSegmentLinkInput(string id, double start, double end)
    {
      Id = id ?? string.Empty;
      Start = start;
      End = end;
    }

    public string Id { get; }

    public double Start { get; }

    public double End { get; }
  }
}
