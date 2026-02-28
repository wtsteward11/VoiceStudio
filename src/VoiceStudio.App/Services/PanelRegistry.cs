using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
      Debug.WriteLine($"[PanelRegistry] Registered panel: {descriptor.PanelId}");
    }

    public IEnumerable<PanelDescriptor> GetAllDescriptors()
    {
      return _descriptors.Values.ToList().AsReadOnly();
    }

    public object CreatePanel(string panelId)
    {
      if (!_descriptors.TryGetValue(panelId, out var descriptor))
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
      if (descriptor.ViewModelType != null)
      {
        var viewModel = _viewModelFactory.Create(descriptor.ViewModelType);

        if (view is UserControl userControl)
        {
          userControl.DataContext = viewModel;
        }
        else if (view is FrameworkElement frameworkElement)
        {
          frameworkElement.DataContext = viewModel;
        }
      }

      Debug.WriteLine($"[PanelRegistry] Created panel: {panelId}");
      return view;
    }

    public bool TryGetDescriptor(string panelId, out PanelDescriptor? descriptor)
    {
      if (_descriptors.TryGetValue(panelId, out var found))
      {
        descriptor = found;
        return true;
      }

      descriptor = null;
      return false;
    }

    public bool IsRegistered(string panelId)
    {
      return _descriptors.ContainsKey(panelId);
    }
  }
}