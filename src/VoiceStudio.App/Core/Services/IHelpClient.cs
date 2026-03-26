using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for help system API (topics, search, shortcuts, categories, panel help).
  /// Use instead of IBackendClient for Help panel.
  /// </summary>
  public interface IHelpClient
  {
    /// <summary>
    /// Gets help topics, optionally filtered by category and panel.
    /// </summary>
    Task<HelpTopic[]?> GetTopicsAsync(string? category, string? panelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches help topics.
    /// </summary>
    Task<HelpSearchResponse?> SearchAsync(string query, string? category, string? panelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets keyboard shortcuts, optionally filtered by panel.
    /// </summary>
    Task<HelpKeyboardShortcut[]?> GetShortcutsAsync(string? panelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available help categories.
    /// </summary>
    Task<HelpCategoriesResponse?> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets help content for a specific panel.
    /// </summary>
    Task<PanelHelpResponse?> GetPanelHelpAsync(string panelId, CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Response from help search endpoint.
  /// </summary>
  public class HelpSearchResponse
  {
    public HelpTopic[] Topics { get; set; } = Array.Empty<HelpTopic>();
    public int Total { get; set; }
  }

  /// <summary>
  /// Response from help categories endpoint.
  /// </summary>
  public class HelpCategoriesResponse
  {
    public string[] Categories { get; set; } = Array.Empty<string>();
  }

  /// <summary>
  /// Response from panel help endpoint.
  /// </summary>
  public class PanelHelpResponse
  {
    public HelpTopic[] Topics { get; set; } = Array.Empty<HelpTopic>();
    public HelpKeyboardShortcut[] Shortcuts { get; set; } = Array.Empty<HelpKeyboardShortcut>();
    public string PanelId { get; set; } = string.Empty;
  }
}
