using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for multi-voice generator API (/api/voice/multi).
  /// Use instead of IBackendClient for multi-voice generation, CSV import/export,
  /// job status, results, and voice comparison.
  /// </summary>
  public interface IMultiVoiceGeneratorClient
  {
    Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.MultiVoiceCSVImportResponse?> ImportCSVAsync(string csvContent, CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.MultiVoiceCSVExportResponse?> ExportCSVAsync(string jobId, CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.MultiVoiceGenerateResponse?> GenerateAsync(VoiceStudio.App.Services.MultiVoiceGenerateRequest request, CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.MultiVoiceJobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.MultiVoiceResultsResponse?> GetResultsAsync(string jobId, CancellationToken cancellationToken = default);
    Task<VoiceStudio.App.Services.MultiVoiceCompareResponse?> CompareVoicesAsync(VoiceStudio.App.Services.MultiVoiceCompareRequest request, CancellationToken cancellationToken = default);
  }
}
