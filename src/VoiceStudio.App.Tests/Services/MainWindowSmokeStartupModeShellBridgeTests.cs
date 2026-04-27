using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowSmokeStartupModeShellBridgeTests
{
    private const string SafeStartupVar = "VOICESTUDIO_SAFE_STARTUP";
    private const string SmokeExitVar = "VOICE_STUDIO_SMOKE_EXIT";

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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowSmokeStartupModeShellBridge.cs");

    [TestMethod]
    public void Smoke_startup_bridge_does_not_reference_keyboard_key_dispatch_bridge_type()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("MainWindowKeyboardShortcutKeyDispatchShellBridge", StringComparison.Ordinal),
            "Anti-creep: Slice 38 dispatch bridge must not appear in Slice 39 mode shell.");
    }

    [TestMethod]
    public void Smoke_startup_bridge_source_does_not_call_RegisterShortcut()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("RegisterShortcut", StringComparison.Ordinal),
            "Mode shell must not perform shortcut registration.");
    }

    [TestMethod]
    public void IsSafeStartupMode_false_when_env_unset()
    {
        try
        {
            Environment.SetEnvironmentVariable(SafeStartupVar, null);
            var bridge = new MainWindowSmokeStartupModeShellBridge();
            Assert.IsFalse(bridge.IsSafeStartupMode());
            Assert.IsFalse(MainWindowSmokeStartupModeShellBridge.EvaluateSafeStartup());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SafeStartupVar, null);
        }
    }

    [TestMethod]
    public void IsSafeStartupMode_true_when_env_is_1()
    {
        try
        {
            Environment.SetEnvironmentVariable(SafeStartupVar, "1");
            var bridge = new MainWindowSmokeStartupModeShellBridge();
            Assert.IsTrue(bridge.IsSafeStartupMode());
            Assert.IsTrue(MainWindowSmokeStartupModeShellBridge.EvaluateSafeStartup());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SafeStartupVar, null);
        }
    }

    [TestMethod]
    public void EvaluateSafeStartup_matches_instance_IsSafeStartupMode_when_env_true()
    {
        try
        {
            Environment.SetEnvironmentVariable(SafeStartupVar, "true");
            var bridge = new MainWindowSmokeStartupModeShellBridge();
            Assert.AreEqual(MainWindowSmokeStartupModeShellBridge.EvaluateSafeStartup(), bridge.IsSafeStartupMode());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SafeStartupVar, null);
        }
    }

    [TestMethod]
    public void IsGateCSmokeMode_true_when_smoke_exit_env_truthy()
    {
        try
        {
            Environment.SetEnvironmentVariable(SmokeExitVar, null);
            Environment.SetEnvironmentVariable(SmokeExitVar, "1");
            var bridge = new MainWindowSmokeStartupModeShellBridge();
            Assert.IsTrue(bridge.IsGateCSmokeMode());
            Assert.IsTrue(MainWindowSmokeStartupModeShellBridge.EvaluateGateCSmoke());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SmokeExitVar, null);
        }
    }

    [TestMethod]
    public void EvaluateGateCSmoke_matches_instance_when_smoke_env_set()
    {
        try
        {
            Environment.SetEnvironmentVariable(SmokeExitVar, "true");
            var bridge = new MainWindowSmokeStartupModeShellBridge();
            Assert.AreEqual(MainWindowSmokeStartupModeShellBridge.EvaluateGateCSmoke(), bridge.IsGateCSmokeMode());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SmokeExitVar, null);
        }
    }
}
