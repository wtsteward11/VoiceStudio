namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// GAP-045 Option B: explicit transcript truth vs timeline clip audio after operations that invalidate linkage (e.g. segment regeneration).
  /// Persisted on <see cref="AudioClip"/> in project JSON.
  /// </summary>
  public enum TranscriptTruthState
  {
    /// <summary>Transcript segments (if linked) match this clip's current audio.</summary>
    Current = 0,

    /// <summary>Clip audio was replaced and linkage was cleared; operator must run canonical transcript refresh.</summary>
    StaleAfterClipRegeneration = 1,

    /// <summary>Canonical refresh in flight; ignore concurrent refresh attempts.</summary>
    RefreshInProgress = 2,
  }
}
