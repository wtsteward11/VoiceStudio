using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceStudio.App.Services;

/// <summary>CustomState key for GAP-042 Slice 4 multitrack recovery JSON blob.</summary>
public static class MultitrackRecoveryKeys
{
  public const string PayloadV1 = "recording.multitrackRecovery.v1";

  public const int CurrentSchemaVersion = 1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MultitrackRecoveryLegStatus
{
  Completed,
  Failed,
  Missing,
}

/// <summary>One leg in a multitrack recovery snapshot (serializable).</summary>
public sealed class MultitrackRecoveryLegRecord
{
  public string TrackId { get; init; } = string.Empty;

  public string InputSourceId { get; init; } = string.Empty;

  public MultitrackRecoveryLegStatus Status { get; init; }

  public string? PreservedOutputPath { get; init; }

  public string? FailureMessage { get; init; }
}

/// <summary>Project-scoped multitrack recording recovery payload (stored in crash session custom state).</summary>
public sealed class MultitrackRecoveryPayload
{
  public int SchemaVersion { get; init; } = MultitrackRecoveryKeys.CurrentSchemaVersion;

  public string? ProjectId { get; init; }

  public string SessionId { get; init; } = string.Empty;

  public string CreatedAtUtc { get; init; } = string.Empty;

  public bool EndedCleanly { get; init; }

  public IReadOnlyList<MultitrackRecoveryLegRecord> Legs { get; init; } = Array.Empty<MultitrackRecoveryLegRecord>();

  public int SuccessCount => CountByStatus(MultitrackRecoveryLegStatus.Completed);

  public int FailedCount => CountByStatus(MultitrackRecoveryLegStatus.Failed)
      + CountByStatus(MultitrackRecoveryLegStatus.Missing);

  private int CountByStatus(MultitrackRecoveryLegStatus s)
  {
    var n = 0;
    foreach (var leg in Legs)
    {
      if (leg.Status == s)
        n++;
    }

    return n;
  }
}

/// <summary>JSON helpers for round-tripping the payload through <see cref="CrashRecoveryService.SessionState.CustomState"/>.</summary>
public static class MultitrackRecoveryPayloadJson
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
  };

  public static string Serialize(MultitrackRecoveryPayload payload) =>
      JsonSerializer.Serialize(payload, Options);

  public static bool TryDeserialize(string? json, out MultitrackRecoveryPayload? payload)
  {
    payload = null;
    if (string.IsNullOrWhiteSpace(json))
      return false;
    try
    {
      payload = JsonSerializer.Deserialize<MultitrackRecoveryPayload>(json, Options);
      return payload != null && payload.SchemaVersion == MultitrackRecoveryKeys.CurrentSchemaVersion;
    }
    catch (JsonException)
    {
      return false;
    }
  }

  /// <summary>Reads from <see cref="CrashRecoveryService.SessionState.CustomState"/> raw values (string or JsonElement).</summary>
  public static bool TryParseFromCustomStateValue(object? raw, out MultitrackRecoveryPayload? payload)
  {
    payload = null;
    switch (raw)
    {
      case string s:
        return TryDeserialize(s, out payload);
      case System.Text.Json.JsonElement el when el.ValueKind == JsonValueKind.String:
        return TryDeserialize(el.GetString(), out payload);
      case System.Text.Json.JsonElement el when el.ValueKind == JsonValueKind.Object:
        try
        {
          var p = JsonSerializer.Deserialize<MultitrackRecoveryPayload>(el.GetRawText(), Options);
          if (p != null && p.SchemaVersion == MultitrackRecoveryKeys.CurrentSchemaVersion)
          {
            payload = p;
            return true;
          }

          return false;
        }
        catch (JsonException)
        {
          return false;
        }
      default:
        return false;
    }
  }
}

public sealed class RecordingCaptureFaultedEventArgs : EventArgs
{
  public RecordingCaptureFaultedEventArgs(string message, RecordingCaptureStopResult stopResult)
  {
    Message = message;
    StopResult = stopResult;
  }

  public string Message { get; }

  public RecordingCaptureStopResult StopResult { get; }
}

/// <summary>Builds <see cref="MultitrackRecoveryPayload"/> from fan-out outcomes + coordinator assignment map.</summary>
public static class MultitrackRecoveryPayloadBuilder
{
  public static MultitrackRecoveryPayload Build(
      string? projectId,
      Guid? sessionId,
      IReadOnlyDictionary<string, string> assignments,
      RecordingCaptureStopResult stopResult,
      bool endedCleanly)
  {
    var legs = new List<MultitrackRecoveryLegRecord>();
    foreach (var o in stopResult.Legs)
    {
      _ = assignments.TryGetValue(o.TrackId, out var inputId);
      MultitrackRecoveryLegStatus st;
      if (o.CompletedSuccessfully && !string.IsNullOrWhiteSpace(o.LocalPath) && File.Exists(o.LocalPath))
        st = MultitrackRecoveryLegStatus.Completed;
      else if (o.CompletedSuccessfully)
        st = MultitrackRecoveryLegStatus.Missing;
      else
        st = MultitrackRecoveryLegStatus.Failed;

      legs.Add(new MultitrackRecoveryLegRecord
      {
        TrackId = o.TrackId,
        InputSourceId = inputId ?? string.Empty,
        Status = st,
        PreservedOutputPath = st == MultitrackRecoveryLegStatus.Completed ? o.LocalPath : null,
        FailureMessage = o.ErrorMessage,
      });
    }

    return new MultitrackRecoveryPayload
    {
      SchemaVersion = MultitrackRecoveryKeys.CurrentSchemaVersion,
      ProjectId = projectId,
      SessionId = sessionId?.ToString() ?? string.Empty,
      CreatedAtUtc = DateTime.UtcNow.ToString("O"),
      EndedCleanly = endedCleanly,
      Legs = legs,
    };
  }

  public static bool ShouldPersistForRecovery(RecordingCaptureStopResult stopResult, bool endedCleanly)
  {
    if (!endedCleanly)
      return true;
    if (stopResult.SessionFaulted)
      return true;
    foreach (var leg in stopResult.Legs)
    {
      if (!leg.CompletedSuccessfully)
        return true;
    }

    return false;
  }
}

/// <summary>GAP-035: operator copy so recovery restore does not imply capture can resume without hardware.</summary>
public static class MultitrackRecoveryOperatorCopy
{
  public static string? ContinuationGuidanceAfterRestore(MultitrackRecoveryPayload? payload)
  {
    if (payload == null)
      return null;
    if (payload.FailedCount == 0)
      return null;
    return "Some tracks did not finish recording. Starting new capture requires microphones available and selected in the Recording panel.";
  }
}
