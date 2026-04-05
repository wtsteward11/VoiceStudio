using VoiceStudio.Core.Recording;

namespace VoiceStudio.Core.Services;

/// <summary>
/// Project-scoped authority for multitrack recording session lifecycle (GAP-042).
/// Slice 1: in-memory lifecycle state machine. Slice 2–3: track id + input device id assignment on arm; Slice 3 allows multiple armed tracks.
/// Slice 4+ recovery UX remains separate.
/// </summary>
public interface IRecordingSessionCoordinator
{
    string? BoundProjectId { get; }

    System.Guid? ActiveSessionId { get; }

    MultitrackRecordingSessionPhase Phase { get; }

    System.Collections.Generic.IReadOnlySet<string> ArmedTrackIds { get; }

    /// <summary>Armed track id → backend input device id (Slice 2 assignment authority).</summary>
    System.Collections.Generic.IReadOnlyDictionary<string, string> TrackInputAssignments { get; }

    RecordingSessionStatus GetStatus();

    void BindProject(string? projectId);

    bool TryCreateSession(out string? errorMessage);

    /// <summary>Arms a timeline track with a validated input device id (backend id). Rejects unknown input and duplicate input across distinct arms (GAP-042 Slice 3 multitrack).</summary>
    bool TryArmTrack(string trackId, string inputSourceId, out string? errorMessage);

    bool TryDisarmTrack(string trackId, out string? errorMessage);

    bool TryStartRecording(out string? errorMessage);

    bool TryStopRecording(out string? errorMessage);

    void CancelSession();
}
