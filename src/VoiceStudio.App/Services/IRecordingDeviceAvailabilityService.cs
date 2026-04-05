using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace VoiceStudio.App.Services;

/// <summary>
/// Canonical app-level snapshot of backend-listed capture devices plus WaveIn enumeration fingerprint (GAP-035).
/// Raises <see cref="InputDevicesChanged"/> when listing or local capture topology likely changed.
/// </summary>
public interface IRecordingDeviceAvailabilityService
{
  /// <summary>Raised after <see cref="RefreshAsync"/> detects a meaningful device/topology change.</summary>
  event EventHandler? InputDevicesChanged;

  /// <summary>Refreshes backend device list and WaveIn signature; may raise <see cref="InputDevicesChanged"/>.</summary>
  Task RefreshAsync(CancellationToken cancellationToken = default);

  /// <summary>Thread-safe copy of the last successful refresh (may be empty).</summary>
  IReadOnlyList<RecordingDevice> GetSnapshot();

  /// <summary>True when <paramref name="inputSourceId"/> exists in the last snapshot (backend id catalogue only).</summary>
  bool IsBackendDeviceIdListed(string inputSourceId);
}
