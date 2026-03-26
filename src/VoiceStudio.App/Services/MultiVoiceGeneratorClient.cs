using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/voice/multi. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class MultiVoiceGeneratorClient : IMultiVoiceGeneratorClient
  {
    private readonly IBackendClient _backend;

    public MultiVoiceGeneratorClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default)
      => _backend.GetEnginesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<MultiVoiceCSVImportResponse?> ImportCSVAsync(string csvContent, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<MultiVoiceCSVImportRequest, MultiVoiceCSVImportResponse>(
          "/api/voice/multi/import",
          new MultiVoiceCSVImportRequest { CsvContent = csvContent },
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<MultiVoiceCSVExportResponse?> ExportCSVAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, MultiVoiceCSVExportResponse>(
          $"/api/voice/multi/export?job_id={Uri.EscapeDataString(jobId)}",
          new { },
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<MultiVoiceGenerateResponse?> GenerateAsync(MultiVoiceGenerateRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<MultiVoiceGenerateRequest, MultiVoiceGenerateResponse>(
          "/api/voice/multi/generate",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<MultiVoiceJobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, MultiVoiceJobStatusResponse>(
          $"/api/voice/multi/{Uri.EscapeDataString(jobId)}/status",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<MultiVoiceResultsResponse?> GetResultsAsync(string jobId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, MultiVoiceResultsResponse>(
          $"/api/voice/multi/{Uri.EscapeDataString(jobId)}/results",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<MultiVoiceCompareResponse?> CompareVoicesAsync(MultiVoiceCompareRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<MultiVoiceCompareRequest, MultiVoiceCompareResponse>(
          "/api/voice/multi/compare",
          request,
          HttpMethod.Post,
          cancellationToken);
  }
}
