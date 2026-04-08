using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services;

/// <summary>
/// Canonical global search orchestration surface (backend + local providers).
/// </summary>
public interface IGlobalSearchService
{
    Task<SearchResponse> SearchAsync(string query, string? types = null, int limit = 50, CancellationToken cancellationToken = default);
}
