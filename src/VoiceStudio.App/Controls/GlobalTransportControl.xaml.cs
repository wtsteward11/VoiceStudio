using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Controls
{
    public sealed partial class GlobalTransportControl : UserControl
    {
        private IContextManager? _contextManager;
        private IAudioPlayerService? _audioPlayer;
        private IStartupStateService? _startupState;
        private bool _subscribed;

        /// <summary>
        /// Raised when Play/Pause is clicked. MainWindow subscribes and calls TogglePlayback.
        /// </summary>
        public event EventHandler? PlayRequested;

        /// <summary>
        /// Raised when Stop is clicked. MainWindow subscribes and calls StopPlayback.
        /// </summary>
        public event EventHandler? StopRequested;

        public GlobalTransportControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_subscribed)
                return;

            _contextManager = AppServices.GetContextManager();
            _audioPlayer = AppServices.GetAudioPlayerService();
            _startupState = AppServices.GetStartupStateService();

            _contextManager.TransportContextChanged += OnTransportContextChanged;
            if (_audioPlayer != null)
                _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
            _startupState.StateChanged += OnStartupStateChanged;

            _subscribed = true;
            Refresh();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed)
                return;

            if (_contextManager != null)
            {
                _contextManager.TransportContextChanged -= OnTransportContextChanged;
                _contextManager = null;
            }

            if (_audioPlayer != null)
            {
                _audioPlayer.IsPlayingChanged -= OnIsPlayingChanged;
                _audioPlayer = null;
            }

            if (_startupState != null)
            {
                _startupState.StateChanged -= OnStartupStateChanged;
                _startupState = null;
            }

            _subscribed = false;
        }

        private void OnStartupStateChanged(object? sender, StartupStateChangedEventArgs e)
        {
            Refresh();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            PlayRequested?.Invoke(this, EventArgs.Empty);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnTransportContextChanged(object? sender, TransportContextChangedEventArgs e)
        {
            // Refresh on transport context change (audio id, source, title)
            Refresh();
        }

        private void OnIsPlayingChanged(object? sender, bool e)
        {
            Refresh();
        }

        private void Refresh()
        {
            // Round 4 Task 4: Disable transport until backend ready
            var isReady = _startupState?.IsReady ?? false;
            if (!isReady)
            {
                PlayPauseButton.IsEnabled = false;
                StopButton.IsEnabled = false;
                CurrentMediaTitle.Text = "Starting…";
                SourcePanelLabel.Text = "—";
                StatusLabel.Text = "Starting…";
                PlayPauseIcon.Glyph = "\uE768";
                return;
            }

            if (_contextManager == null || _audioPlayer == null)
                return;

            var title = _contextManager.CurrentPlayableTitle;
            var source = _contextManager.CurrentPlayableSource.ToDisplayString();
            var hasPlayable = !string.IsNullOrEmpty(_contextManager.CurrentPlayableAudioId);

            CurrentMediaTitle.Text = !string.IsNullOrEmpty(title) ? title : "No media selected";
            SourcePanelLabel.Text = !string.IsNullOrEmpty(source) ? source : "—";

            if (!hasPlayable)
                StatusLabel.Text = "No media selected";
            else if (_audioPlayer.IsPlaying)
                StatusLabel.Text = "Playing";
            else if (_audioPlayer.IsPaused)
                StatusLabel.Text = "Paused";
            else
                StatusLabel.Text = "Ready to play";

            var canControl = hasPlayable || _audioPlayer.IsPlaying || _audioPlayer.IsPaused;
            PlayPauseButton.IsEnabled = canControl;
            StopButton.IsEnabled = canControl;

            PlayPauseIcon.Glyph = _audioPlayer.IsPlaying ? "\uE769" : "\uE768";
        }
    }
}
