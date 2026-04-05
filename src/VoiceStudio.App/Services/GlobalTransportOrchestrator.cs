using System;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Orchestrates global transport play/stop routing by source.
/// Injected with a delegate to resolve the Timeline transport controller (no UI-tree lookup).
/// </summary>
public sealed class GlobalTransportOrchestrator : IGlobalTransportOrchestrator
{
    private readonly IContextManager _contextManager;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly BackendClientConfig _config;
    private readonly ToastNotificationService? _toastService;
    private readonly TransportOrchestrationBootstrap _bootstrap;

    public GlobalTransportOrchestrator(
        IContextManager contextManager,
        IAudioPlayerService audioPlayer,
        BackendClientConfig config,
        ToastNotificationService? toastService,
        TransportOrchestrationBootstrap bootstrap)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _toastService = toastService;
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    }

    public async Task TogglePlaybackAsync()
    {
        var source = _contextManager.CurrentPlayableSource;
        var audioId = _contextManager.CurrentPlayableAudioId;
        var baseUrl = _config.BaseUrl?.TrimEnd('/') ?? BackendClientConfig.DefaultHttpBaseUrl;

        // Timeline path: when Timeline owns transport
        if (source == TransportSource.Timeline)
        {
            var controller = _bootstrap.GetTimelineController();
            if (controller != null)
            {
                if (controller.IsPlaying)
                    controller.Pause();
                else
                    await controller.PlayAsync();
                return;
            }

            // Controller not resolved (e.g. panel not loaded): still drive shared player so global/keyboard match physical state.
            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Pause();
                return;
            }

            if (_audioPlayer.IsPaused)
            {
                _audioPlayer.Resume();
                return;
            }
        }

        // Library / Synthesis / Recording / Analyzer path: use IAudioPlayerService with backend audio ID
        if (!string.IsNullOrEmpty(audioId) && (source == TransportSource.Library
            || source == TransportSource.Synthesis
            || source == TransportSource.Recording
            || source == TransportSource.Analyzer))
        {
            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Pause();
            }
            else if (_audioPlayer.IsPaused)
            {
                _audioPlayer.Resume();
            }
            else
            {
                await _audioPlayer.PlayBackendAudioIdAsync(audioId, baseUrl);
            }
            return;
        }

        // Nothing playable
        _toastService?.ShowToast(ToastType.Info, "No media selected", "Select an audio asset in Library or Timeline, then press Play.");
    }

    public void StopPlayback()
    {
        var source = _contextManager.CurrentPlayableSource;

        // Timeline path
        if (source == TransportSource.Timeline)
        {
            var controller = _bootstrap.GetTimelineController();
            if (controller != null)
            {
                controller.Stop();
                return;
            }

            _audioPlayer.Stop();
            return;
        }

        // Library / Synthesis / Recording / Analyzer: stop via IAudioPlayerService
        if (source == TransportSource.Library
            || source == TransportSource.Synthesis
            || source == TransportSource.Recording
            || source == TransportSource.Analyzer)
        {
            _audioPlayer.Stop();
        }
    }

    /// <inheritdoc />
    public void PausePlayback()
    {
        var source = _contextManager.CurrentPlayableSource;

        if (source == TransportSource.Timeline)
        {
            var controller = _bootstrap.GetTimelineController();
            if (controller != null)
            {
                if (controller.IsPlaying)
                    controller.Pause();
                return;
            }
        }

        if (_audioPlayer.IsPlaying)
            _audioPlayer.Pause();
    }
}
