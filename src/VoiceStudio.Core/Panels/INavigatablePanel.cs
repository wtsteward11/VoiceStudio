using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Panels
{
  /// <summary>
  /// Interface for panels that support search-result navigation.
  /// When a user selects a search result, the panel can navigate to and select the item by ID.
  /// </summary>
  public interface INavigatablePanel
  {
    /// <summary>
    /// Navigates to and selects an item in the panel by ID.
    /// </summary>
    /// <param name="itemId">The item identifier (e.g. profile ID, project ID).</param>
    /// <param name="resultType">Backend search type: profile, project, project_audio, marker, script, etc.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="searchMetadata">Optional metadata from the search API (e.g. marker project_id, time).</param>
    /// <returns>True if the item was found and selected (or best-effort navigation completed for partial flows); false otherwise.</returns>
    Task<bool> NavigateToItemAsync(
        string itemId,
        string resultType,
        CancellationToken ct,
        IReadOnlyDictionary<string, object>? searchMetadata = null);
  }
}
