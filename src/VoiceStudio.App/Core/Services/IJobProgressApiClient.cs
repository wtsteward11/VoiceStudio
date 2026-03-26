using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for job progress HTTP API. Use instead of IBackendClient for job progress panel.
  /// </summary>
  public interface IJobProgressApiClient
  {
    Task<Job[]?> GetJobsAsync(string? jobType = null, string? status = null, CancellationToken cancellationToken = default);
    Task<JobSummary?> GetJobSummaryAsync(CancellationToken cancellationToken = default);
    Task CancelJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task PauseJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task ResumeJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task ClearCompletedJobsAsync(CancellationToken cancellationToken = default);
  }
}
