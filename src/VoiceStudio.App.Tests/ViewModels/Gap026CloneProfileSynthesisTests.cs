using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>GAP-026 — clone finalize → profile selection → synthesis activation sync.</summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class Gap026CloneProfileSynthesisTests
  {
    [TestCleanup]
    public void TestCleanup()
    {
      TestAppServicesHelper.RebuildDefaultProvider();
    }

    [TestMethod]
    public async Task VoiceSynthesisViewModel_OnActivated_SyncsActiveProfileFromContextManager()
    {
      var mockCtx = new Mock<IContextManager>();
      mockCtx.Setup(c => c.ActiveProfileId).Returns("p-ctx");
      mockCtx.Setup(c => c.ActiveProfileName).Returns("Context Voice");
      TestAppServicesHelper.EnsureInitializedWithContextManager(mockCtx.Object);

      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var mockVoice = new Mock<IVoiceSynthesisService>();
          var mockEngines = new Mock<IEnginesClient>();
          mockEngines.Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
          var mockQp = new Mock<IQualityPipelineService>();
          mockQp.Setup(x => x.ListQualityPipelinePresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
          var mockEns = new Mock<IEnsembleService>();
          var mockTxt = new Mock<ITextAnalysisService>();
          var mockQh = new Mock<IQualityHistoryService>();
          var mockProfilesClient = new Mock<IProfilesClient>();
          mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoiceProfile>
            {
              new() { Id = "p-ctx", Name = "Context Voice" }
            });
          var mockAudio = new Mock<IAudioPlayerService>();

          var sut = new VoiceSynthesisViewModel(
            mockVoice.Object,
            mockEngines.Object,
            mockQp.Object,
            mockEns.Object,
            mockTxt.Object,
            mockQh.Object,
            mockProfilesClient.Object,
            mockAudio.Object);

          await sut.LoadProfilesCommand.ExecuteAsync(null);
          await sut.OnActivatedAsync(CancellationToken.None);

          await Task.Delay(400);
          Assert.AreEqual("p-ctx", sut.SelectedProfile?.Id, "GAP-026: activation should mirror IContextManager.ActiveProfileId");

          await sut.OnDeactivatedAsync(CancellationToken.None);
          sut.Dispose();
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task VoiceSynthesisViewModel_OnActivated_NoActiveProfileId_KeepsSelectionNull()
    {
      var mockCtx = new Mock<IContextManager>();
      mockCtx.Setup(c => c.ActiveProfileId).Returns((string?)null);
      mockCtx.Setup(c => c.ActiveProfileName).Returns((string?)null);
      TestAppServicesHelper.EnsureInitializedWithContextManager(mockCtx.Object);

      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var mockVoice = new Mock<IVoiceSynthesisService>();
          var mockEngines = new Mock<IEnginesClient>();
          mockEngines.Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
          var mockQp = new Mock<IQualityPipelineService>();
          mockQp.Setup(x => x.ListQualityPipelinePresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
          var mockEns = new Mock<IEnsembleService>();
          var mockTxt = new Mock<ITextAnalysisService>();
          var mockQh = new Mock<IQualityHistoryService>();
          var mockProfilesClient = new Mock<IProfilesClient>();
          mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoiceProfile> { new() { Id = "p-only", Name = "Only" } });
          var mockAudio = new Mock<IAudioPlayerService>();

          var sut = new VoiceSynthesisViewModel(
            mockVoice.Object,
            mockEngines.Object,
            mockQp.Object,
            mockEns.Object,
            mockTxt.Object,
            mockQh.Object,
            mockProfilesClient.Object,
            mockAudio.Object);

          await sut.LoadProfilesCommand.ExecuteAsync(null);
          await sut.OnActivatedAsync(CancellationToken.None);

          Assert.IsNull(sut.SelectedProfile);
          await sut.OnDeactivatedAsync(CancellationToken.None);
          sut.Dispose();
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task FinalizeWizard_OnSuccess_PublishesProfileCreatedThenProfileSelected()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);

          var types = new List<Type>();
          ProfileCreatedEvent? created = null;
          ProfileSelectedEvent? selected = null;
          using (agg.Subscribe<ProfileCreatedEvent>(e =>
          {
            types.Add(typeof(ProfileCreatedEvent));
            created = e;
          }))
          {
            using (agg.Subscribe<ProfileSelectedEvent>(e =>
            {
              types.Add(typeof(ProfileSelectedEvent));
              selected = e;
            }))
            {
              var mockClient = new Mock<IVoiceCloningWizardClient>();
              mockClient.Setup(x => x.FinalizeWizardAsync(
                    It.IsAny<string>(),
                    It.IsAny<VoiceCloningWizardFinalizeRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VoiceCloningWizardFinalizeResponse
                {
                  Success = true,
                  ProfileId = "p-new",
                  ProfileName = "Cloned"
                });

              var context = AppServices.GetRequiredService<IViewModelContext>();
              var sut = new VoiceCloningWizardViewModel(context, mockClient.Object);
              sut.WizardJobId = "job-1";
              sut.CurrentStep = 4;
              sut.ProcessingStatus = "completed";
              sut.CreatedProfileId = "p-new";
              sut.FinalizeWizardCommand.NotifyCanExecuteChanged();
              await sut.FinalizeWizardCommand.ExecuteAsync(default);
            }
          }

          Assert.AreEqual(2, types.Count);
          Assert.AreEqual(typeof(ProfileCreatedEvent), types[0]);
          Assert.AreEqual(typeof(ProfileSelectedEvent), types[1]);
          Assert.IsNotNull(created);
          Assert.AreEqual("p-new", created!.ProfileId);
          Assert.IsNotNull(selected);
          Assert.AreEqual("p-new", selected!.ProfileId);
          Assert.AreEqual(InteractionIntent.ImmediateUse, selected.Intent);

          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task FinalizeWizard_OnFailure_DoesNotPublishProfileEvents()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);

          var count = 0;
          using (agg.Subscribe<ProfileCreatedEvent>(_ => count++))
          {
            using (agg.Subscribe<ProfileSelectedEvent>(_ => count++))
            {
              var mockClient = new Mock<IVoiceCloningWizardClient>();
              mockClient.Setup(x => x.FinalizeWizardAsync(
                    It.IsAny<string>(),
                    It.IsAny<VoiceCloningWizardFinalizeRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VoiceCloningWizardFinalizeResponse { Success = false, ProfileId = "", ProfileName = "" });

              var context = AppServices.GetRequiredService<IViewModelContext>();
              var sut = new VoiceCloningWizardViewModel(context, mockClient.Object);
              sut.WizardJobId = "job-1";
              sut.CurrentStep = 4;
              sut.ProcessingStatus = "completed";
              sut.CreatedProfileId = "p-x";
              sut.FinalizeWizardCommand.NotifyCanExecuteChanged();
              await sut.FinalizeWizardCommand.ExecuteAsync(default);
            }
          }

          Assert.AreEqual(0, count);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }
  }
}
