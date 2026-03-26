using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for batch processing API (/api/batch).
  /// Use instead of IBackendClient for GetBatchJobs, CreateBatchJob, DeleteBatchJob,
  /// StartBatchJob, CancelBatchJob, GetBatchQueueStatus, GetBatchQualityReport,
  /// GetBatchQualityStatistics, RetryBatchJobWithQuality.
  /// </summary>
  public interface IBatchProcessingClient
  {
    Task<List<BatchJob>> GetBatchJobsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default);
    Task<BatchJob> CreateBatchJobAsync(BatchJobRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteBatchJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchJob> StartBatchJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchJob> CancelBatchJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchQueueStatus> GetBatchQueueStatusAsync(CancellationToken cancellationToken = default);
    Task<BatchQualityReport> GetBatchQualityReportAsync(string jobId, CancellationToken cancellationToken = default);
    Task<BatchQualityStatistics> GetBatchQualityStatisticsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default);
    Task<BatchJob> RetryBatchJobWithQualityAsync(string jobId, BatchRetryWithQualityRequest request, CancellationToken cancellationToken = default);
  }
}
