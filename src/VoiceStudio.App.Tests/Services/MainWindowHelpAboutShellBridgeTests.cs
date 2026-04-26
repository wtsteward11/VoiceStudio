using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowHelpAboutShellBridgeTests
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

    private static string BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowHelpAboutShellBridge.cs");

    [TestMethod]
    public void Help_About_bridge_does_not_reference_other_mainwindow_shell_bridge_types()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowMenuToolActivationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowNavigationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OpenDocumentationFolder_warns_when_docs_path_missing()
    {
        var bridge = new MainWindowHelpAboutShellBridge();
        var warnings = new List<(string Message, string Title)>();
        var tempBase = Path.Combine(Path.GetTempPath(), "vs_gap008_slice28_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempBase);

            bridge.OpenDocumentationFolder(
                () => null,
                tempBase,
                (a, b) => warnings.Add((a, b)),
                (_, _) => { },
                (_, _) => { });

            Assert.AreEqual(1, warnings.Count, "Expected one warning when docs/ does not exist under app base.");
            StringAssert.Contains(warnings[0].Message, "Docs folder not found");
        }
        finally
        {
            try
            {
                Directory.Delete(tempBase, true);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[MainWindowHelpAboutShellBridgeTests] Temp cleanup: " + ex.Message);
            }
        }
    }
}
