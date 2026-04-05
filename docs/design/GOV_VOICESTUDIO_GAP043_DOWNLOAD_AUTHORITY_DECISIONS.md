# GAP-043 — Download authority decisions (frozen before code)

**Execution row:** [GOV_VOICESTUDIO_GAP043_INAPP_MODEL_DOWNLOAD_MANAGER_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP043_INAPP_MODEL_DOWNLOAD_MANAGER_01_EXECUTION_ROW.md)  
**Status:** Accepted for implementation

## 1) Lifecycle owners

| Stage | Owner | Rule |
|-------|--------|------|
| Job row created | `POST /api/models/download` | `job_type=download`, `status=pending`, metadata holds `url`, `engine_id`, `model_name`, `version`, optional `expected_sha256` |
| Bytes on disk | `model_download_service` | Stream to **temp** path under `tempfile`; never write final model dir until verified |
| Verification | `model_download_service` | If `expected_sha256` set → SHA-256 of **staged file** must match before any registration |
| Registry activation | `ModelStorage.register_model` + optional `ModelRegistryService.register_artifact` | **Only after** verification passes (or no expected hash → still require successful save + register) |
| Job terminal state | `JobRepository` | `completed` only after registration succeeds; else `failed` or `cancelled` |

## 2) Canonical job type

- **`download`** is the canonical `job_type` value (enum `JobType.DOWNLOAD`).
- Reuse of `import` for remote downloads is **OUT** — avoids conflating upload-import with URL download.

## 3) Cancel / retry / resume

| Action | Behavior |
|--------|-----------|
| **Cancel** | `POST /api/jobs/{id}/cancel` → status `cancelled`; worker observes cancellation between chunks and stops; staged partial file deleted |
| **Retry** | `POST /api/jobs/{id}/retry` for `failed` → reset to `pending`, clear error, re-**enqueue** download execution |
| **Resume** | `POST /api/jobs/{id}/resume` for `paused` download jobs → set `running` and re-**enqueue** execution |

**HTTP range / resume:** **Not guaranteed.** v1 treats resume/retry as **restart from byte 0** after removing partial staging file. Document as best-effort; true `Range` resume is **future**.

## 4) Fail-closed rules

- Invalid URL scheme → **400**, no job (or fail job immediately if created).
- Insufficient disk space (best-effort check vs `Content-Length` when present) → **fail** job with explicit error; no registration.
- Checksum mismatch → **fail** job; staged file removed; **no** `register_model`.
- Network / HTTP error → **fail** job; no registration.
- Cancelled mid-stream → **cancelled**; partial removed.

## 5) Single-flight

- At most **one** active download job (`pending` | `running` | `paused`) per composite key  
  `(engine_id, model_name, version)` from metadata.
- Second start → **409 Conflict** with message referencing existing `job_id`.

## 6) Hard IN / OUT summary

**IN:** `http`/`https` URLs only; zip-with-`model_info.json` **or** single-file payload into `engine/model_name` path per `ModelStorage` layout; progress updates on canonical job; tests for success + checksum fail + cancel.

**OUT:** Non-HTTP(S) schemes; completing job without verification when `expected_sha256` provided; activating registry on partial files; shell/curl subprocess download (use `httpx` async client).
