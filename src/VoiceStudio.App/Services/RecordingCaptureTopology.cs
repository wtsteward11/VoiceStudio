using System;
using NAudio.Wave;

namespace VoiceStudio.App.Services;

/// <summary>
/// Local WaveIn capability fingerprint for hotplug detection without extra HTTP calls (GAP-035).
/// </summary>
public static class RecordingCaptureTopology
{
  public static int GetWaveInCapabilitySignature()
  {
    unchecked
    {
      var hash = WaveInEvent.DeviceCount * 397;
      for (var i = 0; i < WaveInEvent.DeviceCount; i++)
      {
        var caps = WaveInEvent.GetCapabilities(i);
        hash ^= StringComparer.OrdinalIgnoreCase.GetHashCode(caps.ProductName.Trim()) * (i + 1);
      }

      return hash;
    }
  }
}
