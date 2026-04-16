using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Optional live-backend proof: same HTTP truth as the app, through
  /// <see cref="ProfilesViewModel.OnActivatedAsync"/> + real <see cref="BackendClient"/>.
  /// Skips with Inconclusive when nothing listens on 127.0.0.1:8000 (CI / laptop without backend).
  /// </summary>
  [TestClass]
  [TestCategory("LiveBackend")]
  public sealed class ProfilesRuntimeLiveBackendTests
  {
    private const string BackendBase = "http://127.0.0.1:8000";

    private static ProfilesViewModel CreateVm(IProfilesClient client, IProfilesUseCase useCase)
    {
      return new ProfilesViewModel(
        client,
        useCase,
        new AudioPlayerService(new HttpClient()),
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

    [TestMethod]
    public async Task OnActivatedAsync_LiveBackend_ViewModelCountsAndFooterMatchApiItems()
    {
      TestAppServicesHelper.EnsureInitialized();
      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
      int apiItemsCount;
      try
      {
        using var health = await probe.GetAsync(new Uri(new Uri(BackendBase), "/health"), CancellationToken.None).ConfigureAwait(false);
        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /health returned {(int)health.StatusCode}; start backend with .\\scripts\\start_backend.ps1");
        }

        using var profilesResp = await probe.GetAsync(new Uri(new Uri(BackendBase), "/api/profiles"), CancellationToken.None).ConfigureAwait(false);
        if (!profilesResp.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /api/profiles returned {(int)profilesResp.StatusCode}.");
        }

        var body = await profilesResp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
        {
          Assert.Inconclusive("Backend /api/profiles JSON missing items array.");
        }

        apiItemsCount = itemsEl.GetArrayLength();
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      var coordinator = new RequestCoordinator();
      var config = new BackendClientConfig
      {
        BaseUrl = BackendBase,
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromSeconds(30),
      };
      using var backend = new BackendClient(config, correlationProvider: null, requestCoordinator: coordinator);
      var profilesClient = new ProfilesClient(backend, coordinator);
      var useCase = new ProfilesUseCase(profilesClient);

      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Dispatcher required");

      var tcs = new TaskCompletionSource<(int Total, int Filtered, int Profiles, string Footer)>(
        TaskCreationOptions.RunContinuationsAsynchronously);

      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = CreateVm(profilesClient, useCase);
          await vm.OnActivatedAsync(CancellationToken.None).ConfigureAwait(true);
          await Task.Delay(300).ConfigureAwait(true);
          tcs.TrySetResult((vm.TotalProfiles, vm.FilteredCount, vm.Profiles.Count, vm.FooterSummary));
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      var completed = await Task.WhenAny(tcs.Task, Task.Delay(15000)).ConfigureAwait(false);
      if (completed != tcs.Task)
      {
        Assert.Fail("Timed out waiting for ViewModel activation on dispatcher.");
      }

      var (total, filtered, profiles, footer) = await tcs.Task.ConfigureAwait(false);

      Assert.AreEqual(apiItemsCount, profiles, "Profiles collection count should match API items length.");
      Assert.AreEqual(apiItemsCount, total, "TotalProfiles should match API items length (no filters on cold activation).");
      Assert.AreEqual(apiItemsCount, filtered, "FilteredCount should match API items length (no filters on cold activation).");
      Assert.AreEqual($"{apiItemsCount} of {apiItemsCount} profiles", footer, "FooterSummary should match API-backed counts.");
    }
  }
}
