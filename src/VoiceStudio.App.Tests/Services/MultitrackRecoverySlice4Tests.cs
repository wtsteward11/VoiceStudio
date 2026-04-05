using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MultitrackRecoverySlice4Tests
{
    [TestMethod]
    public void ShouldPersistForRecovery_AllLegsSucceededAndSessionClean_ReturnsFalse()
    {
        var stop = new RecordingCaptureStopResult
        {
            SessionFaulted = false,
            Legs = new[]
            {
                new RecordingCaptureLegOutcome { TrackId = "a", CompletedSuccessfully = true },
            },
        };
        Assert.IsFalse(MultitrackRecoveryPayloadBuilder.ShouldPersistForRecovery(stop, endedCleanly: true));
    }

    [TestMethod]
    public void ShouldPersistForRecovery_AnyFailedLeg_ReturnsTrue()
    {
        var stop = new RecordingCaptureStopResult
        {
            SessionFaulted = false,
            Legs = new[]
            {
                new RecordingCaptureLegOutcome { TrackId = "a", CompletedSuccessfully = true },
                new RecordingCaptureLegOutcome { TrackId = "b", CompletedSuccessfully = false, ErrorMessage = "x" },
            },
        };
        Assert.IsTrue(MultitrackRecoveryPayloadBuilder.ShouldPersistForRecovery(stop, endedCleanly: true));
    }

    [TestMethod]
    public void ShouldPersistForRecovery_Fault_ReturnsTrueEvenIfEndedCleanly()
    {
        var stop = new RecordingCaptureStopResult
        {
            SessionFaulted = true,
            Legs = Array.Empty<RecordingCaptureLegOutcome>(),
        };
        Assert.IsTrue(MultitrackRecoveryPayloadBuilder.ShouldPersistForRecovery(stop, endedCleanly: true));
    }

    [TestMethod]
    public void PayloadBuilder_MapsAssignmentsAndStatuses()
    {
        var wav = Path.Combine(Path.GetTempPath(), $"vs_recv_{Guid.NewGuid():N}.wav");
        File.WriteAllText(wav, "x");
        try
        {
            var stop = new RecordingCaptureStopResult
            {
                SessionFaulted = true,
                Legs = new[]
                {
                    new RecordingCaptureLegOutcome
                    {
                        TrackId = "t1",
                        LocalPath = wav,
                        CompletedSuccessfully = true,
                    },
                    new RecordingCaptureLegOutcome
                    {
                        TrackId = "t2",
                        CompletedSuccessfully = false,
                        ErrorMessage = "boom",
                    },
                },
            };
            var assignments = new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "in1", ["t2"] = "in2" };
            var payload = MultitrackRecoveryPayloadBuilder.Build("proj-1", Guid.NewGuid(), assignments, stop, endedCleanly: false);
            Assert.AreEqual("proj-1", payload.ProjectId);
            Assert.AreEqual(2, payload.Legs.Count);
            Assert.AreEqual(MultitrackRecoveryLegStatus.Completed, payload.Legs[0].Status);
            Assert.AreEqual("in1", payload.Legs[0].InputSourceId);
            Assert.AreEqual(wav, payload.Legs[0].PreservedOutputPath);
            Assert.AreEqual(MultitrackRecoveryLegStatus.Failed, payload.Legs[1].Status);
            Assert.AreEqual("boom", payload.Legs[1].FailureMessage);
        }
        finally
        {
            try
            {
                File.Delete(wav);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"MultitrackRecoverySlice4Tests cleanup: {ex.Message}");
            }
        }
    }

    [TestMethod]
    public void PayloadJson_RoundTripsThroughSerializeDeserialize()
    {
        var payload = new MultitrackRecoveryPayload
        {
            ProjectId = "p",
            SessionId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow.ToString("O"),
            EndedCleanly = false,
            Legs = new[]
            {
                new MultitrackRecoveryLegRecord
                {
                    TrackId = "a",
                    InputSourceId = "i",
                    Status = MultitrackRecoveryLegStatus.Completed,
                    PreservedOutputPath = "/tmp/x.wav",
                },
            },
        };
        var json = MultitrackRecoveryPayloadJson.Serialize(payload);
        Assert.IsTrue(MultitrackRecoveryPayloadJson.TryDeserialize(json, out var back));
        Assert.IsNotNull(back);
        Assert.AreEqual("p", back!.ProjectId);
        Assert.AreEqual(1, back.Legs.Count);
        Assert.AreEqual(MultitrackRecoveryLegStatus.Completed, back.Legs[0].Status);
    }

    [TestMethod]
    public void MultitrackRecoveryStateService_HasPendingPayload_RequiresEndedCleanlyFalse()
    {
        var svc = new MultitrackRecoveryStateService(new CrashRecoveryService());
        var ended = new SessionState();
        ended.CustomState[MultitrackRecoveryKeys.PayloadV1] = MultitrackRecoveryPayloadJson.Serialize(
            new MultitrackRecoveryPayload
            {
                ProjectId = "x",
                SessionId = "s",
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                EndedCleanly = true,
                Legs = Array.Empty<MultitrackRecoveryLegRecord>(),
            });
        Assert.IsFalse(svc.HasPendingPayload(ended));

        var pending = new SessionState();
        pending.CustomState[MultitrackRecoveryKeys.PayloadV1] = MultitrackRecoveryPayloadJson.Serialize(
            new MultitrackRecoveryPayload
            {
                ProjectId = "x",
                SessionId = "s",
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                EndedCleanly = false,
                Legs = Array.Empty<MultitrackRecoveryLegRecord>(),
            });
        Assert.IsTrue(svc.HasPendingPayload(pending));
    }

    [TestMethod]
    public void MultitrackRecoveryStateService_DeletePreservedLegFiles_DeletesCompletedPaths()
    {
        var f = Path.Combine(Path.GetTempPath(), $"vs_mtr_del_{Guid.NewGuid():N}.wav");
        File.WriteAllText(f, "data");
        var svc = new MultitrackRecoveryStateService(new CrashRecoveryService());
        var payload = new MultitrackRecoveryPayload
        {
            EndedCleanly = false,
            Legs = new[]
            {
                new MultitrackRecoveryLegRecord
                {
                    TrackId = "a",
                    Status = MultitrackRecoveryLegStatus.Completed,
                    PreservedOutputPath = f,
                },
            },
        };
        svc.DeletePreservedLegFiles(payload);
        Assert.IsFalse(File.Exists(f));
    }

    [TestMethod]
    public async Task MultitrackRecoveryApplyService_ProjectMismatch_ReturnsFailure()
    {
        var mockRec = new Mock<IRecordingClient>();
        var mockProj = new Mock<IProjectAudioClient>();
        var sut = new MultitrackRecoveryApplyService(mockRec.Object, mockProj.Object);
        var payload = new MultitrackRecoveryPayload
        {
            ProjectId = "expected",
            SessionId = "s",
            CreatedAtUtc = DateTime.UtcNow.ToString("O"),
            EndedCleanly = false,
            Legs = Array.Empty<MultitrackRecoveryLegRecord>(),
        };
        var r = await sut.TryRestoreCompletedTakesAsync("other", payload, CancellationToken.None).ConfigureAwait(false);
        Assert.IsFalse(r.Success);
        StringAssert.Contains(r.ErrorMessage, "does not match");
        mockRec.Verify(x => x.UploadAudioFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
