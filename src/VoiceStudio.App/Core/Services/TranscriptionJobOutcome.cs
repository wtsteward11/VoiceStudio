using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  public enum TranscriptionJobOutcome
  {
    RealCompleted,
    SimulatedCompleted,
    Unavailable,
    Failed,
    InvalidCompleted,
  }

  public static class TranscriptionJobOutcomeClassifier
  {
    public static TranscriptionJobOutcome Classify(TranscriptionJobResponse r) => r.Status switch
    {
      "unavailable" => TranscriptionJobOutcome.Unavailable,
      "failed" => TranscriptionJobOutcome.Failed,
      "completed" when r.Transcript != null && r.IsSimulated => TranscriptionJobOutcome.SimulatedCompleted,
      "completed" when r.Transcript != null && r.RealTranscriptionPerformed => TranscriptionJobOutcome.RealCompleted,
      "completed" when r.Transcript != null => TranscriptionJobOutcome.InvalidCompleted,
      "completed" when !string.IsNullOrWhiteSpace(r.TranscriptId) && r.IsSimulated => TranscriptionJobOutcome.SimulatedCompleted,
      "completed" when !string.IsNullOrWhiteSpace(r.TranscriptId) && r.RealTranscriptionPerformed => TranscriptionJobOutcome.RealCompleted,
      "completed" when !string.IsNullOrWhiteSpace(r.TranscriptId) => TranscriptionJobOutcome.InvalidCompleted,
      "completed" => TranscriptionJobOutcome.InvalidCompleted,
      _ => TranscriptionJobOutcome.Failed,
    };
  }
}
