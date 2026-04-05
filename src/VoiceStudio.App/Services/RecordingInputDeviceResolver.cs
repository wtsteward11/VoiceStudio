using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Maps backend <see cref="RecordingDevice.Id"/> (and display name) to NAudio WaveIn device numbers (GAP-035).
/// </summary>
public static class RecordingInputDeviceResolver
{
  public const string DefaultInputSourceId = "default";

  /// <summary>Backward-compatible overload: fetches devices from <paramref name="recordingClient"/>.</summary>
  public static Task<(bool Ok, int WaveInDeviceNumber, string? ErrorMessage)> TryResolveAsync(
      IRecordingClient recordingClient,
      string inputSourceId,
      CancellationToken cancellationToken) =>
      TryResolveAsync(recordingClient, null, inputSourceId, cancellationToken);

  /// <summary>
  /// Resolves capture input. When <paramref name="availability"/> is non-null, uses its snapshot (caller should <see cref="IRecordingDeviceAvailabilityService.RefreshAsync"/> first for hot paths).
  /// </summary>
  public static async Task<(bool Ok, int WaveInDeviceNumber, string? ErrorMessage)> TryResolveAsync(
      IRecordingClient recordingClient,
      IRecordingDeviceAvailabilityService? availability,
      string inputSourceId,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(recordingClient);
    if (string.IsNullOrWhiteSpace(inputSourceId))
      return (false, 0, "Input source id required.");

    IReadOnlyList<RecordingDevice> devices;
    if (availability != null)
    {
      devices = availability.GetSnapshot();
      if (devices.Count == 0)
      {
        await availability.RefreshAsync(cancellationToken).ConfigureAwait(false);
        devices = availability.GetSnapshot();
      }
    }
    else
    {
      var resp = await recordingClient.GetRecordingDevicesAsync(cancellationToken).ConfigureAwait(false);
      devices = resp?.Devices ?? Array.Empty<RecordingDevice>();
    }

    if (IsDefaultToken(inputSourceId))
      return TryResolveDefaultCapture(devices);

    var match = devices.FirstOrDefault(d => string.Equals(d.Id, inputSourceId, StringComparison.Ordinal));
    if (match == null)
      return (false, 0, "Unknown or unavailable input device id. Open the Recording panel and refresh devices.");

    return TryResolveToWaveInDeviceNumber(match);
  }

  public static bool IsDefaultToken(string inputSourceId) =>
      string.Equals(inputSourceId.Trim(), DefaultInputSourceId, StringComparison.OrdinalIgnoreCase);

  public static (bool Ok, int WaveInDeviceNumber, string? ErrorMessage) TryResolveToWaveInDeviceNumber(RecordingDevice device)
  {
    ArgumentNullException.ThrowIfNull(device);
    if (WaveInEvent.DeviceCount == 0)
      return (false, 0, "No audio input devices found.");

    if (IsDefaultToken(device.Id))
      return TryResolveDefaultCapture(Array.Empty<RecordingDevice>());

    var trimmedName = device.Name.Trim();
    var matches = FindWaveInIndicesByProductName(trimmedName);
    if (matches.Count == 1)
      return (true, matches[0], null);

    if (matches.Count > 1)
    {
      return (false, 0,
          $"Multiple microphones match name '{device.Name}'. Unplug duplicates or use a device with a unique name.");
    }

    if (int.TryParse(device.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
        && idx >= 0
        && idx < WaveInEvent.DeviceCount)
    {
      var caps = WaveInEvent.GetCapabilities(idx);
      if (string.Equals(caps.ProductName.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase))
        return (true, idx, null);

      return (false, 0,
          $"Device id {device.Id} does not match WaveIn device {idx} name (device list may have changed).");
    }

    return (false, 0, $"No NAudio input device matches '{device.Name}' (id {device.Id}).");
  }

  private static (bool Ok, int WaveInDeviceNumber, string? ErrorMessage) TryResolveDefaultCapture(
      IReadOnlyList<RecordingDevice> backendSnapshot)
  {
    if (WaveInEvent.DeviceCount == 0)
      return (false, 0, "No audio input devices found.");

    try
    {
      using var enumerator = new MMDeviceEnumerator();
      MMDevice def;
      try
      {
        def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
      }
      catch
      {
        def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
      }

      var friendly = def.FriendlyName.Trim();
      var backendDup = backendSnapshot.Count(d => string.Equals(d.Name.Trim(), friendly, StringComparison.OrdinalIgnoreCase));
      if (backendDup > 1)
      {
        return (false, 0, "Ambiguous default microphone: multiple backend entries share the default device name.");
      }

      var indices = FindWaveInIndicesByProductName(friendly);
      if (indices.Count == 1)
        return (true, indices[0], null);

      if (indices.Count > 1)
      {
        return (false, 0, "Default capture device maps to multiple WaveIn entries; choose an explicit device in the Recording panel.");
      }

      return (false, 0, "Default capture device could not be matched to a WaveIn capture device.");
    }
    catch (Exception ex)
    {
      return (false, 0, $"Default capture device is not available ({ex.Message}).");
    }
  }

  private static List<int> FindWaveInIndicesByProductName(string trimmedProductName)
  {
    var list = new List<int>();
    for (var i = 0; i < WaveInEvent.DeviceCount; i++)
    {
      var caps = WaveInEvent.GetCapabilities(i);
      if (string.Equals(caps.ProductName.Trim(), trimmedProductName, StringComparison.OrdinalIgnoreCase))
        list.Add(i);
    }

    return list;
  }
}
