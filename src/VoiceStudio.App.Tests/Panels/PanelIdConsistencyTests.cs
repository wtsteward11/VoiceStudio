using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Panels;

/// <summary>
/// CI gate: Ensures every registered panel uses a canonical PanelId from PanelIds.
/// Catches drift between registry and ViewModel PanelId (e.g. "voice_synthesis" vs "VoiceSynthesis").
/// </summary>
[TestClass]
public class PanelIdConsistencyTests
{
    /// <summary>
    /// Minimal registry for collecting descriptors without full DI.
    /// </summary>
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

    [TestMethod]
    public void AllRegisteredPanels_UseCanonicalPanelIds()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        var canonicalIds = new HashSet<string>(PanelIds.All, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        foreach (var descriptor in registry.GetAllDescriptors())
        {
            if (!canonicalIds.Contains(descriptor.PanelId))
            {
                failures.Add($"Panel '{descriptor.PanelId}' (ViewModel: {descriptor.ViewModelType?.Name ?? "null"}) is not in PanelIds.All. Use PanelIds.X constant.");
            }
        }

        Assert.IsTrue(
            failures.Count == 0,
            "Panel ID consistency violations:\n" + string.Join("\n", failures));
    }

    [TestMethod]
    public void AllRegisteredPanels_HaveViewModelType()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        var missing = registry.GetAllDescriptors()
            .Where(d => d.ViewModelType == null)
            .Select(d => d.PanelId)
            .ToList();

        Assert.IsTrue(
            missing.Count == 0,
            "Panels without ViewModelType: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void PanelIds_All_HasNoDuplicates()
    {
        var distinct = PanelIds.All.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.AreEqual(
            PanelIds.All.Count,
            distinct.Count,
            "PanelIds.All contains duplicates. Use distinct values only.");
    }

    [TestMethod]
    public void WorkspaceDefaults_UseOnlyRegisteredPanelIds()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        var canonicalIds = new HashSet<string>(PanelIds.All, StringComparer.OrdinalIgnoreCase);
        var defaultPresets = GetWorkspaceServiceDefaultPresets();
        var failures = new List<string>();

        foreach (var preset in defaultPresets)
        {
            foreach (var placement in preset.Panels ?? Array.Empty<PanelPlacement>())
            {
                var panelId = placement.PanelId;
                if (string.IsNullOrEmpty(panelId))
                    continue;
                if (!canonicalIds.Contains(panelId))
                    failures.Add($"DefaultPreset '{preset.Id}' uses '{panelId}' which is not in PanelIds.All.");
                if (!registry.TryGetDescriptor(panelId, out _))
                    failures.Add($"DefaultPreset '{preset.Id}' uses '{panelId}' which is not registered.");
            }
        }

        Assert.IsTrue(
            failures.Count == 0,
            "Workspace default presets use invalid panel IDs:\n" + string.Join("\n", failures));
    }

    [TestMethod]
    public void WorkspaceJsonFiles_UseOnlyRegisteredPanelIds()
    {
        var registry = new CollectingRegistry();
        CorePanelRegistrationService.RegisterCorePanels(registry);
        AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
        ModulePanelRegistrationService.RegisterModulePanels(registry);

        var canonicalIds = new HashSet<string>(PanelIds.All, StringComparer.OrdinalIgnoreCase);
        var workspacesDir = Path.Combine(
            Path.GetDirectoryName(typeof(WorkspaceService).Assembly.Location) ?? AppContext.BaseDirectory ?? ".",
            "Resources", "Workspaces");
        var failures = new List<string>();

        if (!Directory.Exists(workspacesDir))
        {
            Assert.Inconclusive($"Workspace JSON directory not found: {workspacesDir}. Run full build first.");
            return;
        }

        foreach (var jsonPath in Directory.EnumerateFiles(workspacesDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("layout", out var layoutEl))
                    continue;
                if (!layoutEl.TryGetProperty("regions", out var regionsEl))
                    continue;

                foreach (var regionEl in regionsEl.EnumerateArray())
                {
                    foreach (var prop in new[] { "activePanelId", "openedPanels" })
                    {
                        if (!regionEl.TryGetProperty(prop, out var valEl))
                            continue;
                        if (valEl.ValueKind == JsonValueKind.String)
                        {
                            var id = valEl.GetString();
                            if (!string.IsNullOrEmpty(id) && (!canonicalIds.Contains(id) || !registry.TryGetDescriptor(id, out _)))
                                failures.Add($"{Path.GetFileName(jsonPath)}: {prop}='{id}' not in PanelIds.All or not registered.");
                        }
                        else if (valEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in valEl.EnumerateArray())
                            {
                                var id = item.GetString();
                                if (!string.IsNullOrEmpty(id) && (!canonicalIds.Contains(id) || !registry.TryGetDescriptor(id, out _)))
                                    failures.Add($"{Path.GetFileName(jsonPath)}: {prop}=['{id}'] not in PanelIds.All or not registered.");
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                failures.Add($"{Path.GetFileName(jsonPath)}: JSON parse error: {ex.Message}");
            }
        }

        Assert.IsTrue(
            failures.Count == 0,
            "Workspace JSON files use invalid panel IDs:\n" + string.Join("\n", failures));
    }

    private static IReadOnlyList<WorkspaceDefinition> GetWorkspaceServiceDefaultPresets()
    {
        var type = typeof(WorkspaceService);
        var field = type.GetField("DefaultPresets", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WorkspaceService.DefaultPresets not found.");
        var value = field.GetValue(null);
        return (IReadOnlyList<WorkspaceDefinition>)value!;
    }
}
