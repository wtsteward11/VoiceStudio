using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for storing quality history. Delegates to IBackendClient.
  /// </summary>
  public interface IQualityHistoryService
  {
    Task<QualityHistoryEntry> StoreQualityHistoryAsync(QualityHistoryRequest request, CancellationToken ct = default);
  }
}
