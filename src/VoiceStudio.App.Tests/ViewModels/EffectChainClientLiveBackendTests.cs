using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Live-backend proof for EffectChain CRUD:
  /// create → get → update → delete through <see cref="EffectChainClient"/>
  /// hitting the real FastAPI backend on 127.0.0.1:8000.
  /// Skips with Inconclusive when no backend is running.
  /// </summary>
  [TestClass]
  [TestCategory("LiveBackend")]
  public sealed class EffectChainClientLiveBackendTests
  {
    private const string BackendBase = "http://127.0.0.1:8000";
    private const string TestProjectId = "csharp-live-effects-test";

    [TestMethod]
    public async Task EffectChainCrud_LiveBackend_CreateGetUpdateDelete()
    {
      TestAppServicesHelper.EnsureInitialized();

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
      try
      {
        using var health = await probe.GetAsync(
          new Uri(new Uri(BackendBase), "/api/health"),
          CancellationToken.None).ConfigureAwait(false);

        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /health returned {(int)health.StatusCode}; start backend first.");
        }
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      var jsonOptions = JsonSerializerOptionsFactory.BackendApi;
      using var httpClient = new HttpClient
      {
        BaseAddress = new Uri(BackendBase),
        Timeout = TimeSpan.FromSeconds(30),
      };
      var pipeline = new BackendClientHttpPipeline(httpClient, jsonOptions);
      var client = new EffectChainClient(pipeline);

      // 1. Create
      var newChain = new EffectChain
      {
        Name = "Live Backend Test Chain",
        Description = "Created by C# live-backend test",
      };
      var created = await client.CreateEffectChainAsync(TestProjectId, newChain, CancellationToken.None);
      Assert.IsNotNull(created, "CreateEffectChainAsync returned null");
      Assert.IsFalse(string.IsNullOrEmpty(created.Id), "Created chain should have an Id");
      Assert.AreEqual("Live Backend Test Chain", created.Name);
      Assert.AreEqual(TestProjectId, created.ProjectId);

      try
      {
        // 2. Get
        var fetched = await client.GetEffectChainAsync(TestProjectId, created.Id, CancellationToken.None);
        Assert.IsNotNull(fetched, "GetEffectChainAsync returned null");
        Assert.AreEqual(created.Id, fetched.Id);
        Assert.AreEqual("Live Backend Test Chain", fetched.Name);

        // 3. Update
        var updated = new EffectChain
        {
          Name = "Updated Live Chain",
          Description = "Updated by C# live-backend test",
        };
        var afterUpdate = await client.UpdateEffectChainAsync(TestProjectId, created.Id, updated, CancellationToken.None);
        Assert.IsNotNull(afterUpdate, "UpdateEffectChainAsync returned null");
        Assert.AreEqual("Updated Live Chain", afterUpdate.Name);
        Assert.AreEqual(created.Id, afterUpdate.Id);

        // 4. Delete
        var deleted = await client.DeleteEffectChainAsync(TestProjectId, created.Id, CancellationToken.None);
        Assert.IsTrue(deleted, "DeleteEffectChainAsync should return true");
      }
      catch (Exception)
      {
        // Cleanup: attempt to delete the chain even if test fails mid-way
        try
        {
          await client.DeleteEffectChainAsync(TestProjectId, created.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine(
            $"EffectChain live test cleanup delete failed (non-fatal): {ex.Message}");
        }

        throw;
      }
    }

    [TestMethod]
    public async Task GetEffectChains_LiveBackend_ReturnsListForProject()
    {
      TestAppServicesHelper.EnsureInitialized();

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
      try
      {
        using var health = await probe.GetAsync(
          new Uri(new Uri(BackendBase), "/api/health"),
          CancellationToken.None).ConfigureAwait(false);

        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /health returned {(int)health.StatusCode}; start backend first.");
        }
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      var jsonOptions = JsonSerializerOptionsFactory.BackendApi;
      using var httpClient = new HttpClient
      {
        BaseAddress = new Uri(BackendBase),
        Timeout = TimeSpan.FromSeconds(30),
      };
      var pipeline = new BackendClientHttpPipeline(httpClient, jsonOptions);
      var client = new EffectChainClient(pipeline);

      var chains = await client.GetEffectChainsAsync(TestProjectId, CancellationToken.None);
      Assert.IsNotNull(chains, "GetEffectChainsAsync returned null");
    }
  }
}
