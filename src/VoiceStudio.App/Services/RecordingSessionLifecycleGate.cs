using System;
using VoiceStudio.Core.Recording;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Shared GAP-042 Slice 1 gate: project bind, session create, arm, start — and safe unwind helpers.
/// </summary>
public static class RecordingSessionLifecycleGate
{
    /// <summary>
    /// Binds project, moves stuck <see cref="MultitrackRecordingSessionPhase.Recording"/> back via stop, and ensures <c>Prepared</c> with an open session.
    /// Does not arm tracks or start capture.
    /// </summary>
    public static bool TryPrepareRecordingSessionShell(
        IRecordingSessionCoordinator coordinator,
        string? projectId,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        coordinator.BindProject(projectId);

        if (coordinator.Phase == MultitrackRecordingSessionPhase.Recording)
        {
            if (!coordinator.TryStopRecording(out errorMessage))
                return false;
        }

        if (coordinator.Phase == MultitrackRecordingSessionPhase.None)
        {
            if (!coordinator.TryCreateSession(out errorMessage))
                return false;
        }

        if (coordinator.Phase != MultitrackRecordingSessionPhase.Prepared)
        {
            errorMessage = "Recording session not in Prepared phase.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Reconciles coordinator into <see cref="MultitrackRecordingSessionPhase.Prepared"/>, arms <paramref name="trackId"/>
    /// with <paramref name="inputSourceId"/>, then enters <see cref="MultitrackRecordingSessionPhase.Recording"/>.
    /// If the coordinator was stuck in <c>Recording</c> without capture, calls <see cref="IRecordingSessionCoordinator.TryStopRecording"/> first.
    /// </summary>
    public static bool TryPrepareAndStartRecording(
        IRecordingSessionCoordinator coordinator,
        string? projectId,
        string trackId,
        string inputSourceId,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        coordinator.BindProject(projectId);

        if (coordinator.Phase == MultitrackRecordingSessionPhase.Recording)
        {
            _ = coordinator.TryStopRecording(out _);
        }

        if (coordinator.Phase == MultitrackRecordingSessionPhase.None)
        {
            if (!coordinator.TryCreateSession(out errorMessage))
                return false;
        }

        if (coordinator.Phase != MultitrackRecordingSessionPhase.Prepared)
        {
            errorMessage = "Recording session not in Prepared phase.";
            return false;
        }

        if (!coordinator.TryArmTrack(trackId, inputSourceId, out errorMessage))
            return false;

        return coordinator.TryStartRecording(out errorMessage);
    }

    /// <summary>Unwind after successful mic stop (session remains Prepared for another take).</summary>
    public static void NotifyCaptureStopped(IRecordingSessionCoordinator? coordinator)
    {
        if (coordinator == null)
            return;
        _ = coordinator.TryStopRecording(out _);
    }

    /// <summary>Full reset when the user discards recording or on fatal coordinator/mic mismatch.</summary>
    public static void NotifyCaptureCancelled(IRecordingSessionCoordinator? coordinator)
    {
        if (coordinator == null)
            return;
        coordinator.CancelSession();
    }

    /// <summary>If mic failed after coordinator entered Recording, return coordinator to Prepared.</summary>
    public static void NotifyCaptureStartFailed(IRecordingSessionCoordinator? coordinator)
    {
        if (coordinator == null)
            return;
        _ = coordinator.TryStopRecording(out _);
    }
}
