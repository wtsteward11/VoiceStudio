# Bounded Slice 16 — RHVoice support-contract (frozen)

**Status:** Accepted (2026-04-18)  
**Purpose:** Single source of truth for how VoiceStudio discovers and runs the RHVoice CLI, how that relates to engine config, and which operating modes are honestly supported on Windows vs external/Linux paths.

## 1. Config → preflight → runtime trace

| Stage | Source | Resolution order |
| --- | --- | --- |
| **Engine config** | `backend/config/engine_config.json` → `engine_configs.rhvoice.parameters` | Keys defined by manifest `config_schema` (`executable_path`, `voice`, `language`). |
| **Preflight** | `ensure_rhvoice` in `backend/services/model_preflight.py` (mirror: `backend/ml/models/model_preflight.py`) | 1) `parameters.executable_path` if set and path exists → 2) `shutil.which` for `rhvoice-client`, `rhvoice-say`, `rhvoice-cli`, `RHVoice-test` (same order in both files). |
| **Runtime** | `EngineRouter.get_engine("rhvoice")` → `RHVoiceEngine` | **Slice 16:** `get_engine` merges `EngineConfigService.get_engine_init_kwargs("rhvoice", manifest)` so `executable_path` / `voice` / `language` match preflight. Constructor accepts `executable_path` as an alias for legacy `rhvoice_path`. |
| **Executable discovery inside engine** | `RHVoiceEngine._find_executable` / `initialize` | Custom path (file or directory containing the binary) first; then PATH / `.exe` for `rhvoice-client`, `rhvoice-say`, `rhvoice-cli`, `RHVoice-test`. |

## 2. Naming: `rhvoice-client` vs `rhvoice-say`

- Manifest default string for `executable_path` is **`rhvoice-client`** (Linux packages often expose this name).
- Upstream Windows builds may ship **`rhvoice-say.exe`** or **`rhvoice-cli.exe`**. Preflight and `_find_executable` both try **`rhvoice-client`** (plus the others) so operators are not sent in circles between manifest and engine.

## 3. `parameters.executable_path` wiring (post–Slice 16)

- **Supported:** Full filesystem path to an RHVoice CLI binary (or a directory containing it). This is the same parameter preflight and health checks already read.
- **Runtime:** Passed into `RHVoiceEngine` via router init kwargs (merged only for `engine_id == "rhvoice"` to avoid constructor mismatches on other engines).

## 4. Operating modes (A–D) — evidence

| Mode | Supported in product today? | Evidence / owner |
| --- | --- | --- |
| **A — Windows native CLI on stock install** | **No** (not reproducible via winget/choco/scoop in session; no universal PATH story) | [PROOF_SLICE14_RHVOICE_AUDITION.md](../reports/verification/PROOF_SLICE14_RHVOICE_AUDITION.md) Path 1; `checks.rhvoice.ok` false without manual binary. |
| **B — `parameters.executable_path`** | **Yes** (operator points to a real `.exe` or dir) | Code: `ensure_rhvoice` + `RHVoiceEngine` + router merge; manual verification when a binary exists. |
| **C — WSL / Linux-only CLI** | **Not as a first-class wrapper** | No `wsl.exe` indirection in engine or preflight; Linux CI may have CLI on PATH — same contract as PATH discovery. |
| **D — External / manual runtime (tier2)** | **Yes (baseline for Windows)** | `support_tier`: `tier2_best_effort`; stock Windows does not guarantee RHVoice; matrix row stays **pending PASS** until real proof — not “install soon” fiction. |

**Authoritative product stance:** **Mode D** for default Windows deployments; **Mode B** when an operator configures a concrete CLI path or places a supported binary name on PATH.

## 5. Explicit non-support

- **No automatic fallback** to another TTS engine when RHVoice is missing ([no-fallbacks policy](../../.cursor/rules/core/no-fallbacks.mdc)).
- **No** bundled `wsl ...` wrapper unless added as a separate, user-visible ADR-backed feature.

## 6. Related artifacts

- Matrix: [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) (`rhvoice` row).
- Slice 14 proof: [PROOF_SLICE14_RHVOICE_AUDITION.md](../reports/verification/PROOF_SLICE14_RHVOICE_AUDITION.md).
- Manifest: `engines/audio/rhvoice/engine.manifest.json`.

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-18 | Initial contract: trace, modes A–D, executable_path wiring, Mode D baseline for Windows. |
