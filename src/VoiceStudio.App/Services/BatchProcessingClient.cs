using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/batch. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class BatchProcessingClient : IBatchProcessingClient
  {
    private readonly IBackendClient _backend;

    public BatchProcessingClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<BatchJob>> GetBatchJobsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default)
      => _backend.GetBatchJobsAsync(projectId, status, cancellationToken);

    /// <inheritdoc />
    public Task<BatchJob> CreateBatchJobAsync(BatchJobRequest request, CancellationToken cancellationToken = default)
      => _backend.CreateBatchJobAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.DeleteBatchJobAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<BatchJob> StartBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.StartBatchJobAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<BatchJob> CancelBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.CancelBatchJobAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<BatchQueueStatus> GetBatchQueueStatusAsync(CancellationToken cancellationToken = default)
      => _backend.GetBatchQueueStatusAsync(cancellationToken);

    /// <inheritdoc />
    public Task<BatchQualityReport> GetBatchQualityReportAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.GetBatchQualityReportAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<BatchQualityStatistics> GetBatchQualityStatisticsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default)
      => _backend.GetBatchQualityStatisticsAsync(projectId, status, cancellationToken);

    /// <inheritdoc />
    public Task<BatchJob> RetryBatchJobWithQualityAsync(string jobId, BatchRetryWithQualityRequest request, CancellationToken cancellationToken = default)
      => _backend.RetryBatchJobWithQualityAsync(jobId, request, cancellationToken);
  }
}
