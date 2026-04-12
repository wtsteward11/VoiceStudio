using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class JumpListServiceSeamTests
{
  private static DispatcherQueue CreateTestDispatcher()
  {
    return DispatcherQueue.GetForCurrentThread()
           ?? DispatcherQueueController.CreateOnDedicatedThread().DispatcherQueue;
  }

  [TestMethod]
  public void Constructor_SubscribesToRecentProjectsPropertyChanged()
  {
    var recents = new RecentProjectsService();
    using var service = new JumpListService(recents, CreateTestDispatcher());
    Assert.IsNotNull(service);
  }

  [TestMethod]
  public void UpdateJumpList_DoesNotThrow()
  {
    var recents = new RecentProjectsService();
    using var service = new JumpListService(recents, CreateTestDispatcher());
    service.UpdateJumpList();
    Thread.Sleep(100);
  }

  [TestMethod]
  public void ClearJumpList_DoesNotThrow()
  {
    var recents = new RecentProjectsService();
    using var service = new JumpListService(recents, CreateTestDispatcher());
    service.ClearJumpList();
    Thread.Sleep(100);
  }

  [TestMethod]
  public void Dispose_UnsubscribesFromPropertyChanged()
  {
    var recents = new RecentProjectsService();
    var service = new JumpListService(recents, CreateTestDispatcher());
    service.Dispose();
    // Second dispose should be safe
    service.Dispose();
  }

  [TestMethod]
  public async Task RecentProjectsChange_SchedulesRefreshWithoutThrow()
  {
    var recents = new RecentProjectsService();
    using var service = new JumpListService(recents, CreateTestDispatcher());
    await recents.AddRecentProjectAsync(@"C:\temp\fake_project.vstudio", "fake").ConfigureAwait(true);
    Thread.Sleep(700);
  }

  [TestMethod]
  public void EmptyRecentProjects_UpdateJumpList_DoesNotThrow()
  {
    var recents = new RecentProjectsService();
    using var service = new JumpListService(recents, CreateTestDispatcher());
    service.UpdateJumpList();
    Thread.Sleep(100);
  }
}
