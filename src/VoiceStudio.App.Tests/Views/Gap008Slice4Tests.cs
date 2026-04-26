using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice4Tests
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

    private static string BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowProjectWorkflowBridge.cs");

    [TestMethod]
    public void MainWindow_Project_menu_handlers_delegate_to_workflow_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_projectWorkflowCommandBridge");
        StringAssert.Contains(text, "await _projectWorkflowCommandBridge.SaveProjectAsync");
        StringAssert.Contains(text, "await _projectWorkflowCommandBridge.CreateNewProjectAsync");
        StringAssert.Contains(text, "await _projectWorkflowCommandBridge.OpenProjectAsync");
        StringAssert.Contains(text, "await _projectWorkflowCommandBridge.OpenRecentProjectAsync");
    }

    [TestMethod]
    public void MainWindow_still_owns_recent_menu_population_and_session_coordinator_accessor_Choice_A()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "PopulateRecentProjectsMenu");
        StringAssert.Contains(text, "GetProjectWorkflowCoordinatorForSessionLifecycle");
        StringAssert.Contains(text, "private async void PinRecentProject");
    }

    [TestMethod]
    public void MainWindowProjectWorkflowBridge_file_has_no_import_workflow_type()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("IImportWorkflowService", StringComparison.Ordinal), "Slice 4 bridge must not reference import workflow.");
    }
}
