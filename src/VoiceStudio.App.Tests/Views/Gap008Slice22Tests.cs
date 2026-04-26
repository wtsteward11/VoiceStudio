using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice22Tests
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

    private static string PopulationBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowRecentProjectsMenuPopulationShellBridge.cs");

    private static string MutationBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowRecentProjectsMutationBridge.cs");

    [TestMethod]
    public void MainWindow_delegates_recent_projects_menu_population_to_slice22_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_recentProjectsMenuPopulationBridge");
        StringAssert.Contains(text, "_recentProjectsMenuPopulationBridge.Populate");
    }

    [TestMethod]
    public void MainWindow_PopulateRecentProjectsMenu_is_thin_forward_only()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idx = text.IndexOf("PopulateRecentProjectsMenu", StringComparison.Ordinal);
        Assert.IsTrue(idx >= 0);
        var slice = text.Substring(idx, Math.Min(400, text.Length - idx));
        Assert.IsFalse(slice.Contains("MenuFlyoutSubItem", StringComparison.Ordinal),
            "MenuFlyout construction must live in population shell bridge, not MainWindow.");
    }

    [TestMethod]
    public void Population_bridge_does_not_absorb_mutation_or_workflow_types()
    {
        var text = File.ReadAllText(PopulationBridgePath);
        Assert.IsFalse(text.Contains("IRecentProjectsMutationCommands", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IProjectWorkflowCoordinator", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Mutation_bridge_still_excludes_menu_population()
    {
        var text = File.ReadAllText(MutationBridgePath);
        Assert.IsFalse(text.Contains("PopulateRecentProjectsMenu", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MenuFlyoutSubItem", StringComparison.Ordinal));
    }
}
