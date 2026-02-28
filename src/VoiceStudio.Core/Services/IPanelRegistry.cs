using System.Collections.Generic;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.Core.Services
{
  public interface IPanelRegistry
  {
    IEnumerable<IPanelView> GetPanelsForRegion(PanelRegion region);
    IPanelView? GetDefaultPanel(PanelRegion region);
    void RegisterPanel(IPanelView panel);

    /// <summary>
    /// Register a panel using a descriptor.
    /// </summary>
    void Register(PanelDescriptor descriptor);

    /// <summary>
    /// Get all registered panel descriptors.
    /// </summary>
    IEnumerable<PanelDescriptor> GetAllDescriptors();
  }
}