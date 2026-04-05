namespace VoiceStudio.Core.Recording;

public readonly struct RecordingSessionStatus
{
    public RecordingSessionStatus(
        string? projectId,
        System.Guid? activeSessionId,
        MultitrackRecordingSessionPhase phase,
        System.Collections.Generic.IReadOnlyList<string> armedTrackIds,
        System.Collections.Generic.IReadOnlyDictionary<string, string> trackInputAssignments)
    {
        ProjectId = projectId;
        ActiveSessionId = activeSessionId;
        Phase = phase;
        ArmedTrackIds = armedTrackIds;
        TrackInputAssignments = trackInputAssignments;
    }

    public string? ProjectId { get; }

    public System.Guid? ActiveSessionId { get; }

    public MultitrackRecordingSessionPhase Phase { get; }

    public System.Collections.Generic.IReadOnlyList<string> ArmedTrackIds { get; }

    /// <summary>Armed track id → canonical backend input device id (Slice 2).</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, string> TrackInputAssignments { get; }
}
