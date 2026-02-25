# VoiceStudio Handover Checklist

**Version:** v1.0.2 GA
**Created:** 2026-02-21 (Phase 12 WS5)
**Last Updated:** 2026-02-23

## Build Verification

- [ ] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` exits 0
- [ ] `dotnet build VoiceStudio.sln -c Release -p:Platform=x64` exits 0
- [ ] `python -m pytest tests/ -x --timeout=60` passes
- [ ] `.\scripts\verify.ps1 -Quick` reports GREEN

## Gate Status

| Gate | Status | Evidence |
|------|--------|----------|
| A (Architecture) | PASS | 45 ADRs in `docs/architecture/decisions/` |
| B (Build) | PASS | `.buildlogs/verification/last_run.json` |
| C (Core) | PASS | UI smoke summary in `%LOCALAPPDATA%\VoiceStudio\crashes\` |
| D (Data/Storage) | PASS | Unit tests for project store, job state, artifact registry |
| E (Engine) | PASS | Proof runs in `.buildlogs/proof_runs/` |
| F (Features/UI) | PASS | UI compliance audit in `docs/reports/verification/` |
| G (Quality) | N/A | Screen reader testing deferred |
| H (Packaging) | PASS | Installer lifecycle in `docs/reports/packaging/` |

## Key Files

| Purpose | Path |
|---------|------|
| Session State | `.cursor/STATE.md` |
| Quality Ledger | `docs/archive/Recovery_Plan/QUALITY_LEDGER.md` |
| Phase Gates Evidence | `docs/governance/PHASE_GATES_EVIDENCE_MAP.md` |
| Canonical Registry | `docs/governance/CANONICAL_REGISTRY.md` |
| Master Roadmap | `docs/governance/MASTER_ROADMAP_UNIFIED.md` |
| Release Notes | `docs/release/RELEASE_NOTES_v1.0.2.md` |
| CHANGELOG | `CHANGELOG.md` |

## Test Commands

```powershell
# Full verification (all gates)
.\scripts\verify.ps1

# Quick pre-commit check
.\scripts\verify.ps1 -Quick

# C# build
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# C# tests
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64

# Python tests
python -m pytest tests/

# Single Python test
python -m pytest tests/path/to/test.py::TestClass::test_name -v
```

## Operational Procedures

### Starting the Backend

```powershell
cd e:\VoiceStudio
python -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000
```

### Launching the App

```powershell
dotnet run --project src/VoiceStudio.App/VoiceStudio.App.csproj -c Debug -p:Platform=x64
```

### Running the Installer Build

```powershell
iscc installer/VoiceStudio.iss
```

## GA Tag Verification

- [ ] Tag `v1.0.2` exists: `git tag -l v1.0.2`
- [ ] Tag points to correct commit: `git show v1.0.2 --oneline`
- [ ] Release notes match tag version

## Known Issues

| ID | Severity | Description |
|----|----------|-------------|
| VS-0043 | S4 Chore | mypy --strict audit: baseline with incremental fix plan |

## Role Reference

| Role | Guide |
|------|-------|
| Overseer (0) | `docs/governance/roles/ROLE_0_OVERSEER_GUIDE.md` |
| System Architect (1) | `docs/governance/roles/ROLE_1_SYSTEM_ARCHITECT_GUIDE.md` |
| Build & Tooling (2) | `docs/governance/roles/ROLE_2_BUILD_TOOLING_GUIDE.md` |
| UI Engineer (3) | `docs/governance/roles/ROLE_3_UI_ENGINEER_GUIDE.md` |
| Core Platform (4) | `docs/governance/roles/ROLE_4_CORE_PLATFORM_GUIDE.md` |
| Engine Engineer (5) | `docs/governance/roles/ROLE_5_ENGINE_ENGINEER_GUIDE.md` |
| Release Engineer (6) | `docs/governance/roles/ROLE_6_RELEASE_ENGINEER_GUIDE.md` |
| Debug Agent (7) | `docs/governance/roles/ROLE_7_DEBUG_AGENT_GUIDE.md` |
