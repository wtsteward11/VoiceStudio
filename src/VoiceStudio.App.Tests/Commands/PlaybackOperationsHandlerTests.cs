using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Commands;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Helpers;
using VoiceStudio.Core.Recording;

namespace VoiceStudio.App.Tests.Commands
{
    /// <summary>
    /// Unit tests for PlaybackOperationsHandler.
    /// </summary>
    [TestClass]
    [TestCategory("Commands")]
    public class PlaybackOperationsHandlerTests : CommandHandlerTestBase
    {
        private PlaybackOperationsHandler _handler = null!;

        [TestInitialize]
        public override void SetupBase()
        {
            base.SetupBase();
            _handler = new PlaybackOperationsHandler(
                Registry,
                MockAudioPlayer.Object,
                null);
        }

        #region Registration Tests

        [TestMethod]
        public void Constructor_RegistersAllPlaybackCommands()
        {
            AssertCommandsRegistered(
                "playback.play",
                "playback.pause",
                "playback.toggle",
                "playback.stop",
                "playback.record",
                "playback.rewind",
                "playback.forward",
                "playback.stepBack",
                "playback.stepForward",
                "playback.seek"
            );
        }

        [TestMethod]
        public void Commands_HaveCorrectCategory()
        {
            AssertCommandMetadata("playback.play", "Play", "Playback");
            AssertCommandMetadata("playback.pause", "Pause", "Playback");
            AssertCommandMetadata("playback.stop", "Stop", "Playback");
        }

        #endregion

        #region Play Tests

        [TestMethod]
        public async Task Play_CallsAudioPlayerResume()
        {
            // Act
            await Registry.ExecuteAsync("playback.play");

            // Assert
            MockAudioPlayer.Verify(p => p.Resume(), Times.Once);
            Assert.IsTrue(_handler.IsPlaying);
            Assert.IsFalse(_handler.IsPaused);
        }

        [TestMethod]
        public async Task Play_WhenPaused_CallsResume()
        {
            // Arrange - First play then pause
            await _handler.PlayAsync();
            await _handler.PauseAsync();
            Assert.IsTrue(_handler.IsPaused);

            // Act
            await Registry.ExecuteAsync("playback.play");

            // Assert - Resume is called twice (once in arrange, once in act)
            MockAudioPlayer.Verify(p => p.Resume(), Times.AtLeast(2));
            Assert.IsTrue(_handler.IsPlaying);
            Assert.IsFalse(_handler.IsPaused);
        }

        #endregion

        #region Pause Tests

        [TestMethod]
        public async Task Pause_CallsAudioPlayerPause()
        {
            // Arrange - Start playing first
            await _handler.PlayAsync();

            // Act
            await Registry.ExecuteAsync("playback.pause");

            // Assert
            MockAudioPlayer.Verify(p => p.Pause(), Times.Once);
            Assert.IsTrue(_handler.IsPaused);
        }

        #endregion

        #region Toggle Tests

        [TestMethod]
        public async Task Toggle_WhenStopped_StartsPlaying()
        {
            // Arrange
            Assert.IsFalse(_handler.IsPlaying);

            // Act
            await Registry.ExecuteAsync("playback.toggle");

            // Assert
            Assert.IsTrue(_handler.IsPlaying);
        }

        [TestMethod]
        public async Task Toggle_WhenPlaying_Pauses()
        {
            // Arrange
            await _handler.PlayAsync();
            Assert.IsTrue(_handler.IsPlaying);
            Assert.IsFalse(_handler.IsPaused);

            // Act
            await Registry.ExecuteAsync("playback.toggle");

            // Assert
            Assert.IsTrue(_handler.IsPaused);
        }

        [TestMethod]
        public async Task Toggle_WhenPaused_Resumes()
        {
            // Arrange
            await _handler.PlayAsync();
            await _handler.PauseAsync();
            Assert.IsTrue(_handler.IsPaused);

            // Act
            await Registry.ExecuteAsync("playback.toggle");

            // Assert
            Assert.IsFalse(_handler.IsPaused);
            Assert.IsTrue(_handler.IsPlaying);
        }

        #endregion

        #region Stop Tests

        [TestMethod]
        public async Task Stop_CallsAudioPlayerStop()
        {
            // Arrange - Start playing first
            await _handler.PlayAsync();

            // Act
            await Registry.ExecuteAsync("playback.stop");

            // Assert
            MockAudioPlayer.Verify(p => p.Stop(), Times.Once);
            Assert.IsFalse(_handler.IsPlaying);
            Assert.IsFalse(_handler.IsPaused);
        }

        [TestMethod]
        public async Task Stop_ResetsPosition()
        {
            // Arrange
            _handler.UpdatePosition(TimeSpan.FromSeconds(30));
            await _handler.PlayAsync();

            // Act
            await Registry.ExecuteAsync("playback.stop");

            // Assert
            Assert.AreEqual(TimeSpan.Zero, _handler.CurrentPosition);
        }

        #endregion

        #region Seek Tests

        [TestMethod]
        public async Task Seek_WithTimeSpan_SetsPosition()
        {
            // Arrange
            _handler.SetDuration(TimeSpan.FromMinutes(5));
            var targetPosition = TimeSpan.FromSeconds(60);

            // Act
            await Registry.ExecuteAsync("playback.seek", targetPosition);

            // Assert
            MockAudioPlayer.Verify(p => p.Seek(60.0), Times.Once);
        }

        [TestMethod]
        public async Task Seek_WithDouble_ConvertsToTimeSpan()
        {
            // Arrange
            _handler.SetDuration(TimeSpan.FromMinutes(5));

            // Act
            await Registry.ExecuteAsync("playback.seek", 30.0);

            // Assert
            MockAudioPlayer.Verify(p => p.Seek(30.0), Times.Once);
        }

        [TestMethod]
        public async Task Seek_BeyondDuration_ClampsToDuration()
        {
            // Arrange
            var duration = TimeSpan.FromMinutes(2);
            _handler.SetDuration(duration);

            // Act
            await _handler.SeekAsync(TimeSpan.FromMinutes(10));

            // Assert
            MockAudioPlayer.Verify(p => p.Seek(duration.TotalSeconds), Times.Once);
        }

        [TestMethod]
        public async Task Seek_NegativeValue_ClampsToZero()
        {
            // Arrange
            _handler.SetDuration(TimeSpan.FromMinutes(5));

            // Act
            await _handler.SeekAsync(TimeSpan.FromSeconds(-10));

            // Assert
            MockAudioPlayer.Verify(p => p.Seek(0.0), Times.Once);
        }

        #endregion

        #region Step Tests

        [TestMethod]
        public async Task StepForward_MovesPositionForward()
        {
            // Arrange
            _handler.SetDuration(TimeSpan.FromMinutes(5));
            _handler.UpdatePosition(TimeSpan.FromSeconds(30));

            // Act
            await Registry.ExecuteAsync("playback.stepForward");

            // Assert - Should step forward 5 seconds
            MockAudioPlayer.Verify(p => p.Seek(35.0), Times.Once);
        }

        [TestMethod]
        public async Task StepBack_MovesPositionBack()
        {
            // Arrange
            _handler.SetDuration(TimeSpan.FromMinutes(5));
            _handler.UpdatePosition(TimeSpan.FromSeconds(30));

            // Act
            await Registry.ExecuteAsync("playback.stepBack");

            // Assert - Should step back 5 seconds
            MockAudioPlayer.Verify(p => p.Seek(25.0), Times.Once);
        }

        #endregion

        #region Rewind/Forward Tests

        [TestMethod]
        public async Task Rewind_SeeksToStart()
        {
            // Arrange
            _handler.UpdatePosition(TimeSpan.FromMinutes(2));

            // Act
            await Registry.ExecuteAsync("playback.rewind");

            // Assert
            MockAudioPlayer.Verify(p => p.Seek(0.0), Times.Once);
        }

        [TestMethod]
        public async Task Forward_SeeksToEnd()
        {
            // Arrange
            var duration = TimeSpan.FromMinutes(5);
            _handler.SetDuration(duration);

            // Act
            await Registry.ExecuteAsync("playback.forward");

            // Assert
            MockAudioPlayer.Verify(p => p.Seek(duration.TotalSeconds), Times.Once);
        }

        #endregion

        #region Record Tests

        [TestMethod]
        public async Task StartRecording_WithCoordinator_NoProject_DoesNotEnterRecordingState()
        {
            var coordinator = new RecordingSessionCoordinator();
            var handler = new PlaybackOperationsHandler(
                Registry,
                MockAudioPlayer.Object,
                null,
                null,
                coordinator);
            await handler.StartRecordingAsync();
            Assert.IsFalse(handler.IsRecording);
            Assert.AreEqual(MultitrackRecordingSessionPhase.None, coordinator.Phase);
        }

        [TestMethod]
        public async Task StartRecording_WithCoordinator_AuthorityResolverFails_DoesNotEnterRecordingState()
        {
            var coordinator = new RecordingSessionCoordinator();
            static Task<RecordingAuthorityResolution> FailResolver(CancellationToken ct) =>
                Task.FromResult(RecordingAuthorityResolution.Fail("Test authority block"));
            var handler = new PlaybackOperationsHandler(
                Registry,
                MockAudioPlayer.Object,
                null,
                null,
                coordinator,
                FailResolver);
            await handler.StartRecordingAsync();
            Assert.IsFalse(handler.IsRecording);
            Assert.AreEqual(MultitrackRecordingSessionPhase.None, coordinator.Phase);
        }

        [TestMethod]
        [TestCategory("RequiresAudioDevice")]
        public async Task Record_StartsRecording()
        {
            AudioDeviceGuard.SkipIfNoAudioInputDevice();
            // Act
            await Registry.ExecuteAsync("playback.record");

            // Assert
            Assert.IsTrue(_handler.IsRecording);
        }

        [TestMethod]
        [TestCategory("RequiresAudioDevice")]
        public async Task Record_WhenRecording_StopsRecording()
        {
            AudioDeviceGuard.SkipIfNoAudioInputDevice();
            // Arrange
            await _handler.StartRecordingAsync();
            Assert.IsTrue(_handler.IsRecording);

            // Act
            await Registry.ExecuteAsync("playback.record");

            // Assert
            Assert.IsFalse(_handler.IsRecording);
        }

        #endregion

        #region Event Tests

        [TestMethod]
        public async Task PlaybackStateChanged_RaisedOnPlay()
        {
            // Arrange
            PlaybackState? receivedState = null;
            _handler.PlaybackStateChanged += (s, state) => receivedState = state;

            // Act
            await Registry.ExecuteAsync("playback.play");

            // Assert
            Assert.AreEqual(PlaybackState.Playing, receivedState);
        }

        [TestMethod]
        public async Task PositionChanged_RaisedOnSeek()
        {
            // Arrange
            _handler.SetDuration(TimeSpan.FromMinutes(5));
            TimeSpan? receivedPosition = null;
            _handler.PositionChanged += (s, pos) => receivedPosition = pos;

            // Act
            await _handler.SeekAsync(TimeSpan.FromSeconds(60));

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(60), receivedPosition);
        }

        #endregion
    }
}
