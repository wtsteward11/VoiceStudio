using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowMenuBarShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowMenuBarShellBridge.cs");

    [TestMethod]
    public void Menu_bar_bridge_does_not_reference_unrelated_Slice_33_splitter()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowWorkspaceSplitterShellBridge", StringComparison.Ordinal), "Anti-creep: workspace splitter is another seam.");
    }

    [TestMethod]
    public void Menu_bar_bridge_ctor_rejects_null_dependencies()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "ArgumentNullException(");
    }

    [TestMethod]
    public void Menu_bar_bridge_builds_Modules_menu_from_IPanelRegistry_GetAllDescriptors()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "GetAllDescriptors()");
    }

    [TestMethod]
    public void Ctor_rejects_null_getMenuBarHost()
    {
        var noOpWire = new MainWindowMenuBarShellWire
        {
            RecentProjectsSubMenu = null,
            CommandRouter = null,
            ToggleMiniTimelineMenuItem = null,
            CustomizeToolbarMenuItem = null,
            ManageWorkspacesMenuItem = null,
            CheckForUpdatesMenuItem = null,
            KeyboardShortcutsMenuItem = null
        };
        var callbacks = MakeCallbacks();

        // ArgumentNullException.ThrowIfNull in .NET 6+ might not be in bridge - we used ArgumentNullException
        _ = Assert.ThrowsException<ArgumentNullException>(() => new MainWindowMenuBarShellBridge(
            null!,
            new EmptyPanelRegistry(),
            noOpWire,
            callbacks));
    }

    [TestMethod]
    public void InitializeMenuBar_does_nothing_when_host_resolves_null()
    {
        var noOpWire = new MainWindowMenuBarShellWire
        {
            RecentProjectsSubMenu = null,
            CommandRouter = null,
            ToggleMiniTimelineMenuItem = null,
            CustomizeToolbarMenuItem = null,
            ManageWorkspacesMenuItem = null,
            CheckForUpdatesMenuItem = null,
            KeyboardShortcutsMenuItem = null
        };
        var bridge = new MainWindowMenuBarShellBridge(
            static () => null,
            new EmptyPanelRegistry(),
            noOpWire,
            MakeCallbacks());

        bridge.InitializeMenuBar();
    }

    private static MainWindowMenuBarCommandCallbacks MakeCallbacks() =>
        new()
        {
            NewProject = () => { },
            OpenProject = () => { },
            SaveProject = () => { },
            ImportAudioFile = () => { },
            CloseWindow = () => { },
            ExecuteUndo = () => { },
            ExecuteRedo = () => { },
            ShowGlobalSearch = () => { },
            ExecuteNavCommand = (a, b, c, d) => { },
            OpenPanelByIdAsync = (_, _) => Task.FromResult(true),
            OpenDocumentationFolder = () => { },
            ShowAboutDialog = () => { },
            TogglePlayback = () => { },
            StopPlayback = () => { },
            ToggleRecording = () => { },
            GetShowExperimentalPanels = static () => false
        };

    private sealed class EmptyPanelRegistry : IPanelRegistry
    {
        public IEnumerable<IPanelView> GetPanelsForRegion(PanelRegion region) => Array.Empty<IPanelView>();
        public IPanelView? GetDefaultPanel(PanelRegion region) => null;
        public void RegisterPanel(IPanelView panel) { }
        public void Register(PanelDescriptor descriptor) { }
        public IEnumerable<PanelDescriptor> GetAllDescriptors() => Array.Empty<PanelDescriptor>();
        public object CreatePanel(string panelId) => new object();
        public bool TryGetDescriptor(string panelId, out PanelDescriptor? descriptor)
        {
            descriptor = null;
            return false;
        }

        public bool IsRegistered(string panelId) => false;
    }
}
