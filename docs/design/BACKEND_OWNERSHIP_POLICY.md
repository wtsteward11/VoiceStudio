# Backend Ownership Policy

**Status:** Accepted  
**Last Updated:** 2026-03-14  
**Related:** [BackendProcessManager.cs](src/VoiceStudio.App/Services/BackendProcessManager.cs), [STARTUP_ORCHESTRATION_HARDENING_PLAN.md](STARTUP_ORCHESTRATION_HARDENING_PLAN.md)

## Purpose

Explicit rules for backend lifecycle so behavior is predictable and debuggable. The frontend owns backend startup; the backend process is a child of the product launch contract.

---

## Policy Rules

### 1. Reuse

If `/health` succeeds on the expected port, **reuse** the existing backend. Do not start a new process.

**Implementation:** [BackendProcessManager.cs](src/VoiceStudio.App/Services/BackendProcessManager.cs) — `EnsureBackendRunningAsync` checks `IsBackendHealthyAsync` first; if healthy, invokes `BackendStarted` and returns `true` without spawning.

---

### 2. Port Occupied by Foreign Process

If the port is in use and `/health` does not succeed, **fail clearly**. Do not attempt to start a new backend on the same port.

**Message:** "Port {port} is in use by another process. Stop the other process or set VOICESTUDIO_API_PORT to use a different port."

**Implementation:** [BackendProcessManager.cs:116-128](src/VoiceStudio.App/Services/BackendProcessManager.cs) — `IsPortInUseAsync` + `IsBackendHealthyAsync`; if port in use and not healthy, `BackendStartFailed` with the above message.

---

### 3. Stale VoiceStudio Backend

If the backend process is running but not healthy within timeout, **kill and restart**.

**Implementation:** [BackendProcessManager.cs:75-94](src/VoiceStudio.App/Services/BackendProcessManager.cs) — `WaitForHealthAsync` 10s; if still unhealthy, `Kill(entireProcessTree: true)`; then `StartBackendProcessAsync`.

---

### 4. Frontend Exit

Backend is **not** auto-stopped when the frontend exits.

**Rationale:** Long-running backend supports CLI/headless use, multiple frontend instances (single-instance mutex prevents multiple WinUI shells, but backend may serve other clients), and developer workflows (backend stays up between debug sessions).

**Future:** Installer/uninstaller may offer a "stop backend" option. Not currently implemented.

---

### 5. App Root Not Found

If `FindAppRoot` returns null, **fail** with a message directing the user to set `VOICESTUDIO_APP_ROOT`.

**Message:** "Could not find VoiceStudio app root. Set VOICESTUDIO_APP_ROOT to the app directory."

**Implementation:** [BackendProcessManager.cs:132-138](src/VoiceStudio.App/Services/BackendProcessManager.cs).

---

### 6. Python Runtime Not Found

If no Python executable is found in the checked paths, **fail** with a message listing the paths checked.

**Paths checked:** `{appRoot}/Runtime/python/python.exe`, `{appRoot}/venv/Scripts/python.exe`, `{appRoot}/.venv/Scripts/python.exe`

**Implementation:** [BackendProcessManager.cs:154-161](src/VoiceStudio.App/Services/BackendProcessManager.cs).

---

## Verification

See [STARTUP_ORCHESTRATION_HARDENING_PLAN.md](STARTUP_ORCHESTRATION_HARDENING_PLAN.md) — Failure-Mode Proof and Icon-Launch Smoke Proof sections.
