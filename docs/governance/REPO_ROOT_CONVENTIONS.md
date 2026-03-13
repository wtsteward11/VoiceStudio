# Repo Root Conventions

**Purpose**: Define what belongs in the repository root vs subdirectories. Keeps root minimal and readable.

## Allowed in Root

| Item | Rationale |
|------|-----------|
| `AGENTS.md`, `CLAUDE.md`, `README.md` | Project entry points; discoverable |
| `VoiceStudio.sln`, `pyproject.toml`, `requirements*.txt` | Build and dependency manifests |
| `Directory.Build.props`, `Directory.Build.targets` | MSBuild configuration |
| `LICENSE`, `CONTRIBUTING.md`, `CHANGELOG.md` | Standard project metadata |
| `openmemory.md` | Living project index per openmemory.mdc; canonical location documented |
| `.gitignore`, `.gitattributes`, `.env.example` | Repo and env configuration |
| `pytest.ini`, `.coveragerc`, `.pre-commit-config.yaml` | Test and tool config |
| `version_lock.json`, `buildconfig.lock.json` | Version pinning |
| `VoiceStudio.code-workspace` | IDE workspace |

## Must NOT Be in Root

| Pattern | Action | Destination |
|---------|--------|-------------|
| `skip_report.txt` | Move | `.buildlogs/` or `docs/reports/` |
| `.pytest_fail.txt`, `.pytest_fail2.txt` | Move or delete | `.buildlogs/` |
| `test_seed_*.log` | Move or ignore | `.buildlogs/` or `.gitignore` |
| `turl.txt` | Delete if scratch; move if canonical | `.buildlogs/` |
| Scratch notes, temp dumps | Delete | — |
| Build output dumps | Move | `.buildlogs/` |

## Generated / Temporary Files

These are in `.gitignore` and should not appear in root when committed:

- `skip_report.txt` — pytest skip report; regenerated; output to `.buildlogs/`
- `.pytest_fail.txt`, `.pytest_fail2.txt` — pytest failure dumps
- `test_seed_*.log` — test run logs
- `turl.txt` — personal scratch (per-machine)

## Session State

- **`.cursor/STATE.md`** — Session state oracle; canonical per state-gate.mdc. Max ~75 lines for working section.
- **`STATE.md`** (root) — Duplicate or legacy; prefer `.cursor/STATE.md`
- **Archive**: When STATE.md exceeds ~75 lines, move completed milestones to [STATE_ARCHIVE.md](STATE_ARCHIVE.md). Add to closure protocol: "Update STATE.md; archive if over limit."

## References

- [document-lifecycle.mdc](../../.cursor/rules/workflows/document-lifecycle.mdc) — Document creation 4-gate
- [openmemory.mdc](../../.cursor/rules/openmemory.mdc) — openmemory.md role and location
