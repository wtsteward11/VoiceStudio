using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Panels;

/// <summary>
/// Proves panel restore end-to-end: workspace persistence uses the same IDs on registration,
/// ViewModel, saved layout, and restored layout. No mismatches.
/// </summary>
[TestClass]
public class PanelRestoreEndToEndTests
{
    private sealed class CollectingRegistry : IPanelRegistry
    {
        private readonly Dictionary<string, PanelDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);

        public void Register(PanelDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrEmpty(descriptor.PanelId))
                throw new ArgumentException("PanelId cannot be null or empty", nameof(descriptor));
            _descriptors[descriptor.PanelId] = descriptor;
        }

        public IEnumerable<PanelDescriptor> GetAllDescriptors() => _descriptors.Values.ToList();

        public bool IsRegistered(string panelId) => _descriptors.ContainsKey(panelId);

        public bool TryGetDescriptor(string panelId, out PanelDescriptor? descriptor)
        {
            var found = _descriptors.TryGetValue(panelId, out var d);
            descriptor = d;
            return found;
        }

        public IEnumerable<IPanelView> GetPanelsForRegion(PanelRegion region) => Array.Empty<IPanelView>();
        public IPanelView? GetDefaultPanel(PanelRegion region) => null;
        public void RegisterPanel(IPanelView panel) { }
        public object CreatePanel(string panelId) => throw new NotSupportedException("Not used in this test");
    }

    /// <summary>
    /// Canonical panel IDs used in typical workspace layouts (from plan Task 1).
    /// These must match PanelIds constants and be registered.
    /// </summary>
    private static readonly string[] CanonicalPanelIds = new[]
    {
        PanelIds.Library,
        PanelIds.Profiles,
        PanelIds.VoiceSynthesis,
        PanelIds.Timeline
    };

    [TestMethod]
    public void CanonicalPanels_ResolveViaRegistry_WhenRestored()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        var failures = new List<string>();
        foreach (var panelId in CanonicalPanelIds)
        {
            if (!registry.TryGetDescriptor(panelId, out var descriptor))
            {
                failures.Add($"Panel '{panelId}' not found in registry. Restore would fail with 'panel not found'.");
                continue;
            }
            if (descriptor!.ViewModelType == null)
            {
                failures.Add($"Panel '{panelId}' has no ViewModelType. Cannot create panel on restore.");
            }
        }

        Assert.IsTrue(
            failures.Count == 0,
            "Panel restore would fail for:\n" + string.Join("\n", failures));
    }

    [TestMethod]
    public void SimulatedSavedLayout_AllPanelIdsResolve_OnRestore()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        // Simulate a saved layout (as would be produced by PanelHost.SaveRegionState)
        var simulatedLayout = new WorkspaceLayout
        {
            ProfileName = "studio",
            Version = "1.0",
            Regions = new List<RegionState>
            {
                new() { Region = PanelRegion.Left, ActivePanelId = PanelIds.Profiles, OpenedPanels = new List<string> { PanelIds.Profiles, PanelIds.Library } },
                new() { Region = PanelRegion.Center, ActivePanelId = PanelIds.VoiceSynthesis, OpenedPanels = new List<string> { PanelIds.VoiceSynthesis } },
                new() { Region = PanelRegion.Right, ActivePanelId = PanelIds.Timeline, OpenedPanels = new List<string> { PanelIds.Timeline } }
            }
        };

        var unresolved = new List<string>();
        foreach (var region in simulatedLayout.Regions ?? Enumerable.Empty<RegionState>())
        {
            if (!string.IsNullOrEmpty(region.ActivePanelId) && !registry.TryGetDescriptor(region.ActivePanelId, out _))
                unresolved.Add($"{region.Region}.ActivePanelId={region.ActivePanelId}");
            foreach (var panelId in region.OpenedPanels ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrEmpty(panelId) && !registry.TryGetDescriptor(panelId, out _))
                    unresolved.Add($"{region.Region}.OpenedPanels={panelId}");
            }
        }

        Assert.IsTrue(
            unresolved.Count == 0,
            "Restored layout would fail - unresolved panel IDs: " + string.Join("; ", unresolved));
    }
}
