using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Live-backend proof for Library: probes /api/library/assets via HTTP, then
  /// runs <see cref="LibraryViewModel.OnActivatedAsync"/> through the real
  /// <see cref="BackendClient"/> and asserts ViewModel counts match API totals.
  /// Skips with Inconclusive when no backend listens on 127.0.0.1:8000.
  /// </summary>
  [TestClass]
  [TestCategory("LiveBackend")]
  public sealed class LibraryRuntimeLiveBackendTests
  {
    private const string BackendBase = "http://127.0.0.1:8000";

    [TestMethod]
    public async Task OnActivatedAsync_LiveBackend_AssetsAndEmptyStateMatchApi()
    {
      TestAppServicesHelper.EnsureInitialized();

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
      int apiTotalAssets;

      try
      {
        using var health = await probe.GetAsync(
          new Uri(new Uri(BackendBase), "/health"),
          CancellationToken.None).ConfigureAwait(false);

        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /health returned {(int)health.StatusCode}; start backend first.");
        }

        using var assetsResp = await probe.GetAsync(
          new Uri(new Uri(BackendBase), "/api/library/assets?limit=1"),
          CancellationToken.None).ConfigureAwait(false);

        if (!assetsResp.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /api/library/assets returned {(int)assetsResp.StatusCode}.");
        }

        var body = await assetsResp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("total", out var totalEl))
        {
          Assert.Inconclusive("Backend /api/library/assets JSON missing 'total' field.");
        }

        apiTotalAssets = totalEl.GetInt32();
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
      var libraryClient = new LibraryClient(backend);

      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq, "Dispatcher required");

      var context = AppServices.GetService<IViewModelContext>();
      Assert.IsNotNull(context, "IViewModelContext required");

      var mockDialog = new Mock<IDialogService>();

      var tcs = new TaskCompletionSource<(int Total, int AssetsCount, bool ShowEmpty)>(
        TaskCreationOptions.RunContinuationsAsynchronously);

      dq.TryEnqueue(async () =>
      {
        try
        {
          var vm = new LibraryViewModel(context, libraryClient, mockDialog.Object);
          await vm.OnActivatedAsync(CancellationToken.None).ConfigureAwait(true);
          await Task.Delay(500).ConfigureAwait(true);
          tcs.TrySetResult((vm.TotalAssets, vm.Assets.Count, vm.ShowEmptyState));
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      var completed = await Task.WhenAny(tcs.Task, Task.Delay(20000)).ConfigureAwait(false);
      if (completed != tcs.Task)
      {
        Assert.Fail("Timed out waiting for LibraryViewModel activation on dispatcher.");
      }

      var (total, assetsCount, showEmpty) = await tcs.Task.ConfigureAwait(false);

      Assert.AreEqual(apiTotalAssets, total,
        $"TotalAssets should match API total. API={apiTotalAssets}, VM={total}");

      if (apiTotalAssets > 0)
      {
        Assert.IsTrue(assetsCount > 0,
          $"Assets collection should not be empty when API reports {apiTotalAssets} assets.");
        Assert.IsFalse(showEmpty,
          "ShowEmptyState should be false when assets exist.");
      }
      else
      {
        Assert.AreEqual(0, assetsCount,
          "Assets collection should be empty when API reports 0 assets.");
        Assert.IsTrue(showEmpty,
          "ShowEmptyState should be true when no assets exist.");
      }
    }
  }
}
