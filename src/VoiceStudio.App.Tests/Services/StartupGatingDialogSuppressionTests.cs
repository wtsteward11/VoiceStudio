using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
[TestCategory("Services")]
public class StartupGatingDialogSuppressionTests
{
    [TestMethod]
    public async Task ShowErrorAsync_WhenStartupPending_SuppressesModalDialog()
    {
        var startupState = new StartupStateService();
        var services = new ServiceCollection();
        services.AddSingleton<IStartupStateService>(startupState);
        AppServices.Initialize(services.BuildServiceProvider());
        ErrorDialogService.ResetStartupDialogDiagnostics();

        startupState.SetBackendStarting();
        var service = new ErrorDialogService();
        await service.ShowErrorAsync(new Exception("backend not yet reachable"), "Load Profiles", "startup");

        var diagnostics = ErrorDialogService.GetStartupDialogDiagnostics();
        Assert.AreEqual(1, diagnostics.StartupPendingDialogAttempts);
        Assert.AreEqual(1, diagnostics.StartupPendingDialogSuppressed);
        Assert.AreEqual(0, diagnostics.StartupPendingDialogShown);
    }

    [TestMethod]
    public async Task ShowErrorAsync_WhenStartupReady_DoesNotSuppressByStartupGate()
    {
        var startupState = new StartupStateService();
        var services = new ServiceCollection();
        services.AddSingleton<IStartupStateService>(startupState);
        AppServices.Initialize(services.BuildServiceProvider());
        ErrorDialogService.ResetStartupDialogDiagnostics();

        startupState.SetBackendReady();
        var service = new ErrorDialogService();
        await service.ShowErrorAsync("   ");

        var diagnostics = ErrorDialogService.GetStartupDialogDiagnostics();
        Assert.AreEqual(0, diagnostics.StartupPendingDialogAttempts);
        Assert.AreEqual(0, diagnostics.StartupPendingDialogSuppressed);
    }

    [TestCleanup]
    public void RestoreDefaultTestServices()
    {
        // Restore broad test service graph for subsequent tests.
        TestAppServicesHelper.EnsureInitialized();
    }
}
