# Plugin System Guidelines

> **Canonical Source** — This document is the single source of truth for plugin system governance in VoiceStudio.
> **ADR:** [ADR-036-plugin-system-unification](../architecture/decisions/ADR-036-plugin-system-unification.md)
> **Owner:** Overseer (Role 0)
> **Last Updated:** 2026-02-16
> **Origin:** External architectural review (ChatGPT analysis) adapted for VoiceStudio project standards.

---

## Purpose

These guidelines govern the design, implementation, security, and lifecycle of the VoiceStudio plugin system. They apply to all plugin-related work across both the Python backend and the C#/WinUI frontend. Every contributor working on plugins — whether core infrastructure or individual plugin development — must follow these rules.

---

## 1. Architectural Principles

### 1.1 Modularity Over Monolith

- Decouple optional features into plugins rather than embedding them in the core application.
- Each plugin is a self-contained unit with its own manifest, entry points, dependencies, and tests.
- A **unified manifest schema** (`shared/schemas/plugin-manifest.schema.json`) is the single contract between plugins and both the backend and frontend host systems.

### 1.2 Separation of Concerns

- **UI layer** (WinUI/XAML): Presentation only. No business logic in views or code-behind.
- **API layer** (FastAPI routes): Request validation and routing. No engine-level logic.
- **Service layer** (`backend/services/`): Business logic and orchestration.
- **Engine layer** (`app/core/engines/`): ML inference and audio processing.
- Plugins may span multiple layers but must respect these boundaries. A plugin's backend component communicates with its frontend component exclusively through the established HTTP/WebSocket boundary (per ADR-007).

### 1.3 Plan for Evolution

- All plugins and plugin APIs use **semantic versioning** (MAJOR.MINOR.PATCH).
- Breaking changes to the plugin API require a **deprecation period** of at least one minor release with migration documentation.
- The manifest schema includes `min_app_version` and `min_api_version` fields to enforce compatibility.
- Schema evolution must be backward-compatible; new fields are optional with documented defaults.

---

## 2. Security and Isolation

### 2.1 Defense in Depth

Combine multiple security mechanisms — no single mechanism is sufficient alone:

| Layer | Mechanism | Implementation |
|-------|-----------|----------------|
| **Manifest** | Schema validation | Reject malformed manifests before loading |
| **Permissions** | Capability-based model | Plugins declare required permissions; users approve |
| **Sandbox** | Restricted file/network access | Plugins access only approved paths and endpoints |
| **Resources** | Per-plugin budgets | CPU, memory, I/O limits (see §3) |
| **Integrity** | Digital signatures | Verify plugin authenticity before loading (Phase 2+) |

### 2.2 Least Privilege by Default

- Plugins receive **zero permissions** unless explicitly declared in the manifest `permissions` array and approved by the user.
- Granular permission tokens: `filesystem.read`, `filesystem.write`, `network.http`, `network.websocket`, `system.process`, `system.info`, `audio.input`, `audio.output`, `user.credentials`, `clipboard`, `notifications`.
- High-risk permissions (`filesystem.write`, `system.process`, `user.credentials`) display prominent warnings in the approval dialog.

### 2.3 Sandboxed Execution

- Phase 1: In-process sandbox restricting file paths and network access via permission checks.
- Future phases: Explore WebAssembly or container-based isolation for untrusted third-party plugins.
- Plugins must **never** access user credentials, API keys, or secrets directly. Use the host's `SecureStorageService` through a controlled API.

### 2.4 Authenticity Verification

- Phase 1: Manifest schema validation + checksum verification.
- Phase 2+: Require digital signatures on plugin packages. Reject unsigned plugins in production unless the user explicitly overrides with a security warning.

---

## 3. Performance and Resource Management

### 3.1 Per-Plugin Resource Budgets

| Resource | Default Limit | Action on Exceed |
|----------|--------------|------------------|
| **Load time** | 1,000 ms | Log warning; skip plugin with user notification |
| **Memory** | 100 MB | Throttle; deactivate if sustained |
| **CPU (single operation)** | 30 seconds | Terminate operation; log error |
| **Disk I/O** | Scoped to approved paths | Block with `PermissionError` |

- Limits are configurable per-plugin via the plugin management UI.
- Plugins that repeatedly exceed budgets are automatically disabled with a logged explanation.

### 3.2 Performance Targets

| Metric | Target |
|--------|--------|
| Discover 10 plugins | < 500 ms |
| Validate single manifest | < 50 ms |
| Load single plugin | < 1,000 ms |
| Backend-frontend sync latency | < 100 ms |
| Memory overhead per plugin | < 10 MB idle |

- Track these metrics in structured logs. Regression beyond targets blocks release (see §5).

---

## 4. Developer Experience

### 4.1 Documentation Requirements

Every plugin system release must include or update:

| Document | Location | Purpose |
|----------|----------|---------|
| Getting Started Guide | `docs/plugins/getting-started.md` | Zero-to-working-plugin tutorial (< 2 hours) |
| API Reference (Backend) | `docs/plugins/api-reference-backend.md` | Complete Python API docs |
| API Reference (Frontend) | `docs/plugins/api-reference-frontend.md` | Complete C# API docs |
| Best Practices Guide | `docs/plugins/best-practices.md` | Patterns, anti-patterns, security |

### 4.2 Templates and Tooling

- Maintain **four plugin templates** in `templates/`: minimal-backend, minimal-frontend, full-stack, audio-effect.
- Provide a **CLI generator** (`tools/plugin-generator/`) that scaffolds new plugins from templates.
- Templates must be production-ready: proper error handling, logging, tests, configuration, and README.
- Generated plugins must pass manifest validation and load successfully without modification.

### 4.3 Reference Plugin

- Maintain a canonical **example plugin** (`plugins/example_audio_effect/`) demonstrating full-stack capabilities.
- The example plugin is the living specification: if behavior is ambiguous, the example is authoritative.
- Update the example plugin whenever the plugin API changes.

### 4.4 Error Messages

- All plugin errors (validation, permission, load, runtime) must be **actionable**: state what went wrong, where, and what to do about it.
- Manifest validation errors include the field path and expected format.
- Permission denials specify which permission was missing and how to grant it.
- Runtime errors include the plugin ID, operation context, and stack trace in logs.

---

## 5. Testing and Quality Assurance

### 5.1 Plugin Testing Requirements

| Test Type | Requirement | Scope |
|-----------|-------------|-------|
| **Unit tests** | Required | Plugin business logic |
| **Integration tests** | Required | Plugin loading, API endpoints, bridge sync |
| **Performance tests** | Required for audio/engine plugins | Load time, processing latency, memory |
| **Security tests** | Required | Sandbox bypass attempts, permission enforcement |

### 5.2 Automated Security Checks

- Run static analysis (`ruff`, `mypy`) on plugin Python code in CI.
- Run dependency vulnerability scanning (`pip-audit`) on plugin requirements.
- Simulate malicious manifests (excessive permissions, invalid paths, injection attempts) in integration tests.
- Test sandbox boundary enforcement: verify unauthorized file/network access is blocked.

### 5.3 Release Gates

Before any plugin system release:

- [ ] All unit and integration tests pass
- [ ] Performance targets met (§3.2)
- [ ] Security tests pass (sandbox, permissions)
- [ ] Documentation updated for API changes
- [ ] Example plugin loads and functions correctly
- [ ] `scripts/verify.ps1` exits GREEN

---

## 6. Governance and Version Control

### 6.1 Plugin Ledger

Maintain a plugin state ledger tracking:

| Field | Description |
|-------|-------------|
| Plugin ID | Unique identifier |
| Version | Installed version |
| State | discovered, loaded, activated, deactivated, error |
| Permissions | Granted permission set |
| Last State Change | Timestamp + reason |
| Backend Loaded | Boolean |
| Frontend Loaded | Boolean |
| Sync Status | synchronized, backend-only, frontend-only, error |

- The **plugin bridge service** maintains this ledger and provides it to the management UI.
- State changes are logged with structured log entries for audit trails.

### 6.2 Review Processes

- Plugin system infrastructure changes require **code review** per standard project workflow.
- Community-submitted plugins require review against:
  - Manifest schema compliance
  - Permission justification (why does it need each permission?)
  - No suppression of errors or warnings
  - Test coverage
  - Security scan results
- Architectural decisions affecting the plugin system require an **ADR** (see `docs/architecture/decisions/`).

### 6.3 Git Conventions

- Plugin infrastructure commits: `feat(plugins): <description>` or `fix(plugins): <description>`.
- Individual plugin commits: `feat(plugin/<name>): <description>`.
- Plugin API breaking changes: `feat(plugins)!: <description>` with migration notes in body.

---

## 7. User Interface and Experience

### 7.1 UI Invariants

- Plugin UI panels must use **VSQ design tokens** from `src/VoiceStudio.App/Resources/DesignTokens.xaml`.
- Follow Fluent Design guidelines consistent with the rest of VoiceStudio.
- Plugin panels must support light/dark themes via `ThemeResource`.
- All interactive elements must have `AutomationProperties.AutomationId` for UI testing.

### 7.2 Plugin Management UX

- **Gallery view**: Browse available plugins with search, filter by type, and status indicators.
- **Status indicators**: Clear visual distinction between loaded, active, error, and disabled states.
- **Permission dialog**: Concise explanations for each permission; prominent warnings for high-risk permissions.
- **Settings integration**: Plugin settings accessible through the standard settings panel; no separate config files exposed to users.

### 7.3 No Clutter

- Plugins do not create top-level menu items without user consent.
- Related settings are consolidated; plugins that add multiple settings group them under a single section.
- Disabled or unloaded plugins do not leave UI artifacts.

---

## 8. Dependency and Compatibility Management

### 8.1 Explicit Dependencies

- Python dependencies: declared in manifest `dependencies.python` array with semantic version ranges (e.g., `numpy>=1.20.0,<2.0`).
- Plugin dependencies: declared in manifest `dependencies.plugins` array.
- System dependencies: declared in manifest `dependencies.system` array (e.g., `ffmpeg`).
- C# dependencies: declared in the plugin's `.csproj` with explicit version pins.

### 8.2 Compatibility Verification

- Before loading, verify `min_app_version` and `min_api_version` against current VoiceStudio version.
- Skip incompatible plugins with a **logged warning** and user-visible notification (never silent).
- The compatibility matrix (`config/compatibility_matrix.yml`) governs core dependency versions; plugins must not conflict with pinned versions.

### 8.3 Hardware Awareness

- Engine plugins that require GPU must declare hardware requirements in the manifest.
- The plugin loader checks available hardware (via existing GPU detection) and skips plugins whose requirements cannot be met, with clear feedback.
- Document fallback paths (CPU vs GPU) in the plugin manifest and documentation.

---

## 9. Risk and Issue Management

### 9.1 Plugin Risk Register

Plugin-specific risks are tracked in the project risk register (`docs/governance/RISK_REGISTER.md`) under the `PLUGINS` category. Key standing risks:

| Risk | Severity | Mitigation |
|------|----------|------------|
| Malicious plugin compromises host | HIGH | Sandbox + permissions + signatures + review |
| Plugin crash takes down application | HIGH | Error boundaries + future process isolation |
| Schema drift between layers | MEDIUM | Single schema source of truth + CI validation |
| Plugin ecosystem fragmentation | MEDIUM | Centralized manifest standard + version checks |
| Maintenance burden of plugin infrastructure | MEDIUM | Automated testing + templates reduce overhead |

### 9.2 Drift and Conflict Tracking

- Deviations from these guidelines are logged in the Quality Ledger under `PLUGINS` category.
- Conflicts between plugin requirements and core application constraints are resolved through ADRs.
- The Overseer reviews plugin-related drift during regular gate checks.

---

## 10. Logging and Observability

### 10.1 Structured Logging

All plugin system logs must include:

| Field | Required | Example |
|-------|----------|---------|
| `plugin_id` | Yes | `example_audio_effect` |
| `event` | Yes | `plugin_loaded`, `permission_denied`, `sandbox_violation` |
| `level` | Yes | `INFO`, `WARNING`, `ERROR` |
| `context` | When applicable | Operation name, file path, endpoint |
| `correlation_id` | When applicable | Request correlation ID |

### 10.2 Health Endpoints and Metrics

- The backend exposes plugin health at `/api/plugins/health` (aggregate status of all plugins).
- Individual plugin status available at `/api/plugins/{id}/status`.
- Metrics tracked: load count, error count, average load time, memory usage, permission grants/denials.
- These metrics feed into the existing SLO dashboard and diagnostics panel.

---

## Cross-References

| Topic | Document |
|-------|----------|
| Architecture decision | [ADR-036](../architecture/decisions/ADR-036-plugin-system-unification.md) |
| Sacred boundaries | `.cursor/rules/core/architecture.mdc` |
| Error handling | `.cursor/rules/quality/no-suppression.mdc` |
| Security | `.cursor/rules/security/secure-coding.mdc` |
| Free-only policy | `.cursor/rules/core/free-only.mdc` |
| Compatibility matrix | `config/compatibility_matrix.yml` |
| Risk register | `docs/governance/RISK_REGISTER.md` |
| Quality Ledger | `docs/archive/Recovery_Plan/QUALITY_LEDGER.md` |
| Design tokens | `src/VoiceStudio.App/Resources/DesignTokens.xaml` |

---

## Changelog

| Date | Change | Author |
|------|--------|--------|
| 2026-02-16 | Initial version — 10-section guidelines from external review | Overseer |
