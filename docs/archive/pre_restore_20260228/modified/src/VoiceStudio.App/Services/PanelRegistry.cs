using System;
using VoiceStudio.App.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.ViewModels;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Unified panel registry that manages panel descriptors and creates panels
  /// using the ViewModelFactory for dependency injection.
  /// </summary>
  public class PanelRegistry : IPanelRegistry
  {
    private readonly List<IPanelView> _panels = new List<IPanelView>();
    private readonly Dictionary<string, PanelDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _normalizedPanelIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly IViewModelFactory _viewModelFactory;

    public PanelRegistry(IViewModelFactory viewModelFactory)
    {
      _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
    }

    public IEnumerable<IPanelView> GetPanelsForRegion(PanelRegion region)
    {
      return _panels.Where(p => p.Region == region);
    }

    public IPanelView? GetDefaultPanel(PanelRegion region)
    {
      return _panels.FirstOrDefault(p => p.Region == region);
    }

    public void RegisterPanel(IPanelView panel)
    {
      if (!_panels.Contains(panel))
      {
        _panels.Add(panel);
      }
    }

    public void Register(PanelDescriptor descriptor)
    {
      if (descriptor == null)
        throw new ArgumentNullException(nameof(descriptor));

      if (string.IsNullOrEmpty(descriptor.PanelId))
        throw new ArgumentException("PanelId cannot be null or empty", nameof(descriptor));

      if (_descriptors.ContainsKey(descriptor.PanelId))
      {
        throw new InvalidOperationException(
          $"Panel '{descriptor.PanelId}' is already registered. " +
          "Use a unique PanelId or check IsRegistered() before registering.");
      }

      _descriptors[descriptor.PanelId] = descriptor;

      var normalizedPanelId = NormalizePanelId(descriptor.PanelId);
      if (!string.IsNullOrEmpty(normalizedPanelId)
          && !_normalizedPanelIds.ContainsKey(normalizedPanelId))
      {
        _normalizedPanelIds[normalizedPanelId] = descriptor.PanelId;
      }

      ErrorLogger.LogInfo($"[PanelRegistry] Registered panel: {descriptor.PanelId}", "PanelRegistry");
    }

    public IEnumerable<PanelDescriptor> GetAllDescriptors()
    {
      return _descriptors.Values.ToList().AsReadOnly();
    }

    public object CreatePanel(string panelId)
    {
      if (!TryResolveDescriptor(panelId, out var descriptor))
      {
        throw new KeyNotFoundException(
          $"Panel '{panelId}' is not registered. " +
          "Ensure the panel is registered via Register() before calling CreatePanel().");
      }

      // Create the view instance
      var view = Activator.CreateInstance(descriptor.ViewType);
      if (view == null)
      {
        throw new InvalidOperationException(
          $"Failed to create instance of ViewType '{descriptor.ViewType.Name}' for panel '{panelId}'.");
      }

      // Create and assign ViewModel if specified
      if (descriptor.ViewModelType != null && view is FrameworkElement frameworkElement)
      {
        // Many panel views self-initialize DataContext in their constructor.
        // Only create/assign a ViewModel from DI when DataContext has not already been set.
        if (frameworkElement.DataContext == null)
        {
          var viewModel = _viewModelFactory.Create(descriptor.ViewModelType);
          frameworkElement.DataContext = viewModel;
        }
      }

      ErrorLogger.LogDebug($"[PanelRegistry] Created panel: {descriptor.PanelId}", "PanelRegistry");
      return view;
    }

    public bool TryGetDescriptor(string panelId, out PanelDescriptor? descriptor)
    {
      if (TryResolveDescriptor(panelId, out var found))
      {
        descriptor = found;
        return true;
      }

      descriptor = null;
      return false;
    }

    public bool IsRegistered(string panelId)
    {
      return TryResolveDescriptor(panelId, out _);
    }

    private bool TryResolveDescriptor(string panelId, out PanelDescriptor descriptor)
    {
      if (_descriptors.TryGetValue(panelId, out descriptor!))
      {
        return true;
      }

      var normalizedPanelId = NormalizePanelId(panelId);
      if (!string.IsNullOrEmpty(normalizedPanelId)
          && _normalizedPanelIds.TryGetValue(normalizedPanelId, out var canonicalPanelId)
          && _descriptors.TryGetValue(canonicalPanelId, out descriptor!))
      {
        return true;
      }

      descriptor = null!;
      return false;
    }

    private static string NormalizePanelId(string panelId)
    {
      if (string.IsNullOrWhiteSpace(panelId))
      {
        return string.Empty;
      }

      var builder = new StringBuilder(panelId.Length);
      foreach (var ch in panelId)
      {
        if (char.IsLetterOrDigit(ch))
        {
          builder.Append(char.ToLowerInvariant(ch));
        }
      }

      return builder.ToString();
    }
  }
}
