# AGENTS

## Required Rules (alwaysApply)

- `.cursor/rules/core/rule-governance.mdc` ⚠️ AGENTS CANNOT MODIFY RULES WITHOUT USER CONSENT
- `.cursor/rules/core/project-context.mdc`
- `.cursor/rules/core/architecture.mdc`
- `.cursor/rules/core/local-first.mdc`
- `.cursor/rules/core/free-only.mdc`
- `.cursor/rules/core/anti-drift.mdc`
- `.cursor/rules/security/api-key-management.mdc`
- `.cursor/rules/security/secure-coding.mdc`
- `.cursor/rules/security/mcp-security.mdc`
- `.cursor/rules/workflows/git-conventions.mdc`
- `.cursor/rules/workflows/planning.mdc` — Multi-phase execution plan standards
- `.cursor/rules/mcp/mcp-usage.mdc`
- `.cursor/rules/quality/repo-hygiene.mdc`
- `.cursor/rules/quality/no-suppression.mdc` — Errors are NEVER suppressed, always fixed
- `.cursor/rules/quality/no-defer.mdc` — No deferring work without explicit user approval (effective 2026-02-12)
- `.cursor/rules/workflows/state-gate.mdc`
- `.cursor/rules/workflows/closure-protocol.mdc`
- `.cursor/rules/workflows/verifier-subagent.mdc`
- `.cursor/rules/workflows/verification-harness.mdc` — No changes unless scripts/verify.ps1 stays GREEN
- Extra subproject rules: `runtime/external/invokeai/invokeai/frontend/web/CLAUDE.md` and `runtime/external/localai/AGENTS.md`

## Truth Hierarchy

When docs disagree, precedence is (highest first): **current code** → **ADRs** → **CI results** → **STATE.md** → **CLAUDE.md** → conversation. See [CLAUDE.md](CLAUDE.md) § Truth Hierarchy (lines 51–63). **Session state oracle**: `.cursor/STATE.md`. **openmemory.md**: memory-first workflow per openmemory.mdc; not source of architectural truth.

## Professional Standard

- Operate as a senior executive professional software engineer and architect.
- Prefer correctness, maintainability, and verification over speed.
- Document decisions, risks, and rollback steps for non-trivial changes.
- Follow `.cursor/STATE.md` context protocol before code modifications.

## Build and test commands

- **Full Verification**: `.\scripts\verify.ps1` — Single source of truth; must be GREEN before any merge
- **Quick Verification**: `.\scripts\verify.ps1 -Quick` — Pre-commit check (~30 seconds)
- Build (WinUI/C#): `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Test (C#): `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- Test (Python): `python -m pytest tests`
- Single pytest: `python -m pytest tests/path/to/test_file.py::TestClass::test_name`
- Single MSTest: `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Namespace.Class.Test"`

## Project structure

- Frontend: `src/VoiceStudio.App/` (WinUI 3 MVVM).
- Core library: `src/VoiceStudio.Core/`.
- Backend: `backend/api/` and `backend/services/`.
- Engine layer: `app/core/` with manifests in `engines/`.
- Tests: `tests/` and `src/VoiceStudio.App.Tests/`.
- Packaging: `installer/`.

## Architecture boundaries

- Control plane: UI ↔ Backend via HTTP REST + WebSocket (ADR-007).
- Data plane: Backend ↔ Engine subprocess via IPC (ADR-007).

## Platform constraint

VoiceStudio is a **native Windows installed application** (ADR-010):
- WinUI 3 frontend, not Electron or web-based.
- Distributed via Windows installer.
- Offline-capable for core synthesis/transcription.

## Code style

- Follow `.editorconfig` and language-specific rules under `.cursor/rules/languages/`.
