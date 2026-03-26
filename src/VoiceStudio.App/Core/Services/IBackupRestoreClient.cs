using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for backup and restore API (/api/backup).
  /// Use instead of IBackendClient for backup/restore operations.
  /// </summary>
  public interface IBackupRestoreClient
  {
    Task<List<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default);
    Task<BackupInfo> GetBackupAsync(string backupId, CancellationToken cancellationToken = default);
    Task<BackupInfo> CreateBackupAsync(BackupCreateRequest request, CancellationToken cancellationToken = default);
    Task<Stream> DownloadBackupAsync(string backupId, CancellationToken cancellationToken = default);
    Task<RestoreResponse> RestoreBackupAsync(string backupId, RestoreRequest request, CancellationToken cancellationToken = default);
    Task<BackupInfo> UploadBackupAsync(Stream backupFile, string? name = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default);
  }
}
