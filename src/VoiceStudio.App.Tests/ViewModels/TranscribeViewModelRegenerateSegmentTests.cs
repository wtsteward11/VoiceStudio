using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>
/// GAP-046: <see cref="TranscribeViewModel.RegenerateSegmentAudioAsync"/> surfaces a graceful message when the coordinator is not in DI (test harness / degraded init).
/// Full start-regeneration flow is covered by <see cref="Services.TranscriptSegmentRegenerationCoordinatorTests"/>.
/// </summary>
[TestClass]
public sealed class TranscribeViewModelRegenerateSegmentTests
{
  private DispatcherQueueController? _dispatcherController;

  [TestCleanup]
  public void Cleanup()
  {
    if (_dispatcherController != null)
    {
      _dispatcherController.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
      _dispatcherController = null;
    }
  }

  private static Mock<ITranscriptionClient> CreateTranscriptionClientMock()
  {
    var mock = new Mock<ITranscriptionClient>();
    mock.Setup(x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new System.Collections.Generic.List<TranscriptionEngine>());
    mock.Setup(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new System.Collections.Generic.List<SupportedLanguage>());
    return mock;
  }

  private static Mock<IProjectAudioClient> CreateProjectAudioMock()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock.Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "stub.wav" });
    return mock;
  }

  [TestMethod]
  public async Task RegenerateSegmentAudioAsync_WhenCoordinatorMissing_ReturnsGracefulMessage()
  {
    TestAppServicesHelper.EnsureInitialized();
    _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
    var context = new ViewModelContext(NullLogger.Instance, _dispatcherController.DispatcherQueue);
    var vm = new TranscribeViewModel(context, CreateTranscriptionClientMock().Object, CreateProjectAudioMock().Object);
    vm.SelectedTranscription = new TranscriptionResponse { Id = "tr-1" };
    var segment = new TranscriptionSegment { Id = "seg-1", Start = 0, End = 1 };

    var msg = await vm.RegenerateSegmentAudioAsync(segment, cancellationToken: CancellationToken.None).ConfigureAwait(false);

    StringAssert.Contains(msg, "unavailable");
    StringAssert.Contains(msg, "coordinator");
    Assert.IsTrue(vm.TranscriptApplyJobStatusEntries.Count >= 1);
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Failed, vm.TranscriptApplyJobStatusEntries[0].OperatorStatus);
    Assert.AreEqual("tr-1", vm.TranscriptApplyJobStatusEntries[0].TranscriptionId);
    Assert.AreEqual("seg-1", vm.TranscriptApplyJobStatusEntries[0].SegmentIds[0]);
    Assert.IsTrue(vm.TranscriptApplyJobStatusEntries[0].CanShowRetry);
  }
}
