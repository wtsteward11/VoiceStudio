namespace VoiceStudio.Core.Recording;

/// <summary>
/// Slice 1 placeholder track id until per-track input mapping (GAP-042 Slice 2+).
/// Single-mic surfaces (Recording panel, Ctrl+R) arm this logical track for session authority.
/// </summary>
public static class RecordingSessionSlice1Defaults
{
    public const string PrimaryInputTrackId = "recording-primary-input";
}
