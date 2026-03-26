using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;  // KeyboardShortcutsShortcut

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for keyboard shortcuts API (/api/shortcuts).
  /// Use instead of IBackendClient for KeyboardShortcuts panel.
  /// </summary>
  public interface IKeyboardShortcutsClient
  {
    Task<KeyboardShortcutsShortcut[]?> GetShortcutsAsync(string? category, string? panelId, CancellationToken cancellationToken = default);
    Task<KeyboardShortcutsShortcut?> UpdateShortcutAsync(string shortcutId, object request, CancellationToken cancellationToken = default);
    Task<KeyboardShortcutsShortcut?> ResetShortcutAsync(string shortcutId, CancellationToken cancellationToken = default);
    Task ResetAllAsync(CancellationToken cancellationToken = default);
    Task<ConflictCheckResponse?> CheckConflictAsync(string keyCode, string[] modifiers, string? excludeId, CancellationToken cancellationToken = default);
    Task<ShortcutCategoriesResponse?> GetCategoriesAsync(CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Response from shortcut conflict check.
  /// </summary>
  public class ConflictCheckResponse
  {
    public bool HasConflict { get; set; }
    public KeyboardShortcutsShortcut? ConflictingShortcut { get; set; }
  }

  /// <summary>
  /// Response from shortcut categories endpoint.
  /// </summary>
  public class ShortcutCategoriesResponse
  {
    public string[] Categories { get; set; } = Array.Empty<string>();
  }
}
