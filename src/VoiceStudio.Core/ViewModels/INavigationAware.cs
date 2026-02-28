using System;

namespace VoiceStudio.Core.ViewModels
{
  /// <summary>
  /// Interface for ViewModels that need to respond to navigation lifecycle events.
  /// GAP-I14: Provides standardized lifecycle hooks for proper resource management.
  /// </summary>
  /// <remarks>
  /// Implement this interface to:
  /// - Initialize resources when navigated to
  /// - Clean up subscriptions when navigated away
  /// - Support proper disposal when panel is closed
  /// </remarks>
  public interface INavigationAware : IDisposable
  {
    /// <summary>
    /// Called when the panel is navigated to (becomes visible/active).
    /// Use this to load data, start subscriptions, or refresh state.
    /// </summary>
    void OnNavigatedTo();

    /// <summary>
    /// Called when the panel is navigated away from (becomes hidden/inactive).
    /// Use this to pause operations, save state, or release temporary resources.
    /// </summary>
    void OnNavigatedFrom();
  }
}
