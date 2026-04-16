using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam tests for ProfilesViewModel — Pass 07 W7-C1 (ProfileCreatedEvent → select profile).
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ProfilesViewModelSeamTests
  {
    private static IProfileQualityInsightsService CreateMockQualityInsights()
    {
      var mock = new Mock<IProfileQualityInsightsService>();
      mock.Setup(x => x.LoadQualityHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<QualityHistoryEntry>());
      mock.Setup(x => x.LoadQualityTrendsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new QualityTrends());
      mock.Setup(x => x.LoadQualityBaselineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((QualityBaseline?)null);
      mock.Setup(x => x.GetQualityDegradationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((QualityDegradationResponse?)null);
      return mock.Object;
    }

    private static IProfileTransferService CreateMockTransfer()
    {
      var mock = new Mock<IProfileTransferService>();
      mock.Setup(x => x.ParseImports(It.IsAny<string>())).Returns((new List<ProfileImportData>(), (string?)null));
      mock.Setup(x => x.CreateProfilesFromImportDataAsync(It.IsAny<IReadOnlyList<ProfileImportData>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
      mock.Setup(x => x.BuildExportJson(It.IsAny<IEnumerable<VoiceProfile>>())).Returns("{}");
      mock.Setup(x => x.SanitizeFilename(It.IsAny<string?>())).Returns((string? v) => string.IsNullOrWhiteSpace(v) ? "profile_export" : v!);
      return mock.Object;
    }

    private static IProfileEnhancementService CreateMockEnhancement()
    {
      var mock = new Mock<IProfileEnhancementService>();
      mock.Setup(x => x.EnhanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((ReferenceAudioPreprocessResponse?)null);
      return mock.Object;
    }

    private static ProfilesViewModel CreateProfilesViewModel(
      IProfilesClient profilesClient,
      IProfilesUseCase useCase)
    {
      return new ProfilesViewModel(
        profilesClient,
        useCase,
        new AudioPlayerService(new System.Net.Http.HttpClient()),
        new MultiSelectService(),
        CreateMockQualityInsights(),
        CreateMockTransfer(),
        CreateMockEnhancement(),
        toastNotificationService: null,
        undoRedoService: new UndoRedoService(),
        errorService: null,
        logService: null,
        dialogService: null,
        previewService: null);
    }

    [TestMethod]
    public async Task ProfileCreatedEvent_FromTraining_AfterReload_SelectsNewProfile()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var listCalls = 0;
      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(() =>
        {
          listCalls++;
          if (listCalls == 1)
          {
            return new List<VoiceProfile> { new() { Id = "p-old", Name = "Old" } };
          }

          return new List<VoiceProfile>
          {
            new() { Id = "p-old", Name = "Old" },
            new() { Id = "p-trained", Name = "Trained" }
          };
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          await vm.LoadProfilesCommand.ExecuteAsync(null);
          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new ProfileCreatedEvent(PanelIds.Training, "p-trained", "Trained"));
          await Task.Delay(500);
          Assert.AreEqual("p-trained", vm.SelectedProfile?.Id, "W7-C1: trained profile should be selected after event");
          await vm.OnDeactivatedAsync(CancellationToken.None);
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
    public async Task ProfileCreatedEvent_WhenProfileAlreadyInList_SelectsWithoutSecondList()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>
        {
          new() { Id = "p-old", Name = "Old" },
          new() { Id = "p-trained", Name = "Trained" }
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          await vm.LoadProfilesCommand.ExecuteAsync(null);
          mockUseCase.Verify(u => u.ListAsync(It.IsAny<CancellationToken>()), Times.Once);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new ProfileCreatedEvent(PanelIds.Training, "p-trained", "Trained"));
          await Task.Delay(200);

          Assert.AreEqual("p-trained", vm.SelectedProfile?.Id, "W7-C1: should select existing profile without reload");
          mockUseCase.Verify(u => u.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
          await vm.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>Bounded Slice 3: <see cref="IPanelLifecycle.OnActivatedAsync"/> loads profiles when the panel activates with an empty cache (PanelHost contract).</summary>
    [TestMethod]
    public async Task OnActivatedAsync_WhenEmpty_LoadsProfilesOnce()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var listCalls = 0;
      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(() =>
        {
          listCalls++;
          return new List<VoiceProfile> { new() { Id = "p1", Name = "One" } };
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          Assert.AreEqual(0, vm.Profiles.Count, "Precondition: no profiles before activation");

          await vm.OnActivatedAsync(CancellationToken.None);

          Assert.AreEqual(1, listCalls, "ListAsync should run once on first activation");
          Assert.AreEqual(1, vm.Profiles.Count, "Profiles should populate after activation");
          mockUseCase.Verify(u => u.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
          await vm.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>Bounded Slice 3: Second activation does not refetch when data is already present (avoids redundant load).</summary>
    [TestMethod]
    public async Task OnActivatedAsync_WhenAlreadyLoaded_SkipsSecondFetch()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var listCalls = 0;
      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(() =>
        {
          listCalls++;
          return new List<VoiceProfile> { new() { Id = "p1", Name = "One" } };
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          await vm.OnActivatedAsync(CancellationToken.None);
          Assert.AreEqual(1, listCalls);

          await vm.OnActivatedAsync(CancellationToken.None);
          Assert.AreEqual(1, listCalls, "Second activation should not call ListAsync when Profiles is non-empty");

          await vm.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>Runtime proof hardening: first load fails; second activation retries because Profiles stays empty.</summary>
    [TestMethod]
    public async Task OnActivatedAsync_AfterFailedLoad_RetriesOnNextActivation()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var listCalls = 0;
      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .Returns(() =>
        {
          listCalls++;
          if (listCalls == 1)
          {
            return Task.FromException<IReadOnlyList<VoiceProfile>>(new InvalidOperationException("simulated network failure"));
          }

          return Task.FromResult<IReadOnlyList<VoiceProfile>>(
              new List<VoiceProfile> { new() { Id = "recovered", Name = "Recovered" } });
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);

          await vm.OnActivatedAsync(CancellationToken.None);
          Assert.AreEqual(0, vm.Profiles.Count, "After failed load, Profiles should stay empty");
          Assert.IsFalse(string.IsNullOrEmpty(vm.ErrorMessage), "ErrorMessage should surface load failure");

          await vm.OnActivatedAsync(CancellationToken.None);
          Assert.AreEqual(2, listCalls, "Second activation should call ListAsync again");
          Assert.IsTrue(vm.Profiles.Count > 0, "Recovery load should populate Profiles");
          Assert.AreEqual("recovered", vm.Profiles[0].Id);

          await vm.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>Runtime proof hardening: TotalProfiles and FilteredCount match collection after activation (footer bindings).</summary>
    [TestMethod]
    public async Task OnActivatedAsync_AfterLoad_SetsCountProperties()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>
        {
          new() { Id = "a", Name = "A", Language = "en" },
          new() { Id = "b", Name = "B", Language = "en" },
          new() { Id = "c", Name = "C", Language = "en" },
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          await vm.OnActivatedAsync(CancellationToken.None);

          Assert.AreEqual(3, vm.Profiles.Count);
          Assert.AreEqual(3, vm.TotalProfiles, "TotalProfiles should match Profiles.Count for footer");
          Assert.AreEqual(3, vm.FilteredCount, "FilteredCount should match with no active filters");

          await vm.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>Runtime proof hardening: empty API list yields honest empty state (ShowEmptyState).</summary>
    [TestMethod]
    public async Task OnActivatedAsync_EmptyResponse_ShowsEmptyState()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          await vm.OnActivatedAsync(CancellationToken.None);

          Assert.AreEqual(0, vm.Profiles.Count);
          Assert.AreEqual(0, vm.TotalProfiles);
          Assert.AreEqual(0, vm.FilteredCount);
          Assert.IsTrue(vm.ShowEmptyState, "ShowEmptyState should be true when there are no profiles and no error");

          await vm.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>Runtime proof hardening: search narrows FilteredCount but TotalProfiles stays full catalog size.</summary>
    [TestMethod]
    public async Task ApplyFilters_WithSearchQuery_PreservesTotalCount()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>
        {
          new() { Id = "1", Name = "Alpha", Language = "en" },
          new() { Id = "2", Name = "Beta", Language = "en" },
          new() { Id = "3", Name = "Gamma", Language = "en" },
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          await vm.OnActivatedAsync(CancellationToken.None);
          Assert.AreEqual(3, vm.TotalProfiles);
          Assert.AreEqual(3, vm.FilteredCount);

          vm.SearchQuery = "Alpha";
          await Task.Delay(600);

          Assert.AreEqual(3, vm.TotalProfiles, "Total catalog size must not shrink when searching");
          Assert.AreEqual(1, vm.FilteredCount, "Only Alpha should match search");
          Assert.AreEqual(1, vm.FilteredProfiles.Count);
          Assert.AreEqual("Alpha", vm.FilteredProfiles[0].Name);

          await vm.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>GAP-028: ProfileUpdatedEvent from Training triggers list reload for fresh metadata.</summary>
    [TestMethod]
    public async Task ProfileUpdatedEvent_FromTraining_TriggersSecondListLoad()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Test dispatcher required");

      var listCalls = 0;
      var mockUseCase = new Mock<IProfilesUseCase>();
      mockUseCase.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(() =>
        {
          listCalls++;
          return new List<VoiceProfile>
          {
            new() { Id = "p1", Name = "One" },
          };
        });

      var mockClient = new Mock<IProfilesClient>();
      mockClient.Setup(c => c.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateProfilesViewModel(mockClient.Object, mockUseCase.Object);
          await vm.LoadProfilesCommand.ExecuteAsync(null);
          Assert.AreEqual(1, listCalls, "Initial load");

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new ProfileUpdatedEvent(
              PanelIds.Training,
              "p1",
              new Dictionary<string, object>
              {
                  ["training_completed"] = true,
                  ["training_job_id"] = "job-1",
              }));
          await Task.Delay(500);

          Assert.IsTrue(listCalls >= 2, "GAP-028: ProfileUpdated should enqueue LoadProfilesAsync");
          await vm.OnDeactivatedAsync(CancellationToken.None);
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
