using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for model management API. Use instead of IBackendClient for model manager panel.
  /// </summary>
  public interface IModelManagerClient
  {
    Task<List<ModelInfo>> GetModelsAsync(string? engine = null, CancellationToken cancellationToken = default);
    Task<ModelInfo> GetModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<ModelInfo> RegisterModelAsync(string engine, string modelName, string modelPath, string? version = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
    Task<ModelVerifyResponse> VerifyModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<ModelInfo> UpdateModelChecksumAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<bool> DeleteModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default);
    Task<Stream> ExportModelAsync(string engine, string modelName, CancellationToken cancellationToken = default);
    Task<ModelInfo> ImportModelAsync(Stream modelArchive, string? engine = null, CancellationToken cancellationToken = default);
  }
}
