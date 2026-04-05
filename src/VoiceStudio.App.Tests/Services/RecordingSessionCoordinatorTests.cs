using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Recording;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class RecordingSessionCoordinatorTests
{
    private const string In1 = "input-device-1";
    private const string In2 = "input-device-2";

    [TestMethod]
    public void TryCreateSession_WithoutBoundProject_Fails()
    {
        var sut = new RecordingSessionCoordinator();
        var ok = sut.TryCreateSession(out var err);
        Assert.IsFalse(ok);
        Assert.AreEqual("No project bound.", err);
        Assert.AreEqual(MultitrackRecordingSessionPhase.None, sut.Phase);
    }

    [TestMethod]
    public void TryStartRecording_WithoutArmedTracks_Fails()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("proj-a");
        Assert.IsTrue(sut.TryCreateSession(out _));
        var ok = sut.TryStartRecording(out var err);
        Assert.IsFalse(ok);
        StringAssert.Contains(err, "armed");
        Assert.AreEqual(MultitrackRecordingSessionPhase.Prepared, sut.Phase);
    }

    [TestMethod]
    public void TryArmTrack_WhenNotPrepared_Fails()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        var ok = sut.TryArmTrack("t1", In1, out var err);
        Assert.IsFalse(ok);
        Assert.IsNotNull(err);
    }

    [TestMethod]
    public void TryArmTrack_EmptyInputId_Fails()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        var ok = sut.TryArmTrack("t1", "  ", out var err);
        Assert.IsFalse(ok);
        StringAssert.Contains(err, "Input source id");
    }

    [TestMethod]
    public void TryArmTrack_SecondDistinctTrack_Succeeds_Slice3()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        Assert.IsTrue(sut.TryArmTrack("t2", In2, out var err), err);
        Assert.AreEqual(2, sut.ArmedTrackIds.Count);
    }

    [TestMethod]
    public void TryDisarmTrack_OneOfMany_RemovesOnlyThatTrack()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        Assert.IsTrue(sut.TryArmTrack("t2", In2, out _));
        Assert.IsTrue(sut.TryDisarmTrack("t1", out var err), err);
        Assert.IsFalse(sut.ArmedTrackIds.Contains("t1"));
        Assert.IsTrue(sut.ArmedTrackIds.Contains("t2"));
    }

    [TestMethod]
    public void TryArmTrack_DuplicateInputAcrossDistinctTracks_Rejects()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        var ok = sut.TryArmTrack("t2", In1, out var err);
        Assert.IsFalse(ok);
        StringAssert.Contains(err, "Duplicate input");
    }

    [TestMethod]
    public void TryArmTrack_SameTrack_UpdatesInputBinding()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In2, out var err2), err2);
        Assert.AreEqual(1, sut.ArmedTrackIds.Count);
        Assert.AreEqual(In2, sut.TrackInputAssignments["t1"]);
    }

    [TestMethod]
    public void TryStartRecording_WhenAlreadyRecording_IsIdempotent()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        Assert.IsTrue(sut.TryStartRecording(out _));
        Assert.IsTrue(sut.TryStartRecording(out var err2));
        Assert.IsNull(err2);
        Assert.AreEqual(MultitrackRecordingSessionPhase.Recording, sut.Phase);
    }

    [TestMethod]
    public void TryStopRecording_WhenNotRecording_IsSafe()
    {
        var sut = new RecordingSessionCoordinator();
        Assert.IsTrue(sut.TryStopRecording(out var err));
        Assert.IsNull(err);
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryStopRecording(out _));
        Assert.AreEqual(MultitrackRecordingSessionPhase.Prepared, sut.Phase);
    }

    [TestMethod]
    public void StopRecording_FromRecording_ReturnsToPrepared_AndClearsArmed()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        Assert.IsTrue(sut.TryStartRecording(out _));
        Assert.IsTrue(sut.TryStopRecording(out _));
        Assert.AreEqual(MultitrackRecordingSessionPhase.Prepared, sut.Phase);
        Assert.AreEqual(0, sut.ArmedTrackIds.Count);
        Assert.AreEqual(0, sut.TrackInputAssignments.Count);
    }

    [TestMethod]
    public void CancelSession_ResetsToNone()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        sut.CancelSession();
        Assert.AreEqual(MultitrackRecordingSessionPhase.None, sut.Phase);
        Assert.IsNull(sut.ActiveSessionId);
    }

    [TestMethod]
    public void BindProject_WhenProjectChanges_ResetsSession()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p1");
        Assert.IsTrue(sut.TryCreateSession(out _));
        sut.BindProject("p2");
        Assert.AreEqual(MultitrackRecordingSessionPhase.None, sut.Phase);
        Assert.AreEqual("p2", sut.BoundProjectId);
    }

    [TestMethod]
    public void GetStatus_IncludesTrackInputAssignments()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack("t1", In1, out _));
        var status = sut.GetStatus();
        Assert.AreEqual(1, status.TrackInputAssignments.Count);
        Assert.AreEqual(In1, status.TrackInputAssignments["t1"]);
    }

    [TestMethod]
    public void LifecycleGate_StuckRecording_AllowsPrepareAndStartAgain()
    {
        var sut = new RecordingSessionCoordinator();
        sut.BindProject("p");
        Assert.IsTrue(sut.TryCreateSession(out _));
        Assert.IsTrue(sut.TryArmTrack(RecordingSessionSlice1Defaults.PrimaryInputTrackId, In1, out _));
        Assert.IsTrue(sut.TryStartRecording(out _));
        Assert.AreEqual(MultitrackRecordingSessionPhase.Recording, sut.Phase);

        Assert.IsTrue(RecordingSessionLifecycleGate.TryPrepareAndStartRecording(
            sut,
            "p",
            RecordingSessionSlice1Defaults.PrimaryInputTrackId,
            In1,
            out var err),
            err);
        Assert.AreEqual(MultitrackRecordingSessionPhase.Recording, sut.Phase);
    }
}
