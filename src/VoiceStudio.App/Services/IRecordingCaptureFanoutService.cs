using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Multitrack capture execution (GAP-042 Slice 3). Validates all legs before capture; fail-fast on leg error.
/// </summary>
public interface IRecordingCaptureFanoutService
{
  bool IsActive { get; }

  TimeSpan MaxLegDuration { get; }

  float AggregatePeakLevel { get; }

  event EventHandler<float>? AggregateLevelChanged;

  event EventHandler<RecordingCaptureFaultedEventArgs>? CaptureSessionFaulted;

  Task<RecordingCaptureValidationResult> ValidateAndBuildPlanAsync(
      IReadOnlyDictionary<string, string> trackInputAssignments,
      int sampleRate,
      int channels,
      string? filenameStem,
      CancellationToken cancellationToken);

  Task<RecordingCaptureLegStartResult> StartLegsAsync(
      RecordingCaptureValidationResult plan,
      int sampleRate,
      int channels,
      CancellationToken cancellationToken);

  Task<RecordingCaptureStopResult> StopAllAsync(CancellationToken cancellationToken);

  Task CancelAllAsync();
}
