using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

/// <summary>
/// Pins for GAP-008 startup truth recovery: status bar must refresh when IStartupStateService
/// completes; reachability must not bypass unified status copy.
/// </summary>
[TestClass]
public sealed class Gap008StartupTruthTests
{
    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "" })
        {
            if (string.IsNullOrEmpty(start))
            {
                continue;
            }

            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 16 && dir != null; i++, dir = dir.Parent)
            {
                var sln = Path.Combine(dir.FullName, "VoiceStudio.sln");
                if (File.Exists(sln))
                {
                    return dir.FullName;
                }
            }
        }

        throw new InvalidOperationException("VoiceStudio.sln not found.");
    }

    private static string StatusBarCoordinatorPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "StatusBarCoordinator.cs");

    private static string GlobalTransportPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Controls", "GlobalTransportControl.xaml.cs");

    [TestMethod]
    public void StatusBarCoordinator_SubscribesToStartupStateService_StateChanged()
    {
        var text = File.ReadAllText(StatusBarCoordinatorPath);
        StringAssert.Contains(text, "IStartupStateService");
        StringAssert.Contains(text, "_startupStateService.StateChanged += OnStartupStateChanged");
    }

    [TestMethod]
    public void StatusBarCoordinator_Unsubscribe_Detaches_StartupStateService_StateChanged()
    {
        var text = File.ReadAllText(StatusBarCoordinatorPath);
        StringAssert.Contains(text, "_startupStateService.StateChanged -= OnStartupStateChanged");
        StringAssert.Contains(text, "_startupStateService = null");
    }

    [TestMethod]
    public void StatusBarCoordinator_OnBackendReachabilityChanged_DoesNotAssignStatusTextDirectly()
    {
        var text = File.ReadAllText(StatusBarCoordinatorPath);
        Assert.IsFalse(
            text.Contains("statusText.Text = reachable", StringComparison.Ordinal),
            "Reachability must refresh via UpdateActivityIndicators / ApplyPrimaryStatusText, not a reachability-only status line.");
    }

    [TestMethod]
    public void StatusBarCoordinator_DefinesApplyPrimaryStatusText_ForUnifiedStatusLine()
    {
        var text = File.ReadAllText(StatusBarCoordinatorPath);
        StringAssert.Contains(text, "ApplyPrimaryStatusText");
        StringAssert.Contains(text, "Backend offline");
    }

    [TestMethod]
    public void GlobalTransportControl_StartupPath_UsesStateChangedAndRefresh()
    {
        var text = File.ReadAllText(GlobalTransportPath);
        StringAssert.Contains(text, "_startupState.StateChanged += OnStartupStateChanged");
        StringAssert.Contains(text, "private void OnStartupStateChanged");
        StringAssert.Contains(text, "Refresh();");
    }

    /// <summary>
    /// Task 364: regression guard — do not remove subscribe-time catch-up when startup is already ready.
    /// </summary>
    [TestMethod]
    public void StatusBarCoordinator_Subscribe_preserves_catch_up_when_startup_already_ready()
    {
        var text = File.ReadAllText(StatusBarCoordinatorPath);
        StringAssert.Contains(text, "if (_startupStateService.IsReady)");
        StringAssert.Contains(text, "TryEnqueue(() => UpdateActivityIndicators(_activityService))");
    }

    /// <summary>
    /// Task 364: regression guard — every startup state change must enqueue activity refresh.
    /// </summary>
    [TestMethod]
    public void StatusBarCoordinator_OnStartupStateChanged_enqueues_UpdateActivityIndicators()
    {
        var text = File.ReadAllText(StatusBarCoordinatorPath);
        StringAssert.Contains(text, "private void OnStartupStateChanged");
        StringAssert.Contains(text, "TryEnqueue(() => UpdateActivityIndicators(_activityService))");
    }
}
