#nullable enable

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.Controls;

/// <summary>
/// GAP-013 behavioral coverage for panel VM teardown (deactivate before dispose).
/// Full <see cref="PanelHost"/> UI transitions are not instantiated here (WinUI ctor); static
/// <c>DeactivateViewModelThenDisposeAsync</c> is the eviction/unload primitive.
/// Invoked via reflection because <c>VoiceStudio.App</c> uses <c>GenerateAssemblyInfo=false</c> and does not emit
/// <c>InternalsVisibleTo</c> from the csproj.
/// </summary>
[TestClass]
public sealed class PanelHostLifecycleTests
{
  private static async Task InvokeDeactivateViewModelThenDisposeAsync(object? viewModel, CancellationToken cancellationToken)
  {
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var method = typeof(PanelHost).GetMethod("DeactivateViewModelThenDisposeAsync", flags);
    Assert.IsNotNull(method, "PanelHost.DeactivateViewModelThenDisposeAsync must exist.");
    var taskObj = method.Invoke(null, new object?[] { viewModel, cancellationToken });
    Assert.IsInstanceOfType(taskObj, typeof(Task));
    await ((Task)taskObj).ConfigureAwait(false);
  }

  [TestMethod]
  public async Task DeactivateViewModelThenDisposeAsync_CallsDeactivateBeforeDispose()
  {
    var vm = new OrderTrackingLifecycleVm();
    await InvokeDeactivateViewModelThenDisposeAsync(vm, CancellationToken.None);
    Assert.AreEqual(1, vm.DeactivateCalls, "OnDeactivatedAsync should run once.");
    Assert.IsTrue(vm.Disposed, "Dispose should run.");
    Assert.IsTrue(vm.DeactivateRanBeforeDispose, "Deactivate must run before IDisposable.Dispose.");
  }

  [TestMethod]
  public async Task DeactivateViewModelThenDisposeAsync_NullViewModel_Completes()
  {
    await InvokeDeactivateViewModelThenDisposeAsync(null, CancellationToken.None);
  }

  [TestMethod]
  public async Task DeactivateViewModelThenDisposeAsync_NonLifecycleDisposable_Disposes()
  {
    var vm = new PlainDisposableVm();
    await InvokeDeactivateViewModelThenDisposeAsync(vm, CancellationToken.None);
    Assert.IsTrue(vm.Disposed);
  }

  private sealed class OrderTrackingLifecycleVm : IPanelLifecycle, IDisposable
  {
    public int DeactivateCalls { get; private set; }
    public bool Disposed { get; private set; }
    public bool DeactivateRanBeforeDispose { get; private set; }

    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
      DeactivateCalls++;
      return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
      DeactivateRanBeforeDispose = DeactivateCalls >= 1;
      Disposed = true;
    }
  }

  private sealed class PlainDisposableVm : IDisposable
  {
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
  }
}
