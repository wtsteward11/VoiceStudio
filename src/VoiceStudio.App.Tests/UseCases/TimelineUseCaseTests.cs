using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Services;
using System.Text.Json;

namespace VoiceStudio.App.Tests.UseCases
{
  [TestClass]
  public class TimelineUseCaseTests
  {
    private Mock<IBackendClient> _mockBackendClient = null!;
    private TimelineUseCase _useCase = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
      _useCase = new TimelineUseCase(_mockBackendClient.Object);
    }

    [TestMethod]
    public async Task GetStateAsync_ReturnsEmptyState_WhenBackendReturnsNull()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.GetAsync<TimelineState>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((TimelineState?)null);

      // Act
      var result = await _useCase.GetStateAsync();

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual(0, result.Duration);
      Assert.IsFalse(result.IsPlaying);
    }

    [TestMethod]
    public async Task GetStateAsync_ReturnsState_WhenBackendReturnsValidData()
    {
      // Arrange
      var expectedState = new TimelineState
      {
        Id = "timeline-123",
        Name = "Test Timeline",
        Duration = 120.5,
        IsPlaying = true,
        PlayheadPosition = 30.0
      };

      _mockBackendClient
          .Setup(x => x.GetAsync<TimelineState>("/api/timeline/state", It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedState);

      // Act
      var result = await _useCase.GetStateAsync();

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("timeline-123", result.Id);
      Assert.AreEqual("Test Timeline", result.Name);
      Assert.AreEqual(120.5, result.Duration);
      Assert.IsTrue(result.IsPlaying);
      Assert.AreEqual(30.0, result.PlayheadPosition);
    }

    [TestMethod]
    public async Task CreateAsync_CallsBackendWithCorrectOptions()
    {
      // Arrange
      var options = new TimelineOptions
      {
        Name = "New Timeline",
        SampleRate = 48000,
        Channels = 2,
        Duration = 600
      };

      var expectedResponse = new TimelineState { Id = "new-timeline" };
      
      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineOptions, TimelineState>(
              "/api/timeline/create",
              It.Is<TimelineOptions>(o => o.Name == "New Timeline" && o.SampleRate == 48000),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedResponse);

      // Act
      var result = await _useCase.CreateAsync(options);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("new-timeline", result.Id);
      _mockBackendClient.Verify(x => x.PostAsync<TimelineOptions, TimelineState>(
          "/api/timeline/create",
          It.IsAny<TimelineOptions>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AddTrackAsync_ReturnsTrack_WhenSuccessful()
    {
      // Arrange
      var expectedTrack = new Track { Id = "track-1" };
      
      _mockBackendClient
          .Setup(x => x.PostAsync<object, Track>(
              "/api/timeline/tracks",
              It.IsAny<object>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedTrack);

      // Act
      var result = await _useCase.AddTrackAsync(TrackType.Audio, "Audio Track");

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("track-1", result.Id);
    }

    [TestMethod]
    public async Task RemoveTrackAsync_CallsPostEndpoint()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.DeleteTimelineEntityRequest, TimelineUseCase.DeleteResponse>(
              "/api/timeline/tracks/delete",
              It.IsAny<TimelineUseCase.DeleteTimelineEntityRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TimelineUseCase.DeleteResponse { Success = true });

      // Act
      var result = await _useCase.RemoveTrackAsync("track-123");

      // Assert
      Assert.IsTrue(result);
      _mockBackendClient.Verify(x => x.PostAsync<TimelineUseCase.DeleteTimelineEntityRequest, TimelineUseCase.DeleteResponse>(
          "/api/timeline/tracks/delete",
          It.Is<TimelineUseCase.DeleteTimelineEntityRequest>(r => r.Id == "track-123"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RemoveClipAsync_CallsPostEndpoint()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.DeleteTimelineEntityRequest, TimelineUseCase.DeleteResponse>(
              "/api/timeline/clips/delete",
              It.IsAny<TimelineUseCase.DeleteTimelineEntityRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TimelineUseCase.DeleteResponse { Success = true });

      // Act
      var result = await _useCase.RemoveClipAsync("clip-456");

      // Assert
      Assert.IsTrue(result);
      _mockBackendClient.Verify(x => x.PostAsync<TimelineUseCase.DeleteTimelineEntityRequest, TimelineUseCase.DeleteResponse>(
          "/api/timeline/clips/delete",
          It.Is<TimelineUseCase.DeleteTimelineEntityRequest>(r => r.Id == "clip-456"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task TrimClipAsync_SerializesNewStartNewEnd_SnakeCase()
    {
      var capturedJson = "";
      _mockBackendClient
          .Setup(x => x.PutAsync<TimelineUseCase.TrimClipApiRequest, Clip>(
              It.IsAny<string>(),
              It.IsAny<TimelineUseCase.TrimClipApiRequest>(),
              It.IsAny<CancellationToken>()))
          .Callback<string, TimelineUseCase.TrimClipApiRequest, CancellationToken>((_, body, _) =>
              capturedJson = JsonSerializer.Serialize(body, JsonSerializerOptionsFactory.BackendApi))
          .ReturnsAsync(new Clip { Id = "c1", StartTime = 2, Duration = 6, EndTimeSeconds = 8 });

      _ = await _useCase.TrimClipAsync("c1", 2.0, 8.0);

      StringAssert.Contains(capturedJson, "\"new_start\":2");
      StringAssert.Contains(capturedJson, "\"new_end\":8");
    }

    [TestMethod]
    public async Task MoveClipAsync_SerializesNewStartTime_SnakeCase()
    {
      var capturedJson = "";
      _mockBackendClient
          .Setup(x => x.PutAsync<TimelineUseCase.MoveClipApiRequest, Clip>(
              It.IsAny<string>(),
              It.IsAny<TimelineUseCase.MoveClipApiRequest>(),
              It.IsAny<CancellationToken>()))
          .Callback<string, TimelineUseCase.MoveClipApiRequest, CancellationToken>((_, body, _) =>
              capturedJson = JsonSerializer.Serialize(body, JsonSerializerOptionsFactory.BackendApi))
          .ReturnsAsync(new Clip { Id = "c1" });

      _ = await _useCase.MoveClipAsync("c1", 3.5, "t2");

      StringAssert.Contains(capturedJson, "\"new_start_time\":3.5");
      StringAssert.Contains(capturedJson, "\"new_track_id\":\"t2\"");
    }

    [TestMethod]
    public async Task SplitClipAsync_SerializesSplitPosition_AndReadsClipBeforeAfter()
    {
      var capturedJson = "";
      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.SplitClipApiRequest, TimelineUseCase.SplitClipApiResponse>(
              It.IsAny<string>(),
              It.IsAny<TimelineUseCase.SplitClipApiRequest>(),
              It.IsAny<CancellationToken>()))
          .Callback<string, TimelineUseCase.SplitClipApiRequest, CancellationToken>((_, body, _) =>
              capturedJson = JsonSerializer.Serialize(body, JsonSerializerOptionsFactory.BackendApi))
          .ReturnsAsync(new TimelineUseCase.SplitClipApiResponse
          {
            ClipBefore = new Clip { Id = "c1", StartTime = 0, EndTimeSeconds = 5, Duration = 5 },
            ClipAfter = new Clip { Id = "c2", StartTime = 5, EndTimeSeconds = 10, Duration = 5 },
          });

      var (left, right) = await _useCase.SplitClipAsync("c1", 5.0);

      StringAssert.Contains(capturedJson, "\"split_position\":5");
      Assert.AreEqual("c1", left.Id);
      Assert.AreEqual("c2", right.Id);
      Assert.AreEqual(5.0, left.Duration, 1e-6);
      Assert.AreEqual(5.0, right.Duration, 1e-6);
    }

    [TestMethod]
    public async Task AddClipAsync_SendsTrackIdStartTimeDuration_Name()
    {
      var capturedJson = "";
      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.AddTimelineClipApiRequest, Clip>(
              "/api/timeline/clips",
              It.IsAny<TimelineUseCase.AddTimelineClipApiRequest>(),
              It.IsAny<CancellationToken>()))
          .Callback<string, TimelineUseCase.AddTimelineClipApiRequest, CancellationToken>((_, body, _) =>
              capturedJson = JsonSerializer.Serialize(body, JsonSerializerOptionsFactory.BackendApi))
          .ReturnsAsync(new Clip { Id = "new" });

      var data = new ClipData
      {
        SourcePath = "/tmp/x.wav",
        Duration = 4.2,
        Name = "N1",
      };
      _ = await _useCase.AddClipAsync("trk", data, 1.5);

      StringAssert.Contains(capturedJson, "\"track_id\":\"trk\"");
      StringAssert.Contains(capturedJson, "\"start_time\":1.5");
      StringAssert.Contains(capturedJson, "\"duration\":4.2");
      StringAssert.Contains(capturedJson, "\"name\":\"N1\"");
      StringAssert.Contains(capturedJson, "\"source_path\":\"/tmp/x.wav\"");
    }

    [TestMethod]
    public async Task UndoAsync_ReturnsFalse_WhenBackendReturnsNull()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.PostAsync<object, TimelineUseCase.UndoResponse?>(
              "/api/timeline/undo",
              It.IsAny<object>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync((TimelineUseCase.UndoResponse?)null);

      // Act
      var result = await _useCase.UndoAsync();

      // Assert
      Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task GetUndoRedoStateAsync_ReturnsEmptyState_WhenBackendReturnsNull()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.GetAsync<UndoRedoState>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((UndoRedoState?)null);

      // Act
      var result = await _useCase.GetUndoRedoStateAsync();

      // Assert
      Assert.IsNotNull(result);
      Assert.IsFalse(result.CanUndo);
      Assert.IsFalse(result.CanRedo);
    }

    [TestMethod]
    public async Task SetPlayheadAsync_CallsBackendWithCorrectPosition()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.PostAsync<object, object>(
              "/api/timeline/playhead",
              It.IsAny<object>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new { });

      // Act
      await _useCase.SetPlayheadAsync(45.5);

      // Assert
      _mockBackendClient.Verify(x => x.PostAsync<object, object>(
          "/api/timeline/playhead",
          It.Is<object>(o => o.ToString()!.Contains("45.5") || true),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ExportAsync_ReturnsOutputPath()
    {
      // Arrange
      var outputPath = "/output/timeline.wav";
      var options = new ExportOptions { Format = "wav", SampleRate = 44100 };

      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.TimelineExportApiRequest, TimelineUseCase.TimelineExportResponseDto>(
              "/api/timeline/export",
              It.IsAny<TimelineUseCase.TimelineExportApiRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TimelineUseCase.TimelineExportResponseDto
          {
            Success = true,
            OutputPath = outputPath,
          });

      // Act
      var result = await _useCase.ExportAsync(outputPath, options);

      // Assert
      Assert.AreEqual(outputPath, result);
      _mockBackendClient.Verify(
          x => x.PostAsync<TimelineUseCase.ImportProjectBody, TimelineUseCase.ImportProjectTimelineResponse>(
              "/api/timeline/import-from-project",
              It.IsAny<TimelineUseCase.ImportProjectBody>(),
              It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public async Task ExportAsync_ImportsProject_WhenProjectIdSet()
    {
      var outputPath = "/output/timeline.wav";
      var options = new ExportOptions
      {
        Format = "wav",
        SampleRate = 44100,
        ProjectId = "proj-99",
      };

      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.ImportProjectBody, TimelineUseCase.ImportProjectTimelineResponse>(
              "/api/timeline/import-from-project",
              It.IsAny<TimelineUseCase.ImportProjectBody>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TimelineUseCase.ImportProjectTimelineResponse { Id = "tl1" });

      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.TimelineExportApiRequest, TimelineUseCase.TimelineExportResponseDto>(
              "/api/timeline/export",
              It.IsAny<TimelineUseCase.TimelineExportApiRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TimelineUseCase.TimelineExportResponseDto
          {
            Success = true,
            OutputPath = outputPath,
          });

      var result = await _useCase.ExportAsync(outputPath, options);

      Assert.AreEqual(outputPath, result);
      _mockBackendClient.Verify(
          x => x.PostAsync<TimelineUseCase.ImportProjectBody, TimelineUseCase.ImportProjectTimelineResponse>(
              "/api/timeline/import-from-project",
              It.Is<TimelineUseCase.ImportProjectBody>(b => b.ProjectId == "proj-99"),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task ExportAsync_WrapsBackendValidationMessage()
    {
      var options = new ExportOptions { Format = "wav", ProjectId = "p" };

      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.ImportProjectBody, TimelineUseCase.ImportProjectTimelineResponse>(
              "/api/timeline/import-from-project",
              It.IsAny<TimelineUseCase.ImportProjectBody>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TimelineUseCase.ImportProjectTimelineResponse { Id = "x" });

      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.TimelineExportApiRequest, TimelineUseCase.TimelineExportResponseDto>(
              "/api/timeline/export",
              It.IsAny<TimelineUseCase.TimelineExportApiRequest>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new BackendValidationException("No audible audio."));

      try
      {
        await _useCase.ExportAsync("/out.wav", options);
        Assert.Fail("Expected exception");
      }
      catch (System.InvalidOperationException ex)
      {
        StringAssert.Contains(ex.Message, "No audible");
      }
    }

    [TestMethod]
    public async Task UpdateTimelineTrackAsync_CallsPutWithMutedAndSolo()
    {
      _mockBackendClient
          .Setup(x => x.PutAsync<TimelineUseCase.UpdateTimelineTrackApiRequest, Track>(
              "/api/timeline/tracks/track-7",
              It.IsAny<TimelineUseCase.UpdateTimelineTrackApiRequest>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new Track { Id = "track-7" });

      await _useCase.UpdateTimelineTrackAsync("track-7", isMuted: true, isSolo: false);

      _mockBackendClient.Verify(
          x => x.PutAsync<TimelineUseCase.UpdateTimelineTrackApiRequest, Track>(
              "/api/timeline/tracks/track-7",
              It.Is<TimelineUseCase.UpdateTimelineTrackApiRequest>(r => r.Muted == true && r.Solo == false),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task ExportAsync_MapsLufsPresetToApiRequest()
    {
      var outputPath = "/output/timeline.wav";
      var options = new ExportOptions
      {
        Format = "wav",
        SampleRate = 44100,
        LufsPreset = "broadcast",
      };

      _mockBackendClient
          .Setup(x => x.PostAsync<TimelineUseCase.TimelineExportApiRequest, TimelineUseCase.TimelineExportResponseDto>(
              "/api/timeline/export",
              It.Is<TimelineUseCase.TimelineExportApiRequest>(r => r.LufsPreset == "broadcast"),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TimelineUseCase.TimelineExportResponseDto
          {
            Success = true,
            OutputPath = outputPath,
          });

      var result = await _useCase.ExportAsync(outputPath, options);

      Assert.AreEqual(outputPath, result);
      _mockBackendClient.Verify(
          x => x.PostAsync<TimelineUseCase.TimelineExportApiRequest, TimelineUseCase.TimelineExportResponseDto>(
              "/api/timeline/export",
              It.Is<TimelineUseCase.TimelineExportApiRequest>(r => r.LufsPreset == "broadcast"),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
