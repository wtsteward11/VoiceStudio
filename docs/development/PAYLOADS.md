# Payload Management (M8 Repo Payload Detox)

Heavyweight binaries (models, test assets, installer output) live outside the repo in an external payload root. This keeps the repo source-only and prevents Cursor/IDE indexing failures.

## What Lives in PAYLOADS_ROOT

- **Canonical test audio**: `tests/assets/canonical/originals/allan_watts.m4a`, `tests/assets/canonical/standard/allan_watts.wav`
- **Installer output**: `installer/Output/VoiceStudio-Setup-*.exe`
- **Models** (when migrated): `models/` subdirs
- **Runtime externals** (when migrated): `runtime/external/` subdirs

Layout mirrors repo-relative paths: `<PAYLOADS_ROOT>/<relative_path_from_repo>`.

## Environment Variable

- **VOICESTUDIO_PAYLOADS_ROOT**: Override the payload root. If unset:
  - Windows: `%LOCALAPPDATA%\VoiceStudioPayloads`
  - Other: `~/.voicestudio/payloads` (if implemented)

## Migration Scripts

### payload_migrate.ps1

Migrates large files from repo to payload root. Replaces each with a `.payload_pointer.json` file.

```powershell
# Dry run (default): show what would move
.\scripts\dev\payload_migrate.ps1
.\scripts\dev\payload_migrate.ps1 -DryRun

# Execute migration
.\scripts\dev\payload_migrate.ps1 -Execute
```

Writes manifest to `docs/reports/verification/PAYLOAD_MIGRATION_MANIFEST_<date>.json`.

### payload_restore.ps1

Restores payloads from payload root back into the repo working tree (for local dev only).

```powershell
.\scripts\dev\payload_restore.ps1
```

**WARNING: Do not commit restored payloads.** They exceed repo size limits.

## Pointer Files

Each migrated file is replaced by `<original>.payload_pointer.json` containing:

```json
{
  "original_path": "tests/assets/canonical/standard/allan_watts.wav",
  "payload_path": "C:\\Users\\...\\VoiceStudioPayloads\\tests\\assets\\canonical\\standard\\allan_watts.wav",
  "sha256": "...",
  "size_bytes": 137983054,
  "moved_at": "2026-02-28T12:00:00.0000000"
}
```

Code resolves paths via `backend.config.path_config.resolve_payload_path()`.

## Why Payloads Must Not Be Committed

- Large files (>25MB) brick Cursor indexing and slow clones
- CI enforces `large_file_exceptions` empty (M8)
- Models and runtime externals are downloaded/generated at build time
