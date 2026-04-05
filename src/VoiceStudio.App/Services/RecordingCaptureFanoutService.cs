using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Fan-out capture: one <see cref="IRecordingCaptureLeg"/> per armed assignment (GAP-042 Slice 3).
/// </summary>
public sealed class RecordingCaptureFanoutService : IRecordingCaptureFanoutService, IDisposable
{
  private readonly IRecordingClient _recordingClient;
  private readonly IRecordingDeviceAvailabilityService? _deviceAvailability;
  private readonly Func<IRecordingCaptureLeg> _legFactory;
  private readonly Func<string, CancellationToken, Task<(bool Ok, int WaveInDeviceNumber, string? ErrorMessage)>> _resolveDeviceAsync;
  private readonly object _sync = new();
  private readonly SemaphoreSlim _sessionGate = new(1, 1);
  private readonly List<ActiveLeg> _active = new();
  private bool _sessionFaulted;
  private volatile float _aggregatePeak;

  /// <summary>Production: resolves via <see cref="RecordingInputDeviceResolver"/> + availability churn (GAP-035).</summary>
  public RecordingCaptureFanoutService(
      IRecordingClient recordingClient,
      IRecordingDeviceAvailabilityService deviceAvailability)
      : this(
          recordingClient,
          () => new MicrophoneRecordingCaptureLeg(new MicrophoneRecordingService()),
          null,
          deviceAvailability ?? throw new ArgumentNullException(nameof(deviceAvailability)))
  {
  }

  /// <summary>
  /// Unit-test constructor: injectable leg factory and optional device resolution (skips NAudio when set).
  /// </summary>
  public RecordingCaptureFanoutService(
      IRecordingClient recordingClient,
      Func<IRecordingCaptureLeg> legFactory,
      Func<string, CancellationToken, Task<(bool Ok, int WaveInDeviceNumber, string? ErrorMessage)>>? resolveDeviceAsync)
      : this(recordingClient, legFactory, resolveDeviceAsync, null)
  {
  }

  private RecordingCaptureFanoutService(
      IRecordingClient recordingClient,
      Func<IRecordingCaptureLeg> legFactory,
      Func<string, CancellationToken, Task<(bool Ok, int WaveInDeviceNumber, string? ErrorMessage)>>? resolveDeviceAsync,
      IRecordingDeviceAvailabilityService? deviceAvailability)
  {
    _recordingClient = recordingClient ?? throw new ArgumentNullException(nameof(recordingClient));
    _legFactory = legFactory ?? throw new ArgumentNullException(nameof(legFactory));
    _deviceAvailability = deviceAvailability;
    _resolveDeviceAsync = resolveDeviceAsync
        ?? ((id, ct) => RecordingInputDeviceResolver.TryResolveAsync(_recordingClient, _deviceAvailability, id, ct));

  }

  public void Dispose()
  {
    StopActiveCaptureTopologyWatch();
  }

  private Timer? _topologyTimer;
  private int _lastWaveInSignature = int.MinValue;

  private void StartActiveCaptureTopologyWatch()
  {
    StopActiveCaptureTopologyWatch();
    _lastWaveInSignature = RecordingCaptureTopology.GetWaveInCapabilitySignature();
    _topologyTimer = new Timer(_ => TopologyTimerTick(), null, 400, 400);
  }

  private void StopActiveCaptureTopologyWatch()
  {
    _topologyTimer?.Dispose();
    _topologyTimer = null;
    _lastWaveInSignature = int.MinValue;
  }

  private void TopologyTimerTick()
  {
    try
    {
      var sig = RecordingCaptureTopology.GetWaveInCapabilitySignature();
      if (sig == _lastWaveInSignature)
        return;
      _lastWaveInSignature = sig;
      if (!IsActive)
        return;
      _ = RevalidateActiveCaptureBecauseDevicesChangedAsync();
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[RecordingCaptureFanoutService] Topology poll: {ex.Message}");
    }
  }

  private async Task RevalidateActiveCaptureBecauseDevicesChangedAsync()
  {
    List<(string TrackId, string InputSourceId)> snapshot;
    lock (_sync)
      snapshot = _active.Select(a => (a.TrackId, a.InputSourceId)).ToList();

    foreach (var leg in snapshot)
    {
      var resolved = await _resolveDeviceAsync(leg.InputSourceId, CancellationToken.None).ConfigureAwait(false);
      if (!resolved.Ok)
      {
        await DrainAndRaiseFaultAsync(
                $"Capture device became unavailable for track '{leg.TrackId}': {resolved.ErrorMessage}")
            .ConfigureAwait(false);
        return;
      }
    }
  }

  public bool IsActive
  {
    get
    {
      lock (_sync)
        return _active.Count > 0 && _active.Any(a => a.Leg.IsRecording);
    }
  }

  public TimeSpan MaxLegDuration
  {
    get
    {
      lock (_sync)
      {
        if (_active.Count == 0)
          return TimeSpan.Zero;
        return _active.Max(a => a.Leg.Duration);
      }
    }
  }

  public float AggregatePeakLevel => _aggregatePeak;

  public event EventHandler<float>? AggregateLevelChanged;

  public event EventHandler<RecordingCaptureFaultedEventArgs>? CaptureSessionFaulted;

  public async Task<RecordingCaptureValidationResult> ValidateAndBuildPlanAsync(
      IReadOnlyDictionary<string, string> trackInputAssignments,
      int sampleRate,
      int channels,
      string? filenameStem,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(trackInputAssignments);
    if (trackInputAssignments.Count == 0)
      return new RecordingCaptureValidationResult { Success = false, ErrorMessage = "No armed tracks to capture." };

    var legs = new List<RecordingCaptureLegPlan>();
    foreach (var kv in trackInputAssignments.OrderBy(k => k.Key, StringComparer.Ordinal))
    {
      var resolved = await _resolveDeviceAsync(kv.Value, cancellationToken).ConfigureAwait(false);
      var (ok, deviceNumber, err) = resolved;
      if (!ok)
      {
        return new RecordingCaptureValidationResult
        {
          Success = false,
          ErrorMessage = $"Cannot arm capture for track '{kv.Key}': {err}",
        };
      }

      var tempDir = Path.GetTempPath();
      var stamp = $"{filenameStem ?? "take"}_{kv.Key}_{Guid.NewGuid():N}".Replace('\\', '_').Replace('/', '_');
      var path = Path.Combine(tempDir, $"voicestudio_mt_{stamp}.wav");
      legs.Add(new RecordingCaptureLegPlan
      {
        TrackId = kv.Key,
        InputSourceId = kv.Value,
        WaveInDeviceNumber = deviceNumber,
        OutputPath = path,
      });
    }

    return new RecordingCaptureValidationResult { Success = true, Legs = legs };
  }

  public async Task<RecordingCaptureLegStartResult> StartLegsAsync(
      RecordingCaptureValidationResult plan,
      int sampleRate,
      int channels,
      CancellationToken cancellationToken)
  {
    if (!plan.Success || plan.Legs.Count == 0)
      return new RecordingCaptureLegStartResult { Success = false, ErrorMessage = plan.ErrorMessage ?? "Invalid capture plan." };

    List<ActiveLeg> started = new();
    try
    {
      foreach (var legPlan in plan.Legs)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var leg = _legFactory();
        leg.Error += OnLegError;
        leg.LevelChanged += OnLegLevelChanged;
        await leg.StartAsync(
                legPlan.TrackId,
                legPlan.OutputPath,
                sampleRate,
                channels,
                legPlan.WaveInDeviceNumber,
                cancellationToken)
            .ConfigureAwait(false);
        lock (_sync)
        {
          started.Add(new ActiveLeg(legPlan.TrackId, legPlan.InputSourceId, leg));
          _active.Add(new ActiveLeg(legPlan.TrackId, legPlan.InputSourceId, leg));
        }
      }

      _sessionFaulted = false;
      StartActiveCaptureTopologyWatch();
      return new RecordingCaptureLegStartResult { Success = true };
    }
    catch (OperationCanceledException)
    {
      await DisposeStartedLegsAsync(started).ConfigureAwait(false);
      throw;
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[RecordingCaptureFanoutService] Start failed: {ex.Message}");
      foreach (var a in started.ToList())
      {
        try
        {
          if (a.Leg.IsRecording)
            _ = await a.Leg.StopRecordingAsync().ConfigureAwait(false);
        }
        catch (Exception stopEx)
        {
          Debug.WriteLine($"[RecordingCaptureFanoutService] Leg stop after failure: {stopEx.Message}");
        }

        TryRemoveAndDispose(a);
      }

      return new RecordingCaptureLegStartResult { Success = false, ErrorMessage = ex.Message };
    }
  }

  public async Task<RecordingCaptureStopResult> StopAllAsync(CancellationToken cancellationToken)
  {
    await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      List<ActiveLeg> snapshot;
      lock (_sync)
        snapshot = _active.ToList();

      var outcomes = await StopSnapshotLegsAsync(snapshot).ConfigureAwait(false);
      var faulted = _sessionFaulted || outcomes.Any(o => !o.CompletedSuccessfully);
      _sessionFaulted = false;
      _aggregatePeak = 0f;
      StopActiveCaptureTopologyWatch();
      return new RecordingCaptureStopResult { SessionFaulted = faulted, Legs = outcomes };
    }
    finally
    {
      _sessionGate.Release();
    }
  }

  public async Task CancelAllAsync()
  {
    await _sessionGate.WaitAsync().ConfigureAwait(false);
    try
    {
      List<ActiveLeg> snapshot;
      lock (_sync)
        snapshot = _active.ToList();

      foreach (var a in snapshot)
      {
        try
        {
          if (a.Leg.IsRecording)
          {
            var path = await a.Leg.StopRecordingAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
              try
              {
                File.Delete(path);
              }
              catch (Exception ex)
              {
                Debug.WriteLine($"[RecordingCaptureFanoutService] Cancel delete failed: {ex.Message}");
              }
            }
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[RecordingCaptureFanoutService] Cancel stop failed: {ex.Message}");
        }
        finally
        {
          TryRemoveAndDispose(a);
        }
      }

      _sessionFaulted = false;
      _aggregatePeak = 0f;
      StopActiveCaptureTopologyWatch();
    }
    finally
    {
      _sessionGate.Release();
    }
  }

  private async Task DisposeStartedLegsAsync(List<ActiveLeg> legs)
  {
    foreach (var a in legs)
    {
      try
      {
        if (a.Leg.IsRecording)
          _ = await a.Leg.StopRecordingAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[RecordingCaptureFanoutService] DisposeStarted: {ex.Message}");
      }
      finally
      {
        TryRemoveAndDispose(a);
      }
    }
  }

  private void OnLegError(object? sender, string message)
  {
    _sessionFaulted = true;
    Debug.WriteLine($"[RecordingCaptureFanoutService] Leg error: {message}");
    _ = DrainAndRaiseFaultAsync(message);
  }

  private async Task DrainAndRaiseFaultAsync(string message)
  {
    await _sessionGate.WaitAsync().ConfigureAwait(false);
    try
    {
      List<ActiveLeg> snapshot;
      lock (_sync)
        snapshot = _active.ToList();

      if (snapshot.Count == 0)
      {
        _sessionFaulted = false;
        return;
      }

      var outcomes = await StopSnapshotLegsAsync(snapshot).ConfigureAwait(false);
      _sessionFaulted = false;
      _aggregatePeak = 0f;
      var result = new RecordingCaptureStopResult
      {
        SessionFaulted = true,
        Legs = outcomes,
      };
      CaptureSessionFaulted?.Invoke(this, new RecordingCaptureFaultedEventArgs(message, result));
      StopActiveCaptureTopologyWatch();
    }
    finally
    {
      _sessionGate.Release();
    }
  }

  private async Task<List<RecordingCaptureLegOutcome>> StopSnapshotLegsAsync(List<ActiveLeg> snapshot)
  {
    var outcomes = new List<RecordingCaptureLegOutcome>();
    foreach (var a in snapshot)
    {
      try
      {
        if (a.Leg.IsRecording)
        {
          var path = await a.Leg.StopRecordingAsync().ConfigureAwait(false);
          outcomes.Add(new RecordingCaptureLegOutcome
          {
            TrackId = a.TrackId,
            LocalPath = path,
            CompletedSuccessfully = File.Exists(path),
          });
        }
        else
        {
          outcomes.Add(new RecordingCaptureLegOutcome
          {
            TrackId = a.TrackId,
            LocalPath = null,
            CompletedSuccessfully = false,
            ErrorMessage = "Leg was not recording.",
          });
        }
      }
      catch (Exception ex)
      {
        outcomes.Add(new RecordingCaptureLegOutcome
        {
          TrackId = a.TrackId,
          CompletedSuccessfully = false,
          ErrorMessage = ex.Message,
        });
      }
      finally
      {
        TryRemoveAndDispose(a);
      }
    }

    return outcomes;
  }

  private void OnLegLevelChanged(object? sender, float level)
  {
    float peak;
    lock (_sync)
    {
      if (_active.Count == 0)
      {
        peak = 0f;
      }
      else
      {
        peak = _active.Max(x => x.Leg.CurrentLevel);
      }
    }

    _aggregatePeak = peak;
    AggregateLevelChanged?.Invoke(this, peak);
  }

  private void TryRemoveAndDispose(ActiveLeg a)
  {
    lock (_sync)
    {
      _active.RemoveAll(x => ReferenceEquals(x.Leg, a.Leg));
    }

    a.Leg.Error -= OnLegError;
    a.Leg.LevelChanged -= OnLegLevelChanged;
    a.Leg.Dispose();
  }

  private readonly struct ActiveLeg
  {
    public ActiveLeg(string trackId, string inputSourceId, IRecordingCaptureLeg leg)
    {
      TrackId = trackId;
      InputSourceId = inputSourceId;
      Leg = leg;
    }

    public string TrackId { get; }

    public string InputSourceId { get; }

    public IRecordingCaptureLeg Leg { get; }
  }
}
