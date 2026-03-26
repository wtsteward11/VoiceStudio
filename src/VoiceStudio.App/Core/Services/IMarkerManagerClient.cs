using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for marker management API (/api/markers, /api/markers/categories).
  /// Use instead of IBackendClient for MarkerManager panel.
  /// </summary>
  public interface IMarkerManagerClient
  {
    Task<Marker[]?> GetMarkersAsync(string? projectId = null, string? category = null, CancellationToken cancellationToken = default);
    Task<Marker?> CreateMarkerAsync(string projectId, string name, double time, string color, string? category, string? description, CancellationToken cancellationToken = default);
    Task<Marker?> UpdateMarkerAsync(string markerId, string name, double time, string color, string? category, string? description, CancellationToken cancellationToken = default);
    Task DeleteMarkerAsync(string markerId, CancellationToken cancellationToken = default);
    Task<MarkerManagerViewModel.MarkerCategoriesResponse?> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default);
  }
}
