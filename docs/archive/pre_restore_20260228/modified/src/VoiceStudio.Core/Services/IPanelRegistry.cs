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
    /// <exception cref="System.InvalidOperationException">
    /// Thrown if a panel with the same ID is already registered.
    /// </exception>
    void Register(PanelDescriptor descriptor);

    /// <summary>
    /// Get all registered panel descriptors.
    /// </summary>
    IEnumerable<PanelDescriptor> GetAllDescriptors();

    /// <summary>
    /// Creates a panel instance by its ID using the ViewModelFactory.
    /// </summary>
    /// <param name="panelId">The panel ID to create.</param>
    /// <returns>A new UserControl instance with its ViewModel set as DataContext.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// Thrown if the panel ID is not registered.
    /// </exception>
    object CreatePanel(string panelId);

    /// <summary>
    /// Tries to get a panel descriptor by ID.
    /// </summary>
    /// <param name="panelId">The panel ID to look up.</param>
    /// <param name="descriptor">The descriptor if found; otherwise null.</param>
    /// <returns>True if the descriptor was found; otherwise false.</returns>
    bool TryGetDescriptor(string panelId, out PanelDescriptor? descriptor);

    /// <summary>
    /// Checks if a panel with the given ID is registered.
    /// </summary>
    bool IsRegistered(string panelId);
  }
}