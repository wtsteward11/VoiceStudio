using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowJumpListTaskbarProgressShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowJumpListTaskbarProgressShellBridge.cs");

    [TestMethod]
    public void Bridge_ctor_rejects_null_getWindowHandle()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new MainWindowJumpListTaskbarProgressShellBridge(null!));
    }

    [TestMethod]
    public void MainWindowJumpListTaskbarProgressShellBridge_excludes_forbidden_slice12_creep_identifiers()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowToolCatalogShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowCommandPaletteShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowStartupWelcomeActivationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("TryDispatchPendingJumpListActivation", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("TryDispatchPendingFileActivation", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("rhvoice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase));
    }
}
