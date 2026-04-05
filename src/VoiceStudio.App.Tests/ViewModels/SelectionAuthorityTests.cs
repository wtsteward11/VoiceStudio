using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Features.Synthesis;
using VoiceStudio.App.Features.VoiceProfile;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Gateways;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>
/// GOV-VOICESTUDIO-SELECTION-AUTHORITY-01 — deterministic proof: canonical <see cref="ProfileSelectedEvent"/> drives Features synthesis VM.
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public sealed class SelectionAuthorityTests
{
  [TestMethod]
  public async Task ProfileSelectedEvent_UpdatesFeaturesSynthesisViewModel_SelectedVoice()
  {
    TestAppServicesHelper.EnsureInitialized();
    var dq = TestAppServicesHelper.GetDispatcher();
    Assert.IsNotNull(dq);

    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    dq.TryEnqueue(() =>
    {
      try
      {
        var mockVoice = new Mock<IVoiceGateway>();
        var mockEngine = new Mock<IEngineGateway>();
        mockEngine.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
          .Returns(Task.FromResult(
            GatewayResult<IReadOnlyList<VoiceStudio.Core.Gateways.EngineInfo>>.Ok(
              Array.Empty<VoiceStudio.Core.Gateways.EngineInfo>())));

        var sut = new SynthesisViewModel(
          new MockViewModelContext(),
          mockVoice.Object,
          mockEngine.Object);

        sut.AvailableVoices.Add(new VoiceProfileData { Id = "pv1", Name = "One" });

        var agg = AppServices.TryGetEventAggregator();
        Assert.IsNotNull(agg);
        agg.Publish(new ProfileSelectedEvent(PanelIds.Library, "pv1", "One"));

        Assert.AreEqual("pv1", sut.SelectedVoice?.Id, "Features Synthesis should follow ProfileSelectedEvent");

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
}
