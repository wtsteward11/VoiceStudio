using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowRecentProjectsMenuPopulationShellBridgeTests
{
    private static MainWindowRecentProjectsMenuPopulationShellBridge CreateBridge(
        Func<string, string, Task>? open = null,
        Func<string, Task>? pin = null,
        Func<string, Task>? unpin = null,
        Func<string, Task>? remove = null,
        Func<Task>? clear = null)
    {
        return new MainWindowRecentProjectsMenuPopulationShellBridge(
            open ?? ((_, _) => Task.CompletedTask),
            pin ?? (_ => Task.CompletedTask),
            unpin ?? (_ => Task.CompletedTask),
            remove ?? (_ => Task.CompletedTask),
            clear ?? (() => Task.CompletedTask));
    }

    [TestMethod]
    public void Constructor_throws_when_any_delegate_null()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowRecentProjectsMenuPopulationShellBridge(
                null!,
                _ => Task.CompletedTask,
                _ => Task.CompletedTask,
                _ => Task.CompletedTask,
                () => Task.CompletedTask));
    }

    [TestMethod]
    public void Populate_noop_when_submenu_null()
    {
        var bridge = CreateBridge();
        bridge.Populate(null, new RecentProjectsService());
    }

    [TestMethod]
    public void Populate_bridge_contains_flyout_composition_for_spine_pin()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "VoiceStudio.App", "Services", "MainWindowRecentProjectsMenuPopulationShellBridge.cs");
        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "MenuFlyoutSubItem");
        StringAssert.Contains(text, "No recent projects");
        StringAssert.Contains(text, "Clear Recent Projects");
    }

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
}
