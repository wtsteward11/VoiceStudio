using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Commands
{
    /// <summary>
    /// Handles all playback-related commands: play, pause, stop, record, seek.
    /// </summary>
    public sealed class PlaybackOperationsHandler
    {
        private readonly IUnifiedCommandRegistry _registry;
        private readonly IAudioPlayerService _audioPlayer;
        private readonly ToastNotificationService? _toastService;
        private readonly Services.MicrophoneRecordingService _recordingService;

        private bool _isPlaying;
        private bool _isPaused;
        private bool _isRecording;
        private TimeSpan _currentPosition;
        private TimeSpan _duration;

        public event EventHandler<PlaybackState>? PlaybackStateChanged;
        public event EventHandler<TimeSpan>? PositionChanged;

        /// <summary>Raised when a recording completes with the file path.</summary>
        public event EventHandler<Services.RecordingCompletedEventArgs>? RecordingCompleted;

        public PlaybackOperationsHandler(
            IUnifiedCommandRegistry registry,
            IAudioPlayerService audioPlayer,
            ToastNotificationService? toastService = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
            _toastService = toastService;

            // Initialize microphone recording service
            _recordingService = new Services.MicrophoneRecordingService();
            _recordingService.RecordingStopped += OnRecordingCompleted;
            _recordingService.RecordingError += OnRecordingError;

            RegisterCommands();
        }

        public bool IsPlaying => _isPlaying;
        public bool IsPaused => _isPaused;
        public bool IsRecording => _isRecording;
        public TimeSpan CurrentPosition => _currentPosition;
        public TimeSpan Duration => _duration;

        private void RegisterCommands()
        {
            // playback.play
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.play",
                    Title = "Play",
                    Description = "Play or resume audio playback",
                    Category = "Playback",
                    Icon = "▶️",
                    KeyboardShortcut = "Space"
                },
                async (param, ct) => await PlayAsync(ct),
                _ => !_isPlaying || _isPaused
            );

            // playback.pause
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.pause",
                    Title = "Pause",
                    Description = "Pause audio playback",
                    Category = "Playback",
                    Icon = "⏸️"
                },
                async (param, ct) => await PauseAsync(ct),
                _ => _isPlaying && !_isPaused
            );

            // playback.toggle
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.toggle",
                    Title = "Play/Pause",
                    Description = "Toggle play/pause",
                    Category = "Playback",
                    Icon = "⏯️",
                    KeyboardShortcut = "Space"
                },
                async (param, ct) => await TogglePlayPauseAsync(ct),
                _ => true
            );

            // playback.stop
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.stop",
                    Title = "Stop",
                    Description = "Stop audio playback",
                    Category = "Playback",
                    Icon = "⏹️",
                    KeyboardShortcut = "Escape"
                },
                async (param, ct) => await StopAsync(ct),
                _ => _isPlaying || _isPaused
            );

            // playback.record
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.record",
                    Title = "Record",
                    Description = "Start or stop recording",
                    Category = "Playback",
                    Icon = "⏺️",
                    KeyboardShortcut = "Ctrl+R"
                },
                async (param, ct) => await ToggleRecordAsync(ct),
                _ => true
            );

            // playback.rewind
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.rewind",
                    Title = "Go to Start",
                    Description = "Return to the beginning",
                    Category = "Playback",
                    Icon = "⏮️",
                    KeyboardShortcut = "Home"
                },
                async (param, ct) => await SeekAsync(TimeSpan.Zero, ct),
                _ => true
            );

            // playback.forward
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.forward",
                    Title = "Go to End",
                    Description = "Skip to the end",
                    Category = "Playback",
                    Icon = "⏭️",
                    KeyboardShortcut = "End"
                },
                async (param, ct) => await SeekAsync(_duration, ct),
                _ => true
            );

            // playback.stepBack
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.stepBack",
                    Title = "Step Back",
                    Description = "Move back 5 seconds",
                    Category = "Playback",
                    Icon = "⏪",
                    KeyboardShortcut = "Left"
                },
                async (param, ct) => await StepAsync(TimeSpan.FromSeconds(-5), ct),
                _ => true
            );

            // playback.stepForward
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.stepForward",
                    Title = "Step Forward",
                    Description = "Move forward 5 seconds",
                    Category = "Playback",
                    Icon = "⏩",
                    KeyboardShortcut = "Right"
                },
                async (param, ct) => await StepAsync(TimeSpan.FromSeconds(5), ct),
                _ => true
            );

            // playback.seek
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "playback.seek",
                    Title = "Seek",
                    Description = "Seek to a specific position",
                    Category = "Playback",
                    Icon = "🔍"
                },
                async (param, ct) =>
                {
                    if (param is TimeSpan position)
                    {
                        await SeekAsync(position, ct);
                    }
                    else if (param is double seconds)
                    {
                        await SeekAsync(TimeSpan.FromSeconds(seconds), ct);
                    }
                },
                _ => true
            );

            ErrorLogger.LogDebug("Registered 10 playback commands", "PlaybackOperationsHandler");
        }

        public async Task PlayAsync(CancellationToken ct = default)
        {
            try
            {
                if (_isPaused)
                {
                    // Resume playback
                    _audioPlayer.Resume();
                }
                else
                {
                    // Start/Resume playback - Resume also starts if stopped
                    _audioPlayer.Resume();
                }

                _isPlaying = true;
                _isPaused = false;

                PlaybackStateChanged?.Invoke(this, PlaybackState.Playing);
                ErrorLogger.LogInfo("Playback started", "PlaybackOperationsHandler");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Play failed: {ex.Message}", "PlaybackOperationsHandler");
                throw;
            }

            await Task.CompletedTask;
        }

        public async Task PauseAsync(CancellationToken ct = default)
        {
            try
            {
                _audioPlayer.Pause();
                _isPaused = true;

                PlaybackStateChanged?.Invoke(this, PlaybackState.Paused);
                ErrorLogger.LogInfo("Playback paused", "PlaybackOperationsHandler");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Pause failed: {ex.Message}", "PlaybackOperationsHandler");
                throw;
            }

            await Task.CompletedTask;
        }

        public async Task TogglePlayPauseAsync(CancellationToken ct = default)
        {
            if (_isPlaying && !_isPaused)
            {
                await PauseAsync(ct);
            }
            else
            {
                await PlayAsync(ct);
            }
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            try
            {
                _audioPlayer.Stop();
                _isPlaying = false;
                _isPaused = false;
                _currentPosition = TimeSpan.Zero;

                PlaybackStateChanged?.Invoke(this, PlaybackState.Stopped);
                PositionChanged?.Invoke(this, TimeSpan.Zero);
                ErrorLogger.LogInfo("Playback stopped", "PlaybackOperationsHandler");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Stop failed: {ex.Message}", "PlaybackOperationsHandler");
                throw;
            }

            await Task.CompletedTask;
        }

        public async Task ToggleRecordAsync(CancellationToken ct = default)
        {
            if (_isRecording)
            {
                await StopRecordingAsync(ct);
            }
            else
            {
                await StartRecordingAsync(ct);
            }
        }

        public async Task StartRecordingAsync(CancellationToken ct = default)
        {
            try
            {
                // Stop any current playback first
                if (_isPlaying)
                {
                    await StopAsync(ct);
                }

                // Start microphone capture via NAudio
                await _recordingService.StartRecordingAsync();

                _isRecording = true;
                PlaybackStateChanged?.Invoke(this, PlaybackState.Recording);
                _toastService?.ShowInfo("Recording started - speak into your microphone");
                ErrorLogger.LogInfo("Recording started via MicrophoneRecordingService", "PlaybackOperationsHandler");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No audio input"))
            {
                _toastService?.ShowError("No Microphone", "No microphone found. Please connect a microphone and try again.");
                ErrorLogger.LogWarning($"No microphone: {ex.Message}", "PlaybackOperationsHandler");
            }
            catch (Exception ex)
            {
                _toastService?.ShowError("Recording Failed", $"Failed to start recording: {ex.Message}");
                ErrorLogger.LogWarning($"Start recording failed: {ex.Message}", "PlaybackOperationsHandler");
                throw;
            }
        }

        public async Task StopRecordingAsync(CancellationToken ct = default)
        {
            try
            {
                if (!_isRecording)
                {
                    return;
                }

                // Stop microphone capture -- file is finalized by the service
                var recordingPath = await _recordingService.StopRecordingAsync();
                var duration = _recordingService.Duration;

                _isRecording = false;
                PlaybackStateChanged?.Invoke(this, PlaybackState.Stopped);

                _toastService?.ShowSuccess($"Recording saved ({duration.TotalSeconds:F1}s)");
                ErrorLogger.LogInfo($"Recording stopped: {duration.TotalSeconds:F1}s -> {recordingPath}", "PlaybackOperationsHandler");
            }
            catch (Exception ex)
            {
                _isRecording = false;
                PlaybackStateChanged?.Invoke(this, PlaybackState.Stopped);
                ErrorLogger.LogWarning($"Stop recording failed: {ex.Message}", "PlaybackOperationsHandler");
                throw;
            }
        }

        public async Task SeekAsync(TimeSpan position, CancellationToken ct = default)
        {
            try
            {
                // Clamp position to valid range
                if (position < TimeSpan.Zero)
                    position = TimeSpan.Zero;
                if (position > _duration)
                    position = _duration;

                _audioPlayer.Seek(position.TotalSeconds);
                _currentPosition = position;

                PositionChanged?.Invoke(this, position);
                ErrorLogger.LogDebug($"Seeked to: {position}", "PlaybackOperationsHandler");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Seek failed: {ex.Message}", "PlaybackOperationsHandler");
                throw;
            }

            await Task.CompletedTask;
        }

        public async Task StepAsync(TimeSpan offset, CancellationToken ct = default)
        {
            var newPosition = _currentPosition + offset;
            await SeekAsync(newPosition, ct);
        }

        public void SetDuration(TimeSpan duration)
        {
            _duration = duration;
            ErrorLogger.LogDebug($"Duration set: {duration}", "PlaybackOperationsHandler");
        }

        public void UpdatePosition(TimeSpan position)
        {
            _currentPosition = position;
            PositionChanged?.Invoke(this, position);
        }

        // ---------------------------------------------------------------
        // Recording event handlers
        // ---------------------------------------------------------------

        private void OnRecordingCompleted(
            object? sender,
            Services.RecordingCompletedEventArgs e)
        {
            ErrorLogger.LogInfo($"Recording completed: {e.Duration.TotalSeconds:F1}s -> {e.FilePath}", "PlaybackOperationsHandler");
            RecordingCompleted?.Invoke(this, e);
        }

        private void OnRecordingError(object? sender, string errorMessage)
        {
            _isRecording = false;
            PlaybackStateChanged?.Invoke(this, PlaybackState.Stopped);
            _toastService?.ShowError("Recording Error", errorMessage);
            ErrorLogger.LogWarning($"Recording error: {errorMessage}", "PlaybackOperationsHandler");
        }
    }

    /// <summary>
    /// Represents the current playback state.
    /// </summary>
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused,
        Recording
    }
}
