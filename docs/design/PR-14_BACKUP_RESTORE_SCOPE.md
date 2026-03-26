# PR-14 BackupRestore Extraction Scope

**Status:** Frozen (2026-03-22)
**Related:** [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md), [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md)
**Pattern:** scope → extract → delete → sweep → prove → ledger. No improvisation.

---

## Decision

**PR-14 = BackupRestore (7 methods)**

Chosen per evidence-based ranking: thin client exists, bounded endpoint family, 1 primary caller, low blast radius.

---

## Methods Leaving IBackendClient

| Method | Endpoint | Notes |
|--------|----------|-------|
| GetBackupsAsync | GET `/api/backup` | Returns List&lt;BackupInfo&gt; |
| GetBackupAsync | GET `/api/backup/{id}` | Returns BackupInfo |
| CreateBackupAsync | POST `/api/backup` | BackupCreateRequest → BackupInfo |
| DownloadBackupAsync | GET `/api/backup/{id}/download` | Returns Stream |
| RestoreBackupAsync | POST `/api/backup/{id}/restore` | RestoreRequest → RestoreResponse |
| UploadBackupAsync | POST `/api/backup/upload` | Stream + optional name → BackupInfo |
| DeleteBackupAsync | DELETE `/api/backup/{id}` | Returns bool |

---

## Destination

- **Interface:** `IBackupRestoreClient` (already exists)
- **Client:** `BackupRestoreClient` (already exists; currently delegates to IBackendClient)
- **Migration:** Replace `IBackendClient _backend` with `BackendClientHttpPipeline`; implement all 7 methods via pipeline

---

## Callers

| Caller | Usage |
|--------|-------|
| BackupRestoreViewModel | GetBackupsAsync, CreateBackupAsync, GetBackupAsync, DownloadBackupAsync, RestoreBackupAsync, UploadBackupAsync, DeleteBackupAsync |
| BackupRestoreViewModelSeamTests | Mocks IBackupRestoreClient |

**Caller count:** 1 ViewModel. DataBackupService does local file backup only; does not use IBackupRestoreClient.

---

## Retained Exceptions (Implementation Notes)

- **DownloadBackupAsync returns Stream:** BackendClientHttpPipeline has `GetAsync<T>` for JSON. For Stream responses, extend pipeline with `GetStreamAsync(string endpoint, CancellationToken)` (similar to BackendTransport.GetStreamAsync) or use `_httpClient.GetAsync` + `response.Content.ReadAsStreamAsync()` within BackupRestoreClient. Ensure retry/error handling is consistent.
- **UploadBackupAsync accepts Stream:** Multipart form upload. BackendClient uses `StreamContent` + `MultipartFormDataContent`. Pipeline or BackupRestoreClient must support multipart POST; may require direct HttpClient usage for this method if pipeline has no multipart helper.

---

## Implementation Checklist

1. Add `GetStreamAsync` to BackendClientHttpPipeline (or equivalent) if not present; use for DownloadBackupAsync
2. Add `BackendClientHttpPipeline` to BackupRestoreClient ctor (internal)
3. Implement GetBackupsAsync via `_pipeline.GetAsync<List<BackupInfo>>`
4. Implement GetBackupAsync via `_pipeline.GetAsync<BackupInfo>`
5. Implement CreateBackupAsync via `_pipeline.PostAsync<BackupCreateRequest, BackupInfo>`
6. Implement DownloadBackupAsync via pipeline stream method or HttpClient
7. Implement RestoreBackupAsync via `_pipeline.PostAsync<RestoreRequest, RestoreResponse>`
8. Implement UploadBackupAsync via multipart POST (pipeline or HttpClient)
9. Implement DeleteBackupAsync via pipeline DELETE (extend pipeline if needed)
10. Update AppServices DI: register with `BackendHttpContext().Pipeline` instead of IBackendClient
11. Remove all 7 methods from IBackendClient, BackendClient
12. Add seam tests for each method (path resolution)
13. Add anti-regression: IBackendClient_DoesNotExposeBackupMethods, BackendClient_DoesNotExposeBackupMethods
14. Run proof: build, targeted tests, verify.ps1 -Quick
15. Update extraction inventory, STATE.md

---

## Out-of-Scope

- DataBackupService (local backup; different seam)
- Other thin clients (Models, Mixer, Video)
- UI changes

---

## Proof Requirements

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → PASS
- `dotnet test --filter "FullyQualifiedName~BackupRestore|FullyQualifiedName~BackendClientExtractionRegression"` → PASS
- `.\scripts\verify.ps1 -Quick` → PASS
- Record actual artifact path (no "latest")
