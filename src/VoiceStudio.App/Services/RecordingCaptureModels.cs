using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Services;

public sealed class RecordingCaptureValidationResult
{
  public bool Success { get; init; }

  public string? ErrorMessage { get; init; }

  public IReadOnlyList<RecordingCaptureLegPlan> Legs { get; init; } = Array.Empty<RecordingCaptureLegPlan>();
}

public sealed class RecordingCaptureLegPlan
{
  public string TrackId { get; init; } = string.Empty;

  public string InputSourceId { get; init; } = string.Empty;

  public int WaveInDeviceNumber { get; init; }

  public string OutputPath { get; init; } = string.Empty;
}

public sealed class RecordingCaptureLegStartResult
{
  public bool Success { get; init; }

  public string? ErrorMessage { get; init; }
}

public sealed class RecordingCaptureStopResult
{
  public bool SessionFaulted { get; init; }

  public IReadOnlyList<RecordingCaptureLegOutcome> Legs { get; init; } = Array.Empty<RecordingCaptureLegOutcome>();
}

public sealed class RecordingCaptureLegOutcome
{
  public string TrackId { get; init; } = string.Empty;

  public string? LocalPath { get; init; }

  public bool CompletedSuccessfully { get; init; }

  public string? ErrorMessage { get; init; }
}
