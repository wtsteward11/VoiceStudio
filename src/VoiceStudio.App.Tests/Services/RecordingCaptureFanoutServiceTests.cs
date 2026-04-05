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
public sealed class RecordingCaptureFanoutServiceTests
{
    private sealed class StubLeg : IRecordingCaptureLeg
    {
        public string TrackId { get; private set; } = string.Empty;

        public bool IsRecording { get; private set; }

        public TimeSpan Duration => TimeSpan.Zero;

        public float CurrentLevel => 0f;

        public event EventHandler<float>? LevelChanged;

        public event EventHandler<string>? Error;

        public bool FailStart { get; set; }

        public string OutputPath { get; set; } = string.Empty;

        public int StartCount { get; private set; }

        public Task StartAsync(
            string trackId,
            string outputPath,
            int sampleRate,
            int channels,
            int waveInDeviceNumber,
            CancellationToken cancellationToken)
        {
            if (FailStart)
                throw new InvalidOperationException("simulated start failure");
            TrackId = trackId;
            OutputPath = outputPath;
            StartCount++;
            IsRecording = true;
            return Task.CompletedTask;
        }

        public Task<string> StopRecordingAsync()
        {
            IsRecording = false;
            return Task.FromResult(OutputPath);
        }

        public void Dispose()
        {
        }

        public void RaiseError(string message) => Error?.Invoke(this, message);
    }

    private static RecordingCaptureFanoutService CreateSut(
        Func<IRecordingCaptureLeg> legs,
        Func<string, CancellationToken, Task<(bool Ok, int WaveInDeviceNumber, string? ErrorMessage)>> resolve)
    {
        var mockClient = new Mock<IRecordingClient>();
        return new RecordingCaptureFanoutService(mockClient.Object, legs, resolve);
    }

    [TestMethod]
    public async Task ValidateAndBuildPlan_TwoAssignments_ProducesTwoLegs()
    {
        var sut = CreateSut(
            () => new StubLeg(),
            static (id, _) => Task.FromResult((true, id == "in-a" ? 0 : 1, (string?)null)));

        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["track-a"] = "in-a",
            ["track-b"] = "in-b",
        };
        var plan = await sut.ValidateAndBuildPlanAsync(map, 44100, 1, "take", CancellationToken.None);
        Assert.IsTrue(plan.Success, plan.ErrorMessage);
        Assert.AreEqual(2, plan.Legs.Count);
    }

    [TestMethod]
    public async Task ValidateAndBuildPlan_OneInvalidInput_BlocksAll()
    {
        var sut = CreateSut(
            () => new StubLeg(),
            static (id, _) =>

                Task.FromResult(id == "bad"
                    ? (false, 0, "no device")
                    : (true, 0, (string?)null)));

        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["t1"] = "ok",
            ["t2"] = "bad",
        };
        var plan = await sut.ValidateAndBuildPlanAsync(map, 44100, 1, null, CancellationToken.None);
        Assert.IsFalse(plan.Success);
        StringAssert.Contains(plan.ErrorMessage, "t2");
    }

    [TestMethod]
    public async Task StartLegs_TwoLegs_StartsBoth()
    {
        StubLeg? legA = null;
        StubLeg? legB = null;
        var sut = CreateSut(
            () =>
            {
                var l = new StubLeg();
                if (legA == null)
                    legA = l;
                else
                    legB = l;
                return l;
            },
            static (_, _) => Task.FromResult((true, 0, (string?)null)));

        var plan = await sut.ValidateAndBuildPlanAsync(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "x", ["b"] = "y" },
                44100,
                1,
                null,
                CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsTrue(plan.Success);
        var started = await sut.StartLegsAsync(plan, 44100, 1, CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(started.Success, started.ErrorMessage);
        Assert.IsNotNull(legA);
        Assert.IsNotNull(legB);
        Assert.AreEqual(1, legA.StartCount);
        Assert.AreEqual(1, legB.StartCount);
        await sut.StopAllAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task StartLegs_SecondLegThrows_StartReturnsFailure_AndDoesNotLeaveActiveRecording()
    {
        var n = 0;
        var sut = CreateSut(
            () =>
            {
                n++;
                return new StubLeg { FailStart = n >= 2 };
            },
            static (_, _) => Task.FromResult((true, 0, (string?)null)));

        var plan = await sut.ValidateAndBuildPlanAsync(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "x", ["b"] = "y" },
                44100,
                1,
                null,
                CancellationToken.None)
            .ConfigureAwait(false);
        var started = await sut.StartLegsAsync(plan, 44100, 1, CancellationToken.None).ConfigureAwait(false);
        Assert.IsFalse(started.Success);
        Assert.IsFalse(sut.IsActive);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task LegError_RaisesCaptureSessionFaulted_WithStopResult_AndClearsActive()
    {
        StubLeg? legA = null;
        var sut = CreateSut(
            () =>
            {
                var l = new StubLeg();
                legA ??= l;
                return l;
            },
            static (_, _) => Task.FromResult((true, 0, (string?)null)));

        RecordingCaptureFaultedEventArgs? args = null;
        var raised = new TaskCompletionSource<bool>();
        sut.CaptureSessionFaulted += (_, e) =>
        {
            args = e;
            raised.TrySetResult(true);
        };

        var plan = await sut.ValidateAndBuildPlanAsync(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "x" },
                44100,
                1,
                "ut",
                CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsTrue(plan.Success);
        var started = await sut.StartLegsAsync(plan, 44100, 1, CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(started.Success, started.ErrorMessage);
        Assert.IsNotNull(legA);
        var wav = Path.Combine(Path.GetTempPath(), $"vs_fanout_fault_{Guid.NewGuid():N}.wav");
        await File.WriteAllTextAsync(wav, "x").ConfigureAwait(false);
        legA.OutputPath = wav;

        legA.RaiseError("device lost");

        await raised.Task.ConfigureAwait(false);
        Assert.IsNotNull(args);
        StringAssert.Contains(args!.Message, "device lost");
        Assert.IsTrue(args.StopResult.SessionFaulted);
        Assert.AreEqual(1, args.StopResult.Legs.Count);
        Assert.AreEqual("a", args.StopResult.Legs[0].TrackId);
        Assert.IsFalse(sut.IsActive);
        try
        {
            File.Delete(wav);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"RecordingCaptureFanoutServiceTests cleanup: {ex.Message}");
        }
    }
}
