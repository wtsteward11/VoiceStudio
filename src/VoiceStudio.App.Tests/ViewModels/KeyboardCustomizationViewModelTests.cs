using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using Windows.System;

namespace VoiceStudio.App.Tests.ViewModels;

[TestClass]
public sealed class KeyboardCustomizationViewModelTests
{
    [TestMethod]
    public async Task ViewModel_CommitBinding_CallsSetCustomShortcut()
    {
        var keyboard = new KeyboardShortcutService();
        var dialog = new Mock<IDialogService>(MockBehavior.Strict);
        using var vm = new KeyboardCustomizationViewModel(keyboard, dialog.Object);
        vm.RefreshShortcuts();
        await vm.CommitChordAsync("file.new", VirtualKey.P, VirtualKeyModifiers.Control).ConfigureAwait(false);
        var b = keyboard.GetShortcut("file.new");
        Assert.IsNotNull(b);
        Assert.AreEqual(VirtualKey.P, b!.Key);
    }

    [TestMethod]
    public void ViewModel_CommitBinding_ConflictSetsHasConflictOnRow()
    {
        var keyboard = new KeyboardShortcutService();
        var dialog = new Mock<IDialogService>(MockBehavior.Strict);
        using var vm = new KeyboardCustomizationViewModel(keyboard, dialog.Object);
        _ = keyboard.SetCustomShortcutAsync("file.open", VirtualKey.M, VirtualKeyModifiers.Control).GetAwaiter().GetResult();
        vm.RefreshShortcuts();
        vm.CommitChordAsync("file.new", VirtualKey.M, VirtualKeyModifiers.Control).GetAwaiter().GetResult();

        var row = vm.AllItems.First(i => i.CommandId == "file.new");
        Assert.IsTrue(row.HasConflict);
        Assert.IsFalse(string.IsNullOrEmpty(row.ConflictDescription));
    }

    [TestMethod]
    public void ViewModel_ResetBinding_CallsResetToDefault()
    {
        var keyboard = new KeyboardShortcutService();
        var dialog = new Mock<IDialogService>(MockBehavior.Strict);
        using var vm = new KeyboardCustomizationViewModel(keyboard, dialog.Object);
        _ = keyboard.SetCustomShortcutAsync("file.new", VirtualKey.Z, VirtualKeyModifiers.Shift).GetAwaiter().GetResult();
        vm.ResetBindingCommand.Execute("file.new");
        var b = keyboard.GetShortcut("file.new");
        Assert.IsNotNull(b);
        Assert.AreEqual(VirtualKey.N, b!.Key);
    }

    [TestMethod]
    public void MainWindow_ShortcutInit_CallsInitializeAsyncFromLoadedNotConstructor()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(KeyboardCustomizationViewModelTests).Assembly.Location) ?? ".";
        var mainWindowPath = Path.GetFullPath(Path.Combine(
            assemblyDir, "..", "..", "..", "..", "..", "VoiceStudio.App", "MainWindow.xaml.cs"));
        if (!File.Exists(mainWindowPath))
        {
            Assert.Inconclusive($"MainWindow.xaml.cs not found at {mainWindowPath}");
        }

        var source = File.ReadAllText(mainWindowPath);
        var loadedMarker = "contentFE.Loaded";
        var idx = source.IndexOf(loadedMarker, StringComparison.Ordinal);
        Assert.IsTrue(idx >= 0, "Expected contentFE.Loaded handler.");
        var beforeLoaded = source[..idx];
        Assert.IsFalse(
            beforeLoaded.Contains("_keyboardShortcutService.InitializeAsync", StringComparison.Ordinal),
            "InitializeAsync must not run before Loaded handler.");

        Assert.IsTrue(
            source.Contains("await _keyboardShortcutService.InitializeAsync", StringComparison.Ordinal),
            "Expected await _keyboardShortcutService.InitializeAsync in MainWindow.");
    }
}
