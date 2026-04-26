using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowRecentProjectsMutationBridgeTests
{
    [TestMethod]
    public async Task PinRecentProjectAsync_invokes_service_then_success_toast()
    {
        const string path = @"C:\proj\a.vsproj";
        var mutations = new Mock<IRecentProjectsMutationCommands>(MockBehavior.Strict);
        mutations.Setup(m => m.PinProjectAsync(path)).Returns(Task.CompletedTask).Verifiable();
        var toast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        toast.Setup(t => t.ShowToast(
                ToastType.Success,
                "Project Pinned",
                "Project pinned to Recent Projects menu"))
            .Verifiable();
        var bridge = new MainWindowRecentProjectsMutationBridge(() => mutations.Object, () => toast.Object);

        await bridge.PinRecentProjectAsync(path).ConfigureAwait(false);

        mutations.Verify();
        toast.Verify();
    }

    [TestMethod]
    public async Task PinRecentProjectAsync_on_exception_shows_error_toast()
    {
        const string path = @"C:\p";
        var mutations = new Mock<IRecentProjectsMutationCommands>(MockBehavior.Strict);
        mutations.Setup(m => m.PinProjectAsync(path))
            .Returns(Task.FromException(new InvalidOperationException("cap")));
        var toast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        toast.Setup(t => t.ShowToast(
                ToastType.Error,
                "Failed to Pin Project",
                "cap"))
            .Verifiable();
        var bridge = new MainWindowRecentProjectsMutationBridge(() => mutations.Object, () => toast.Object);

        await bridge.PinRecentProjectAsync(path).ConfigureAwait(false);

        toast.Verify();
    }

    [TestMethod]
    public async Task PinRecentProjectAsync_noop_when_mutations_null()
    {
        var toast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        var bridge = new MainWindowRecentProjectsMutationBridge(() => (IRecentProjectsMutationCommands?)null, () => toast.Object);

        await bridge.PinRecentProjectAsync("any").ConfigureAwait(false);
    }

    [TestMethod]
    public async Task UnpinRecentProjectAsync_invokes_service_then_success_toast()
    {
        const string path = @"C:\proj\b.vsproj";
        var mutations = new Mock<IRecentProjectsMutationCommands>(MockBehavior.Strict);
        mutations.Setup(m => m.UnpinProjectAsync(path)).Returns(Task.CompletedTask).Verifiable();
        var toast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        toast.Setup(t => t.ShowToast(
                ToastType.Success,
                "Project Unpinned",
                "Project removed from pinned list"))
            .Verifiable();
        var bridge = new MainWindowRecentProjectsMutationBridge(() => mutations.Object, () => toast.Object);

        await bridge.UnpinRecentProjectAsync(path).ConfigureAwait(false);

        mutations.Verify();
        toast.Verify();
    }

    [TestMethod]
    public async Task ClearRecentProjectsAsync_invokes_service_then_success_toast()
    {
        var mutations = new Mock<IRecentProjectsMutationCommands>(MockBehavior.Strict);
        mutations.Setup(m => m.ClearRecentProjectsAsync()).Returns(Task.CompletedTask).Verifiable();
        var toast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        toast.Setup(t => t.ShowToast(
                ToastType.Success,
                "Recent Projects Cleared",
                "All recent projects have been cleared"))
            .Verifiable();
        var bridge = new MainWindowRecentProjectsMutationBridge(() => mutations.Object, () => toast.Object);

        await bridge.ClearRecentProjectsAsync().ConfigureAwait(false);

        mutations.Verify();
        toast.Verify();
    }

    [TestMethod]
    public async Task RemoveFromRecentListAsync_invokes_remove_and_does_not_toast()
    {
        const string path = @"C:\r\one.vsproj";
        var mutations = new Mock<IRecentProjectsMutationCommands>(MockBehavior.Strict);
        mutations.Setup(m => m.RemoveRecentProjectAsync(path)).Returns(Task.CompletedTask).Verifiable();
        var toast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        var bridge = new MainWindowRecentProjectsMutationBridge(() => mutations.Object, () => toast.Object);

        await bridge.RemoveFromRecentListAsync(path).ConfigureAwait(false);

        mutations.Verify();
    }

    [TestMethod]
    public async Task RemoveFromRecentListAsync_noop_when_mutations_null()
    {
        var bridge = new MainWindowRecentProjectsMutationBridge(
            () => (IRecentProjectsMutationCommands?)null,
            () => (IToastNotificationService?)null);

        await bridge.RemoveFromRecentListAsync("x").ConfigureAwait(false);
    }
}
