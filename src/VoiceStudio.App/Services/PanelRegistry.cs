using System.Collections.Generic;
using System.Linq;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  public class PanelRegistry : IPanelRegistry
  {
    private readonly List<IPanelView> _panels = new List<IPanelView>();
    private readonly List<PanelDescriptor> _descriptors = new List<PanelDescriptor>();

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
      if (!_descriptors.Any(d => d.PanelId == descriptor.PanelId))
      {
        _descriptors.Add(descriptor);
      }
    }

    public IEnumerable<PanelDescriptor> GetAllDescriptors()
    {
      return _descriptors.AsReadOnly();
    }
  }
}