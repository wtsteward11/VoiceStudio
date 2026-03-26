using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/backup. PR-14: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class BackupRestoreClient : IBackupRestoreClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with pipeline from CreateBackupRestoreClient.
    /// </summary>
    internal BackupRestoreClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<List<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<List<BackupInfo>>("/api/backup", cancellationToken);
      return result ?? new List<BackupInfo>();
    }

    /// <inheritdoc />
    public async Task<BackupInfo> GetBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<BackupInfo>($"/api/backup/{Uri.EscapeDataString(backupId)}", cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize backup info");
    }

    /// <inheritdoc />
    public Task<BackupInfo> CreateBackupAsync(BackupCreateRequest request, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<BackupCreateRequest, BackupInfo>("/api/backup", request, cancellationToken);

    /// <inheritdoc />
    public Task<Stream> DownloadBackupAsync(string backupId, CancellationToken cancellationToken = default)
      => _pipeline.GetStreamAsync($"/api/backup/{Uri.EscapeDataString(backupId)}/download", cancellationToken);

    /// <inheritdoc />
    public Task<RestoreResponse> RestoreBackupAsync(string backupId, RestoreRequest request, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<RestoreRequest, RestoreResponse>($"/api/backup/{Uri.EscapeDataString(backupId)}/restore", request, cancellationToken);

    /// <inheritdoc />
    public async Task<BackupInfo> UploadBackupAsync(Stream backupFile, string? name = null, CancellationToken cancellationToken = default)
    {
      var queryParams = string.IsNullOrEmpty(name) ? null : new Dictionary<string, string> { { "name", name } };
      var result = await _pipeline.PostMultipartAsync<BackupInfo>(
          "/api/backup/upload",
          backupFile,
          "file",
          "backup.zip",
          queryParams,
          cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize backup info");
    }

    /// <inheritdoc />
    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/backup/{Uri.EscapeDataString(backupId)}", null, HttpMethod.Delete, cancellationToken);
      return true;
    }
  }
}
