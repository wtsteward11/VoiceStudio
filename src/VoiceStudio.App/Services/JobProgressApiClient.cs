using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for job progress HTTP API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class JobProgressApiClient : IJobProgressApiClient
  {
    private readonly IBackendClient _backend;

    public JobProgressApiClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<Job?> GetJobAsync(string jobId, CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<object, Job>(
          $"/api/jobs/{jobId}",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public async Task<Job[]?> GetJobsAsync(string? jobType = null, string? status = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new NameValueCollection();
      if (!string.IsNullOrEmpty(jobType))
        queryParams.Add("job_type", jobType);
      if (!string.IsNullOrEmpty(status))
        queryParams.Add("status", status);

      var queryString = string.Join("&",
          queryParams.AllKeys.SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={System.Uri.EscapeDataString(value)}") ?? System.Array.Empty<string>()
          )
      );

      var url = "/api/jobs";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await _backend.SendRequestAsync<object, Job[]>(
          url,
          null,
          HttpMethod.Get,
          cancellationToken
      ).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<JobSummary?> GetJobSummaryAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, JobSummary>(
          "/api/jobs/summary",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task CancelJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/jobs/{jobId}/cancel",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task RetryJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/jobs/{jobId}/retry",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task PauseJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/jobs/{jobId}/pause",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task ResumeJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/jobs/{jobId}/resume",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/jobs/{jobId}",
          null,
          HttpMethod.Delete,
          cancellationToken);

    /// <inheritdoc />
    public Task ClearCompletedJobsAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          "/api/jobs",
          null,
          HttpMethod.Delete,
          cancellationToken);
  }
}
