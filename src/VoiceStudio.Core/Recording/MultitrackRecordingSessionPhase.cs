namespace VoiceStudio.Core.Recording;

/// <summary>
/// Lifecycle phase for a multitrack recording session (GAP-042 Slice 1).
/// </summary>
public enum MultitrackRecordingSessionPhase
{
    None,
    Prepared,
    Recording,
}
