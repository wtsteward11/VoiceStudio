# VoiceStudio — UI startup boundary (code truth + harness alignment)

**Date:** 2026-04-05  
**Scope:** Classify WinUI startup readiness vs backend cold-start; align UI-integrated chain with subprocess-only proofs (`VOICESTUDIO_RUNTIME_CHAIN_PROOF_2026-04-05.md`, startup reopen closure).

## 1) UI-integrated startup chain (authoritative components)

| Phase | Component | Code-truth behavior |
|-------|-----------|---------------------|
| Orchestration | `App.xaml.cs` / `MainWindow.xaml.cs` | Startup overlay; `EnsureBackendWithTrackingAsync`; transitions `Starting` → `BackendStarting` → `BackendReady` / `BackendFailed`. |
| Process + health | `BackendProcessManager` | Spawns backend when needed; polls `GET /health` on `HttpClient` `BaseAddress` (must match `BackendClientConfig` / env). |
| Retry | `StartupRetryCoordinator` | `HealthTimeout` → up to **2** retries with **5s** delay between attempts (extends effective health window beyond single wait). |
| Authority | `BackendClientConfig`, `VOICESTUDIO_BACKEND_URL`, `VOICESTUDIO_API_PORT` | `127.0.0.1` preferred over `localhost` to avoid IPv4/IPv6 split-brain. |

## 2) Failure classification

| Class | Symptom | Mitigation in repo |
|-------|---------|-------------------|
| **Timeout budget vs cold start** | Backend alive but `/health` late (> single-window budget on slow disk/AV) | `StartupReadinessTimeoutSeconds` **60** (was 45) + health-timeout retries (~5s gap) in `StartupRetryCoordinator`. |
| **Authority drift** | Process listens but client probes wrong host/port | Single env + `BackendClientConfig`; TCP milestone logs use `127.0.0.1`. |
| **Handshake / gating** | Overlay stuck | Failure-path + icon-launch stages in `verify.ps1` (Stages 8.6–8.8). |

## 3) Observed cold-start context (non-binding)

Subprocess probes documented ~**40s** to first healthy `/health` on a representative dev machine (see runtime chain proof). **Not** a guarantee; UI budget must exceed tail latency.

## 4) Proof procedure (operator + CI)

1. Build: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. Harness (post-build):  
   - `.\scripts\verify.ps1 -OnlyStage "UI Self-Test" -SkipBuild`  
   - `.\scripts\verify.ps1 -OnlyStage "Icon-Launch Smoke" -SkipBuild`  
   - `.\scripts\verify.ps1 -OnlyStage "Failure-Path Smoke" -SkipBuild`  
   - `.\scripts\verify.ps1 -OnlyStage "Runtime-Missing Failure Smoke" -SkipBuild`
3. Live UI: capture `%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json` after a clean launch — fields include `timeout_seconds`, `decision`, `backend_pid`, `elapsed_ms`.
4. Rolling verifier: `python scripts/run_verification.py` (gate + ledger; optional `--build` per task brief).

## 5) Artifacts

- Code: `src/VoiceStudio.App/Services/BackendProcessManager.cs` (`StartupReadinessTimeoutSeconds`)  
- Harness: `scripts/verify.ps1`, `scripts/icon-launch-failure-smoke.ps1`, `scripts/runtime-missing-failure-smoke.ps1`  
- Related: [VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md](VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md)

**Status:** **PASS** — classification documented; timeout margin widened; harness paths aligned with `verify.ps1` stage names.
