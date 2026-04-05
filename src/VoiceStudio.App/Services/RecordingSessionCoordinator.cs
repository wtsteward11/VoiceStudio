using System;
using System.Collections.Generic;
using System.Linq;
using VoiceStudio.Core.Recording;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// In-memory multitrack recording session authority (GAP-042).
/// Slice 2–3: multi-track arm + input device id assignment; capture fan-out executed via <see cref="RecordingCaptureFanoutService"/>.
/// </summary>
public sealed class RecordingSessionCoordinator : IRecordingSessionCoordinator
{
    private readonly object _gate = new();
    private string? _boundProjectId;
    private Guid? _activeSessionId;
    private MultitrackRecordingSessionPhase _phase = MultitrackRecordingSessionPhase.None;
    private readonly Dictionary<string, string> _assignment = new(StringComparer.Ordinal);

    public string? BoundProjectId
    {
        get
        {
            lock (_gate)
                return _boundProjectId;
        }
    }

    public Guid? ActiveSessionId
    {
        get
        {
            lock (_gate)
                return _activeSessionId;
        }
    }

    public MultitrackRecordingSessionPhase Phase
    {
        get
        {
            lock (_gate)
                return _phase;
        }
    }

    public IReadOnlySet<string> ArmedTrackIds
    {
        get
        {
            lock (_gate)
                return _assignment.Keys.ToHashSet(StringComparer.Ordinal);
        }
    }

    public IReadOnlyDictionary<string, string> TrackInputAssignments
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, string>(_assignment, StringComparer.Ordinal);
        }
    }

    public RecordingSessionStatus GetStatus()
    {
        lock (_gate)
        {
            return new RecordingSessionStatus(
                _boundProjectId,
                _activeSessionId,
                _phase,
                _assignment.Keys.ToList(),
                new Dictionary<string, string>(_assignment, StringComparer.Ordinal));
        }
    }

    public void BindProject(string? projectId)
    {
        lock (_gate)
        {
            if (!string.Equals(_boundProjectId, projectId, StringComparison.Ordinal))
            {
                ResetSessionUnlocked();
                _boundProjectId = projectId;
            }
        }
    }

    public bool TryCreateSession(out string? errorMessage)
    {
        lock (_gate)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(_boundProjectId))
            {
                errorMessage = "No project bound.";
                return false;
            }

            if (_phase == MultitrackRecordingSessionPhase.Recording)
            {
                errorMessage = "Cannot create session while recording.";
                return false;
            }

            if (_phase == MultitrackRecordingSessionPhase.Prepared)
            {
                errorMessage = "Session already open.";
                return false;
            }

            _activeSessionId = Guid.NewGuid();
            _phase = MultitrackRecordingSessionPhase.Prepared;
            _assignment.Clear();
            return true;
        }
    }

    public bool TryArmTrack(string trackId, string inputSourceId, out string? errorMessage)
    {
        lock (_gate)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(trackId))
            {
                errorMessage = "Track id required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(inputSourceId))
            {
                errorMessage = "Input source id required.";
                return false;
            }

            if (_phase != MultitrackRecordingSessionPhase.Prepared)
            {
                errorMessage = "Arming only allowed in Prepared phase.";
                return false;
            }

            foreach (var kv in _assignment)
            {
                if (!string.Equals(kv.Key, trackId, StringComparison.Ordinal)
                    && string.Equals(kv.Value, inputSourceId, StringComparison.Ordinal))
                {
                    errorMessage = "Duplicate input assignment across tracks is not allowed.";
                    return false;
                }
            }

            _assignment[trackId] = inputSourceId;
            return true;
        }
    }

    public bool TryDisarmTrack(string trackId, out string? errorMessage)
    {
        lock (_gate)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(trackId))
            {
                errorMessage = "Track id required.";
                return false;
            }

            if (_phase != MultitrackRecordingSessionPhase.Prepared)
            {
                errorMessage = "Disarm only allowed in Prepared phase.";
                return false;
            }

            _assignment.Remove(trackId);
            return true;
        }
    }

    public bool TryStartRecording(out string? errorMessage)
    {
        lock (_gate)
        {
            errorMessage = null;
            if (_phase == MultitrackRecordingSessionPhase.Recording)
                return true;

            if (_phase != MultitrackRecordingSessionPhase.Prepared)
            {
                errorMessage = "Start requires an open session in Prepared phase.";
                return false;
            }

            if (_assignment.Count == 0)
            {
                errorMessage = "At least one armed track is required.";
                return false;
            }

            _phase = MultitrackRecordingSessionPhase.Recording;
            return true;
        }
    }

    public bool TryStopRecording(out string? errorMessage)
    {
        lock (_gate)
        {
            errorMessage = null;
            if (_phase == MultitrackRecordingSessionPhase.Recording)
            {
                _phase = MultitrackRecordingSessionPhase.Prepared;
                _assignment.Clear();
            }

            return true;
        }
    }

    public void CancelSession()
    {
        lock (_gate)
            ResetSessionUnlocked();
    }

    private void ResetSessionUnlocked()
    {
        _phase = MultitrackRecordingSessionPhase.None;
        _activeSessionId = null;
        _assignment.Clear();
    }
}
