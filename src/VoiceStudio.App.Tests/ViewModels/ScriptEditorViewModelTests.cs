using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Unit tests for ScriptEditorViewModel.
  /// Tests cover panel properties and initial state.
  /// </summary>
  [TestClass]
  public class ScriptEditorViewModelTests
  {
    private IViewModelContext _context = null!;
    private Mock<IScriptEditorClient> _mockScriptEditorClient = null!;
    private Mock<IDialogService> _mockDialogService = null!;
    private DispatcherQueueController? _dispatcherController;
    private ScriptEditorViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
      _mockScriptEditorClient = new Mock<IScriptEditorClient>();
      _mockScriptEditorClient.Setup(x => x.GetScriptsAsync(null, null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceStudio.Core.Models.Script>());
      _mockDialogService = new Mock<IDialogService>();
      _mockDialogService
          .Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
          .ReturnsAsync(true);

      _sut = new ScriptEditorViewModel(_context, _mockScriptEditorClient.Object, _mockDialogService.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _sut?.Dispose();
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsScriptEditor()
    {
      Assert.AreEqual(PanelIds.ScriptEditor, _sut.PanelId);
    }

    [TestMethod]
    public void DisplayName_ReturnsLocalizedName()
    {
      Assert.IsNotNull(_sut.DisplayName);
      Assert.IsTrue(_sut.DisplayName.Length > 0);
    }

    #endregion

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
      Assert.IsNotNull(_sut);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullScriptEditorClient_ThrowsArgumentNullException()
    {
      _ = new ScriptEditorViewModel(_context, null!, _mockDialogService!.Object);
    }

    #endregion

    #region Initial State Tests

    [TestMethod]
    public void Scripts_InitiallyEmpty()
    {
      Assert.IsNotNull(_sut.Scripts);
      Assert.AreEqual(0, _sut.Scripts.Count);
    }

    [TestMethod]
    public void SelectedScript_InitiallyNull()
    {
      Assert.IsNull(_sut.SelectedScript);
    }

    [TestMethod]
    public void IsLoading_InitiallyFalse()
    {
      Assert.IsFalse(_sut.IsLoading);
    }

    #endregion

    #region Command Existence Tests

    [TestMethod]
    public void DeleteScriptCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.DeleteScriptCommand);
    }

    #endregion

    #region Generate-to-Persist-to-Play Tests

    /// <summary>
    /// Proves the full chain: GenerateSegmentCommand calls synthesis, persists GeneratedAudioId via UpdateScriptAsync,
    /// reloads scripts, rebinds selection, and enables PlaySegmentCommand for the segment.
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_PersistsGeneratedAudioId_AndEnablesPlay()
    {
      VoiceSynthesisRequest? capturedSynthesisRequest = null;
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .Callback<VoiceSynthesisRequest, CancellationToken>((req, _) => capturedSynthesisRequest = req)
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "test-audio-1" });

      var mockAudioPlayer = new Mock<IAudioPlayerService>();

      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hello world",
        VoiceProfileId = "profile-1"
      };
      var updatedScriptForReload = new Script
      {
        Id = "script-1",
        Name = "Test Script",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment>
        {
          new ScriptSegment
          {
            Id = "seg-1",
            Text = "Hello world",
            VoiceProfileId = "profile-1",
            GeneratedAudioId = "test-audio-1"
          }
        },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };

      ScriptUpdateRequest? capturedRequest = null;
      _mockScriptEditorClient
        .Setup(x => x.GetScriptsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Script> { updatedScriptForReload });
      _mockScriptEditorClient
        .Setup(x => x.UpdateScriptAsync(It.IsAny<string>(), It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .Callback<string, ScriptUpdateRequest, CancellationToken>((_, req, _) => capturedRequest = req)
        .ReturnsAsync((string id, ScriptUpdateRequest req, CancellationToken _) =>
        {
          var script = new Script
          {
            Id = id,
            Name = req.Name ?? "Test",
            Description = req.Description,
            ProjectId = "proj-1",
            Segments = req.Segments ?? new List<ScriptSegment>(),
            Metadata = req.Metadata ?? new Dictionary<string, object>(),
            Created = "2024-01-01T00:00:00Z",
            Modified = "2024-01-01T00:00:00Z",
            Version = 1
          };
          return script;
        });

      var script = new Script
      {
        Id = "script-1",
        Name = "Test Script",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);

      var sutWithSynthesis = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        mockAudioPlayer.Object);

      sutWithSynthesis.SelectedScript = scriptItem;

      await sutWithSynthesis.GenerateSegmentCommand.ExecuteAsync(segment);

      Assert.IsNotNull(capturedSynthesisRequest);
      Assert.AreEqual("Hello world", capturedSynthesisRequest.Text);
      Assert.AreEqual("profile-1", capturedSynthesisRequest.ProfileId);
      Assert.AreEqual("xtts", capturedSynthesisRequest.Engine);
      Assert.AreEqual("en", capturedSynthesisRequest.Language);

      Assert.IsNotNull(capturedRequest);
      Assert.AreEqual(1, capturedRequest.Segments?.Count);
      Assert.AreEqual("test-audio-1", capturedRequest.Segments![0].GeneratedAudioId);
      var updatedSegment = sutWithSynthesis.SelectedScript?.Segments.FirstOrDefault(s => s.Id == segment.Id);
      Assert.IsNotNull(updatedSegment);
      Assert.AreEqual("test-audio-1", updatedSegment.GeneratedAudioId);
      Assert.IsTrue(sutWithSynthesis.PlaySegmentCommand.CanExecute(updatedSegment));

      sutWithSynthesis.Dispose();
    }

    /// <summary>
    /// Proves that GenerateSegmentAsync persists visible edit buffer values (NewScriptName/NewScriptDescription),
    /// not stale SelectedScript values. If user edits name/description without saving, then generates,
    /// the persisted request must contain the edited values.
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_WhenEditFieldsAreModified_PersistsVisibleFieldValues()
    {
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "test-audio-1" });

      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hello world",
        VoiceProfileId = "profile-1"
      };
      var updatedScriptForReload = new Script
      {
        Id = "script-1",
        Name = "Edited Name",
        ProjectId = "proj-1",
        Description = "Edited Description",
        Segments = new List<ScriptSegment>
        {
          new ScriptSegment
          {
            Id = "seg-1",
            Text = "Hello world",
            VoiceProfileId = "profile-1",
            GeneratedAudioId = "test-audio-1"
          }
        },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };

      ScriptUpdateRequest? capturedRequest = null;
      _mockScriptEditorClient
        .Setup(x => x.GetScriptsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Script> { updatedScriptForReload });
      _mockScriptEditorClient
        .Setup(x => x.UpdateScriptAsync(It.IsAny<string>(), It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .Callback<string, ScriptUpdateRequest, CancellationToken>((_, req, _) => capturedRequest = req)
        .ReturnsAsync((string id, ScriptUpdateRequest req, CancellationToken _) =>
        {
          var script = new Script
          {
            Id = id,
            Name = req.Name ?? "Test",
            Description = req.Description,
            ProjectId = "proj-1",
            Segments = req.Segments ?? new List<ScriptSegment>(),
            Metadata = req.Metadata ?? new Dictionary<string, object>(),
            Created = "2024-01-01T00:00:00Z",
            Modified = "2024-01-01T00:00:00Z",
            Version = 1
          };
          return script;
        });

      var script = new Script
      {
        Id = "script-1",
        Name = "Test Script",
        Description = "Original Description",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);

      var sutWithSynthesis = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        audioPlayerService: null);

      sutWithSynthesis.SelectedScript = scriptItem;
      sutWithSynthesis.NewScriptName = "Edited Name";
      sutWithSynthesis.NewScriptDescription = "Edited Description";

      await sutWithSynthesis.GenerateSegmentCommand.ExecuteAsync(segment);

      Assert.IsNotNull(capturedRequest);
      Assert.AreEqual("Edited Name", capturedRequest.Name);
      Assert.AreEqual("Edited Description", capturedRequest.Description);

      sutWithSynthesis.Dispose();
    }

    /// <summary>
    /// Proves that after generation persistence (which includes reload), SelectedScript is in Scripts
    /// and SelectedSegment is in SelectedScript.Segments (post-reload rebind).
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_AfterReload_SelectedScriptAndSegmentAreInLiveCollection()
    {
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "test-audio-1" });

      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hello",
        VoiceProfileId = "profile-1"
      };
      var updatedScriptForReload = new Script
      {
        Id = "script-1",
        Name = "Test Script",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment>
        {
          new ScriptSegment
          {
            Id = "seg-1",
            Text = "Hello",
            VoiceProfileId = "profile-1",
            GeneratedAudioId = "test-audio-1"
          }
        },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };

      _mockScriptEditorClient
        .Setup(x => x.GetScriptsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Script> { updatedScriptForReload });
      _mockScriptEditorClient
        .Setup(x => x.UpdateScriptAsync("script-1", It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updatedScriptForReload);

      var script = new Script
      {
        Id = "script-1",
        Name = "Test Script",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);

      var sutWithSynthesis = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        audioPlayerService: null);

      sutWithSynthesis.SelectedScript = scriptItem;
      sutWithSynthesis.SelectedSegment = segment;

      await sutWithSynthesis.GenerateSegmentCommand.ExecuteAsync(segment);

      Assert.IsTrue(sutWithSynthesis.Scripts.Any(s => s.Id == sutWithSynthesis.SelectedScript?.Id));
      Assert.IsNotNull(sutWithSynthesis.SelectedScript);
      Assert.IsTrue(sutWithSynthesis.SelectedScript.Segments.Any(s => s.Id == sutWithSynthesis.SelectedSegment?.Id));
      Assert.IsNotNull(sutWithSynthesis.SelectedSegment);
      Assert.AreEqual("test-audio-1", sutWithSynthesis.SelectedSegment.GeneratedAudioId);

      sutWithSynthesis.Dispose();
    }

    /// <summary>
    /// Pass 04 C3: synthesis response without AudioId must not persist or imply success.
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_WhenSynthesisReturnsEmptyAudioId_DoesNotPersist()
    {
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = string.Empty });

      _mockScriptEditorClient
        .Setup(x => x.GetScriptsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Script>());

      var segment = new ScriptSegment { Id = "seg-1", Text = "Hello", VoiceProfileId = "profile-1" };
      var script = new Script
      {
        Id = "script-1",
        Name = "Test",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };

      var sut = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        audioPlayerService: null);
      sut.SelectedScript = new ScriptItem(script);

      await sut.GenerateSegmentCommand.ExecuteAsync(segment);

      _mockScriptEditorClient.Verify(
        x => x.UpdateScriptAsync(It.IsAny<string>(), It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
      Assert.IsFalse(string.IsNullOrEmpty(sut.ErrorMessage));

      sut.Dispose();
    }

    /// <summary>
    /// Pass 04: synthesis returns AudioId but no script is selected — must not persist; surfaces error.
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_WhenSynthesisReturnsAudio_ButNoSelectedScript_DoesNotPersist()
    {
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "orphan-audio" });

      var segment = new ScriptSegment { Id = "seg-1", Text = "Hello", VoiceProfileId = "profile-1" };

      var sut = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        audioPlayerService: null);
      sut.SelectedScript = null;

      await sut.GenerateSegmentCommand.ExecuteAsync(segment);

      _mockScriptEditorClient.Verify(
        x => x.UpdateScriptAsync(It.IsAny<string>(), It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
      Assert.IsFalse(string.IsNullOrEmpty(sut.ErrorMessage));

      sut.Dispose();
    }

    /// <summary>
    /// Pass 04 C1: repeat segment generation uses stored engine id when present.
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_UsesSegmentGenerationEngineId_WhenSet()
    {
      VoiceSynthesisRequest? captured = null;
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .Callback<VoiceSynthesisRequest, CancellationToken>((r, _) => captured = r)
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "a1" });

      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hi",
        VoiceProfileId = "p1",
        GenerationEngineId = "piper"
      };
      var updated = new Script
      {
        Id = "script-1",
        Name = "S",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment>
        {
          new ScriptSegment
          {
            Id = "seg-1",
            Text = "Hi",
            VoiceProfileId = "p1",
            GeneratedAudioId = "a1",
            GenerationEngineId = "piper"
          }
        },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      _mockScriptEditorClient
        .Setup(x => x.GetScriptsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Script> { updated });
      _mockScriptEditorClient
        .Setup(x => x.UpdateScriptAsync(It.IsAny<string>(), It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string id, ScriptUpdateRequest req, CancellationToken _) => new Script
        {
          Id = id,
          Name = req.Name ?? "S",
          ProjectId = "proj-1",
          Segments = req.Segments ?? new List<ScriptSegment>(),
          Metadata = req.Metadata ?? new Dictionary<string, object>(),
          Created = "2024-01-01T00:00:00Z",
          Modified = "2024-01-01T00:00:00Z",
          Version = 1
        });

      var script = new Script
      {
        Id = "script-1",
        Name = "S",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };

      var sut = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        audioPlayerService: null);
      sut.SelectedScript = new ScriptItem(script);

      await sut.GenerateSegmentCommand.ExecuteAsync(segment);

      Assert.IsNotNull(captured);
      Assert.AreEqual("piper", captured.Engine);

      sut.Dispose();
    }

    /// <summary>
    /// Pass 04 C1: script metadata supplies language for synthesis request.
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_UsesScriptMetadataLanguage_WhenSet()
    {
      VoiceSynthesisRequest? captured = null;
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .Callback<VoiceSynthesisRequest, CancellationToken>((r, _) => captured = r)
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "a1" });

      var segment = new ScriptSegment { Id = "seg-1", Text = "Hi", VoiceProfileId = "p1" };
      var updated = new Script
      {
        Id = "script-1",
        Name = "S",
        ProjectId = "proj-1",
        Metadata = new Dictionary<string, object> { ["language"] = "fr" },
        Segments = new List<ScriptSegment>
        {
          new ScriptSegment { Id = "seg-1", Text = "Hi", VoiceProfileId = "p1", GeneratedAudioId = "a1" }
        },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      _mockScriptEditorClient
        .Setup(x => x.GetScriptsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Script> { updated });
      _mockScriptEditorClient
        .Setup(x => x.UpdateScriptAsync(It.IsAny<string>(), It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string id, ScriptUpdateRequest req, CancellationToken _) => new Script
        {
          Id = id,
          Name = req.Name ?? "S",
          ProjectId = "proj-1",
          Segments = req.Segments ?? new List<ScriptSegment>(),
          Metadata = req.Metadata ?? new Dictionary<string, object>(),
          Created = "2024-01-01T00:00:00Z",
          Modified = "2024-01-01T00:00:00Z",
          Version = 1
        });

      var script = new Script
      {
        Id = "script-1",
        Name = "S",
        ProjectId = "proj-1",
        Metadata = new Dictionary<string, object> { ["language"] = "fr" },
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };

      var sut = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        audioPlayerService: null);
      sut.SelectedScript = new ScriptItem(script);

      await sut.GenerateSegmentCommand.ExecuteAsync(segment);

      Assert.IsNotNull(captured);
      Assert.AreEqual("fr", captured.Language);

      sut.Dispose();
    }

    /// <summary>
    /// Proves PlaySegmentCommand.ExecuteAsync actually invokes PlayBackendAudioIdAsync with the segment's GeneratedAudioId.
    /// </summary>
    [TestMethod]
    public async Task PlaySegmentCommand_ExecuteAsync_CallsPlayBackendAudioIdAsync()
    {
      var mockAudioPlayer = new Mock<IAudioPlayerService>();
      mockAudioPlayer
        .Setup(x => x.PlayBackendAudioIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action>()))
        .Returns(Task.CompletedTask);

      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hello",
        VoiceProfileId = "profile-1",
        GeneratedAudioId = "audio-123"
      };
      var script = new Script
      {
        Id = "script-1",
        Name = "Test",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);

      var sutWithAudio = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        voiceSynthesisService: null,
        mockAudioPlayer.Object);

      sutWithAudio.SelectedScript = scriptItem;

      await sutWithAudio.PlaySegmentCommand.ExecuteAsync(segment);

      mockAudioPlayer.Verify(
        x => x.PlayBackendAudioIdAsync("audio-123", "http://localhost:8000", It.IsAny<Action>()),
        Times.Once);

      sutWithAudio.Dispose();
    }

    /// <summary>
    /// When synthesis succeeds but UpdateScriptAsync fails, segment must not remain in fake success state.
    /// </summary>
    [TestMethod]
    public async Task GenerateSegmentAsync_WhenPersistFails_DoesNotMutateSegmentToSuccess()
    {
      var mockVoiceSynthesis = new Mock<IVoiceSynthesisService>();
      mockVoiceSynthesis
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "test-audio-1" });

      _mockScriptEditorClient
        .Setup(x => x.UpdateScriptAsync(It.IsAny<string>(), It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("Network error"));

      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hello",
        VoiceProfileId = "profile-1",
        GeneratedAudioId = null
      };
      var script = new Script
      {
        Id = "script-1",
        Name = "Test",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);

      var sut = new ScriptEditorViewModel(
        _context,
        _mockScriptEditorClient.Object,
        _mockDialogService.Object,
        mockVoiceSynthesis.Object,
        audioPlayerService: null);

      sut.SelectedScript = scriptItem;

      await sut.GenerateSegmentCommand.ExecuteAsync(segment);

      Assert.IsNull(segment.GeneratedAudioId);
      Assert.IsFalse(sut.PlaySegmentCommand.CanExecute(segment));

      sut.Dispose();
    }

    /// <summary>
    /// Proves that after UpdateScriptAsync + LoadScriptsAsync, SelectedScript is in Scripts
    /// and SelectedSegment is in SelectedScript.Segments (post-reload rebind).
    /// </summary>
    [TestMethod]
    public async Task UpdateScriptAsync_AfterReload_SelectedScriptAndSegmentAreInLiveCollection()
    {
      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hello",
        VoiceProfileId = "profile-1"
      };
      var script = new Script
      {
        Id = "script-1",
        Name = "Original Name",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);
      scriptItem.Name = "Updated Name";

      var updatedScript = new Script
      {
        Id = "script-1",
        Name = "Updated Name",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 2
      };

      _mockScriptEditorClient
        .Setup(x => x.GetScriptsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Script> { updatedScript });

      _mockScriptEditorClient
        .Setup(x => x.UpdateScriptAsync("script-1", It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updatedScript);

      _sut.Scripts.Add(scriptItem);
      _sut.SelectedScript = scriptItem;
      _sut.SelectedSegment = segment;

      await _sut.UpdateScriptCommand.ExecuteAsync(scriptItem);

      Assert.IsTrue(_sut.Scripts.Any(s => s.Id == _sut.SelectedScript?.Id));
      Assert.IsNotNull(_sut.SelectedScript);
      Assert.IsTrue(_sut.SelectedScript.Segments.Any(s => s.Id == _sut.SelectedSegment?.Id));
      Assert.IsNotNull(_sut.SelectedSegment);
    }

    /// <summary>
    /// Proves that after AddSegment (local mutation), SelectedScript is in Scripts
    /// and SelectedSegment is in SelectedScript.Segments (rebind after UpdateFrom).
    /// </summary>
    [TestMethod]
    public async Task AddSegment_AfterAdd_SelectedScriptAndSegmentCoherent()
    {
      var segment = new ScriptSegment
      {
        Id = "seg-1",
        Text = "Hello",
        VoiceProfileId = "profile-1"
      };
      var script = new Script
      {
        Id = "script-1",
        Name = "Test",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);
      var newSegment = new ScriptSegment
      {
        Id = "seg-2",
        Text = "New segment",
        VoiceProfileId = null
      };
      var updatedScript = new Script
      {
        Id = "script-1",
        Name = "Test",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { segment, newSegment },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 2
      };

      _mockScriptEditorClient
        .Setup(x => x.AddSegmentToScriptAsync("script-1", It.IsAny<ScriptSegment>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updatedScript);

      _sut.Scripts.Add(scriptItem);
      _sut.SelectedScript = scriptItem;
      _sut.SelectedSegment = segment;

      await _sut.AddSegmentCommand.ExecuteAsync(null);

      Assert.IsTrue(_sut.Scripts.Any(s => s.Id == _sut.SelectedScript?.Id));
      Assert.IsNotNull(_sut.SelectedScript);
      Assert.IsTrue(_sut.SelectedScript.Segments.Any(s => s.Id == _sut.SelectedSegment?.Id));
      Assert.IsNotNull(_sut.SelectedSegment);
      Assert.AreEqual("seg-1", _sut.SelectedSegment?.Id);
    }

    /// <summary>
    /// Proves that after RemoveSegment (local mutation), when removing a non-selected segment,
    /// SelectedSegment remains in SelectedScript.Segments.
    /// </summary>
    [TestMethod]
    public async Task RemoveSegment_WhenRemovingOtherSegment_SelectedSegmentRemainsCoherent()
    {
      var seg1 = new ScriptSegment { Id = "seg-1", Text = "One", VoiceProfileId = "p1" };
      var seg2 = new ScriptSegment { Id = "seg-2", Text = "Two", VoiceProfileId = "p1" };
      var script = new Script
      {
        Id = "script-1",
        Name = "Test",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { seg1, seg2 },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);

      _mockScriptEditorClient
        .Setup(x => x.RemoveSegmentFromScriptAsync("script-1", "seg-2", It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

      _sut.Scripts.Add(scriptItem);
      _sut.SelectedScript = scriptItem;
      _sut.SelectedSegment = seg1;

      await _sut.RemoveSegmentCommand.ExecuteAsync(seg2);

      Assert.IsTrue(_sut.Scripts.Any(s => s.Id == _sut.SelectedScript?.Id));
      Assert.IsNotNull(_sut.SelectedScript);
      Assert.IsTrue(_sut.SelectedScript.Segments.Any(s => s.Id == _sut.SelectedSegment?.Id));
      Assert.AreEqual("seg-1", _sut.SelectedSegment?.Id);
    }

    /// <summary>
    /// Proves that after RemoveSegment when removing the selected segment, SelectedSegment becomes null.
    /// </summary>
    [TestMethod]
    public async Task RemoveSegment_WhenRemovingSelectedSegment_ClearsSelectedSegment()
    {
      var seg1 = new ScriptSegment { Id = "seg-1", Text = "One", VoiceProfileId = "p1" };
      var script = new Script
      {
        Id = "script-1",
        Name = "Test",
        ProjectId = "proj-1",
        Segments = new List<ScriptSegment> { seg1 },
        Created = "2024-01-01T00:00:00Z",
        Modified = "2024-01-01T00:00:00Z",
        Version = 1
      };
      var scriptItem = new ScriptItem(script);

      _mockScriptEditorClient
        .Setup(x => x.RemoveSegmentFromScriptAsync("script-1", "seg-1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

      _sut.Scripts.Add(scriptItem);
      _sut.SelectedScript = scriptItem;
      _sut.SelectedSegment = seg1;

      await _sut.RemoveSegmentCommand.ExecuteAsync(seg1);

      Assert.IsTrue(_sut.Scripts.Any(s => s.Id == _sut.SelectedScript?.Id));
      Assert.IsNull(_sut.SelectedSegment);
    }

    #endregion
  }
}
