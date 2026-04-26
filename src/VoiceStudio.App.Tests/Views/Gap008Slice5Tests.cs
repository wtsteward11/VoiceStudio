using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice5Tests
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

    private static string MainWindowPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml.cs");

    private static string MutationBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowRecentProjectsMutationBridge.cs");

    private static string WorkflowBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowProjectWorkflowBridge.cs");

    [TestMethod]
    public void MainWindow_recent_mutation_handlers_delegate_to_mutation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_recentProjectsMutationBridge");
        // Slice 22: Pin/Unpin/Clear/Remove surface via population-bridge ctor lambdas (no duplicate async void wrappers on MainWindow).
        StringAssert.Contains(text, "_recentProjectsMutationBridge.PinRecentProjectAsync");
        StringAssert.Contains(text, "_recentProjectsMutationBridge.UnpinRecentProjectAsync");
        StringAssert.Contains(text, "_recentProjectsMutationBridge.ClearRecentProjectsAsync");
        StringAssert.Contains(text, "_recentProjectsMutationBridge.RemoveFromRecentListAsync");
    }

    [TestMethod]
    public void MainWindow_thin_forwards_recent_menu_population_to_slice22_bridge_and_keeps_workflow_bridge_Slice4()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "PopulateRecentProjectsMenu");
        StringAssert.Contains(text, "_recentProjectsMenuPopulationBridge.Populate");
        StringAssert.Contains(text, "_projectWorkflowCommandBridge");
    }

    [TestMethod]
    public void MainWindowRecentProjectsMutationBridge_excludes_menu_population_and_project_workflow()
    {
        var text = File.ReadAllText(MutationBridgePath);
        Assert.IsFalse(text.Contains("PopulateRecentProjectsMenu", StringComparison.Ordinal), "Slice 5 mutation bridge must not own menu population.");
        Assert.IsFalse(text.Contains("IProjectWorkflowCoordinator", StringComparison.Ordinal), "Slice 5 mutation bridge must not reference project workflow coordinator.");
    }

    [TestMethod]
    public void MainWindowProjectWorkflowBridge_unchanged_for_coordinator_only()
    {
        var text = File.ReadAllText(WorkflowBridgePath);
        Assert.IsFalse(text.Contains("IRecentProjectsMutationCommands", StringComparison.Ordinal), "Slice 4 workflow bridge must not absorb recent mutation surface.");
    }
}
