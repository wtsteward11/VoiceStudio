using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowStartupWelcomeActivationShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowStartupWelcomeActivationShellBridge.cs");

    [TestMethod]
    public void Bridge_ctor_requires_gate_safe_and_key_handler()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "Func<bool> isGateCSmokeMode");
        StringAssert.Contains(text, "Func<bool> isSafeStartupMode");
        StringAssert.Contains(text, "KeyEventHandler windowKeyDown");
        StringAssert.Contains(text, "ArgumentNullException(nameof(isGateCSmokeMode))");
    }

    [TestMethod]
    public void Bridge_ShowWelcomeKey_constant_not_duplicated_on_MainWindow()
    {
        var mw = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml.cs"));
        Assert.IsFalse(mw.Contains("ShowWelcomeDialog", StringComparison.Ordinal), "ShowWelcomeKey constant must live only on the bridge after extraction.");
        var bridge = File.ReadAllText(BridgePath);
        StringAssert.Contains(bridge, "ShowWelcomeDialog");
    }
}
