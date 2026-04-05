using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Maintains a backend + WaveIn fingerprint and notifies consumers on churn (GAP-035).
/// Active capture also polls <see cref="RecordingCaptureTopology"/> from <see cref="RecordingCaptureFanoutService"/>.
/// </summary>
public sealed class RecordingDeviceAvailabilityService : IRecordingDeviceAvailabilityService
{
  private readonly IRecordingClient _recordingClient;
  private readonly object _sync = new();
  private List<RecordingDevice> _snapshot = new();
  private int _waveInSignature;

  public RecordingDeviceAvailabilityService(IRecordingClient recordingClient)
  {
    _recordingClient = recordingClient ?? throw new ArgumentNullException(nameof(recordingClient));
  }

  public event EventHandler? InputDevicesChanged;

  public async Task RefreshAsync(CancellationToken cancellationToken = default)
  {
    var resp = await _recordingClient.GetRecordingDevicesAsync(cancellationToken).ConfigureAwait(false);
    var next = resp?.Devices?.ToList() ?? new List<RecordingDevice>();
    var sig = RecordingCaptureTopology.GetWaveInCapabilitySignature();

    bool fire;
    lock (_sync)
    {
      fire = !SnapshotSequenceEqual(_snapshot, next) || sig != _waveInSignature;
      _snapshot = next;
      _waveInSignature = sig;
    }

    if (fire)
      InputDevicesChanged?.Invoke(this, EventArgs.Empty);
  }

  public IReadOnlyList<RecordingDevice> GetSnapshot()
  {
    lock (_sync)
      return _snapshot.ToList();
  }

  public bool IsBackendDeviceIdListed(string inputSourceId)
  {
    if (string.IsNullOrWhiteSpace(inputSourceId))
      return false;

    lock (_sync)
      return _snapshot.Exists(d => string.Equals(d.Id, inputSourceId, StringComparison.Ordinal));
  }

  private static bool SnapshotSequenceEqual(IReadOnlyList<RecordingDevice> a, IReadOnlyList<RecordingDevice> b)
  {
    var sa = a.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    var sb = b.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    if (sa.Count != sb.Count)
      return false;
    for (var i = 0; i < sa.Count; i++)
    {
      if (!string.Equals(sa[i].Id, sb[i].Id, StringComparison.Ordinal)
          || !string.Equals(sa[i].Name, sb[i].Name, StringComparison.Ordinal))
        return false;
    }

    return true;
  }
}
