using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/templates.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface ITemplateLibraryClient
  {
    /// <summary>
    /// Gets templates with optional category and search filters.
    /// </summary>
    Task<TemplateLibraryTemplate[]?> GetTemplatesAsync(string? category, string? search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new template.
    /// </summary>
    Task<TemplateLibraryTemplate?> CreateTemplateAsync(string name, string? category, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing template.
    /// </summary>
    Task<TemplateLibraryTemplate?> UpdateTemplateAsync(string id, string name, string category, string? description, IReadOnlyList<string> tags, bool isPublic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a template.
    /// </summary>
    Task DeleteTemplateAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a template to create a new project.
    /// </summary>
    Task<TemplateApplyResult?> ApplyTemplateAsync(string id, string projectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available template categories.
    /// </summary>
    Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default);
  }
}
