using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Panels;

/// <summary>
/// Proves Script Editor panel is visible in registry and reachable from shell.
/// </summary>
[TestClass]
public class ScriptEditorVisibilityTests
{
    private sealed class CollectingRegistry : IPanelRegistry
    {
        private readonly System.Collections.Generic.Dictionary<string, PanelDescriptor> _descriptors =
            new(System.StringComparer.OrdinalIgnoreCase);

        public void Register(PanelDescriptor descriptor)
        {
            if (descriptor == null)
                throw new System.ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrEmpty(descriptor.PanelId))
                throw new System.ArgumentException("PanelId cannot be null or empty", nameof(descriptor));
            _descriptors[descriptor.PanelId] = descriptor;
        }

        public System.Collections.Generic.IEnumerable<PanelDescriptor> GetAllDescriptors() =>
            _descriptors.Values.ToList();

        public bool IsRegistered(string panelId) => _descriptors.ContainsKey(panelId);

        public bool TryGetDescriptor(string panelId, out PanelDescriptor? descriptor)
        {
            var found = _descriptors.TryGetValue(panelId, out var d);
            descriptor = d;
            return found;
        }

        public System.Collections.Generic.IEnumerable<IPanelView> GetPanelsForRegion(PanelRegion region) =>
            System.Array.Empty<IPanelView>();
        public IPanelView? GetDefaultPanel(PanelRegion region) => null;
        public void RegisterPanel(IPanelView panel) { }
        public object CreatePanel(string panelId) => throw new System.NotSupportedException("Not used in this test");
    }

    [TestMethod]
    public void ScriptEditor_AppearsInRegistry_WithIsVisibleTrue()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        var found = registry.TryGetDescriptor(PanelIds.ScriptEditor, out var descriptor);
        Assert.IsTrue(found, "Script Editor must be registered in PanelRegistry.");
        Assert.IsNotNull(descriptor);
        Assert.IsTrue(descriptor!.IsVisible, "Script Editor must have IsVisible = true to appear in menu/Command Palette.");
        Assert.AreEqual(PanelIds.ScriptEditor, descriptor.PanelId);
        Assert.IsNotNull(descriptor.ViewModelType, "Script Editor must have ViewModelType for CreatePanel.");
    }

    [TestMethod]
    public void ScriptEditor_IsInGetAllDescriptors_WhenFilteringVisible()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        var visibleScriptEditor = registry.GetAllDescriptors()
            .FirstOrDefault(d => d.PanelId == PanelIds.ScriptEditor && d.IsVisible);
        Assert.IsNotNull(visibleScriptEditor, "Script Editor must appear in visible panels for shell/menu.");
    }
}
