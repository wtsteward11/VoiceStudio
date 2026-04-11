using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using Windows.System;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class KeyboardShortcutServiceGap065Tests
{
    private static readonly object ShortcutsFileLock = new object();

    [TestMethod]
    public void CheckForConflict_SameChordSameContext_ReturnsConflict()
    {
        var svc = new KeyboardShortcutService();
        svc.RegisterShortcut(
            "gap065.a",
            VirtualKey.F22,
            VirtualKeyModifiers.Control,
            "a",
            ShortcutContext.Global);
        svc.RegisterShortcut(
            "gap065.b",
            VirtualKey.F22,
            VirtualKeyModifiers.Control,
            "b",
            ShortcutContext.Global);

        var conflict = svc.CheckForConflict("gap065.c", VirtualKey.F22, VirtualKeyModifiers.Control, ShortcutContext.Global);
        Assert.IsNotNull(conflict);
        Assert.IsTrue(
            conflict!.ConflictingCommandId is "gap065.a" or "gap065.b",
            "Expected conflict with one of the registered globals.");
    }

    [TestMethod]
    public void CheckForConflict_SameChordDifferentContext_ReturnsNull_WhenNoSameContextMatch()
    {
        var svc = new KeyboardShortcutService();
        svc.RegisterShortcut(
            "gap065.panelOnly",
            VirtualKey.F21,
            VirtualKeyModifiers.None,
            "panel",
            ShortcutContext.Panel);

        var globalProbe = svc.CheckForConflict(
            "gap065.newGlobal",
            VirtualKey.F21,
            VirtualKeyModifiers.None,
            ShortcutContext.Global);
        Assert.IsNull(globalProbe);

        svc.RegisterShortcut(
            "gap065.globalOnly",
            VirtualKey.F20,
            VirtualKeyModifiers.None,
            "global",
            ShortcutContext.Global);

        var panelProbe = svc.CheckForConflict(
            "gap065.newPanel",
            VirtualKey.F20,
            VirtualKeyModifiers.None,
            ShortcutContext.Panel);
        Assert.IsNull(panelProbe);
    }

    [TestMethod]
    public void InitializeAsync_LoadsPersistedCustomizations()
    {
        lock (ShortcutsFileLock)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoiceStudio");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "shortcuts.json");
            string? backup = File.Exists(path) ? File.ReadAllText(path) : null;
            try
            {
                var writer = new KeyboardShortcutService();
                writer.SetCustomShortcutAsync("file.new", VirtualKey.M, VirtualKeyModifiers.Control).GetAwaiter().GetResult();

                var reader = new KeyboardShortcutService();
                reader.InitializeAsync().GetAwaiter().GetResult();

                var b = reader.GetShortcut("file.new");
                Assert.IsNotNull(b);
                Assert.AreEqual(VirtualKey.M, b!.Key);
                Assert.AreEqual(VirtualKeyModifiers.Control, b.Modifiers);
            }
            finally
            {
                if (backup != null)
                {
                    File.WriteAllText(path, backup);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void SetCustomShortcutAsync_PersistsAndRoundTrips()
    {
        lock (ShortcutsFileLock)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoiceStudio");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "shortcuts.json");
            string? backup = File.Exists(path) ? File.ReadAllText(path) : null;
            try
            {
                var first = new KeyboardShortcutService();
                var ok = first.SetCustomShortcutAsync("file.open", VirtualKey.Q, VirtualKeyModifiers.Menu).GetAwaiter().GetResult();
                Assert.IsTrue(ok);

                var second = new KeyboardShortcutService();
                second.InitializeAsync().GetAwaiter().GetResult();
                var b = second.GetShortcut("file.open");
                Assert.IsNotNull(b);
                Assert.AreEqual(VirtualKey.Q, b!.Key);
                Assert.AreEqual(VirtualKeyModifiers.Menu, b.Modifiers);
            }
            finally
            {
                if (backup != null)
                {
                    File.WriteAllText(path, backup);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void ResetAllToDefaults_ClearsCustomizations()
    {
        var svc = new KeyboardShortcutService();
        _ = svc.SetCustomShortcutAsync("file.save", VirtualKey.H, VirtualKeyModifiers.Shift).GetAwaiter().GetResult();
        Assert.IsTrue(svc.GetCustomizedShortcuts().Contains("file.save"));

        svc.ResetAllToDefaults();
        Assert.IsFalse(svc.GetCustomizedShortcuts().Contains("file.save"));
        var save = svc.GetShortcut("file.save");
        Assert.IsNotNull(save);
        Assert.AreEqual(VirtualKey.S, save!.Key);
        Assert.AreEqual(VirtualKeyModifiers.Control, save.Modifiers);
    }
}
