
# VoiceStudio – Execution Runbook (Five-Agent Plan)

This runbook is a step‑by‑step guide to execute the VoiceStudio build with 5 parallel agents (human or AI) and stitch the parts back together safely. Keep the app **free** and **offline‑only**. Use this as your source of truth during the build.

---

## 0) What “agents” means here
An **agent** can be:
- a focused human team, or
- an AI coding assistant you point at a scoped folder/branch with strict prompts and tests.

We’ll run **five** agents in parallel (A–E), each owning a layer of the system with **clear interfaces** and **merge gates**. You can mix humans + AI; the process remains the same.

---

## 1) Prerequisites (install once)
- Windows 11/10 x64 dev machine.
- **Git** (latest).
- **Visual Studio 2022** (Desktop dev with .NET).
- **.NET 8 SDK**.
- **Python 3.11** (x64), `pip`, `venv`.
- **FFmpeg** (LGPL build) on PATH, or place binary in `%LOCALAPPDATA%\VoiceStudio\cache\ffmpeg`.
- Optional GPU:
  - NVIDIA: recent driver; CUDA runtime via PyTorch wheels.
  - AMD/Intel: DirectML (ONNX Runtime DirectML).
- (Optional) Jira or Linear workspace.

---

## 2) Create the repository (Day 0)
```powershell
mkdir VoiceStudio && cd VoiceStudio
git init
mkdir src, workers, plugins, installers, docs, assets, build
# Copy the spec markdown + CSVs into docs/ and planning/
mkdir planning
# (Download provided files then place them under /planning)
```

Recommended top-level layout (already defined in the spec):
```
/src/App        /src/Core     /src/IPC     /src/PluginHost /src/Cli
/workers/python/vsdml
/plugins/sdk-csharp  /plugins/sdk-python  /plugins/samples
/installers/msix  /installers/winget
/docs  /assets  /planning
```

Commit the plan:
```powershell
git add .
git commit -m "chore: bootstrap repo structure and planning assets"
```

---

## 3) Import the backlog
### Jira
1) Create a VoiceStudio project (KANBAN recommended).
2) For each agent, import the **Agent_*_Jira.csv** file (Issues → Import from CSV).
3) Map columns: Summary→Summary, Issue Type→Issue Type, Description→Description, Labels→Labels, Story Points→Story Points.
4) After import, create Epics for milestones (M0…M7) and bulk set epic links by label if desired.

### Linear
1) Create 5 teams named per agent.
2) For each team, import the **Agent_*_Linear.json** under that team.

---

## 4) Branching model
- `main` — always bootable.
- `integrate/mX` — per‑milestone integration (e.g., `integrate/M0`).
- `feat/<ticket-id>-short-desc` — per ticket branch.

Protection rules:
- Require PR, green CI, and code owners: A→`src/App`, B→`src/Core, src/IPC`, C→`src/PluginHost, plugins/sdk-*`, D→`/workers/python`, E→`installers, .github`.

---

## 5) Continuous Integration (CI)
Use GitHub Actions or Azure DevOps. Minimal GitHub Actions skeleton:

**.github/workflows/ci.yml**
```yaml
name: ci
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - name: Build .NET
        run: dotnet build src/App/VoiceStudio.App.sln -c Release
      - name: Setup Python
        uses: actions/setup-python@v5
        with: { python-version: '3.11' }
      - name: Install worker deps
        run: |
          python -m venv .venv
          .venv\Scripts\pip install -r workers\python\requirements.txt
      - name: Unit tests
        run: dotnet test --nologo
      - name: Contract tests
        run: powershell -File build\run_contract_tests.ps1
```

Add a **nightly** job that builds MSIX (unsigned) and runs smoke launch with headless flags.

---

## 6) Shared Interfaces (IPC proto) – Source of Truth
- Location: `src/IPC`.
- Owner: **Agent B**.
- Process:
  1) Propose changes via PR that bumps `0.MINOR.PATCH` (SemVer).
  2) Generate stubs:
     - **C# NuGet** (local artifact feed): `dotnet pack` → `nuget push` to local feed `./build/nuget/`.
     - **Python wheel**: `python -m build` → drop into `workers/python/dist/` or a local index.
  3) Agents A, C, D update their package refs; CI enforces version match.

---

## 7) Environment determinism
- Each plugin/worker uses a **locked venv**:
  ```powershell
  cd workers/python
  python -m venv .venv
  .venv\Scripts\pip install -r requirements.txt
  pip freeze --require-virtualenv > requirements.lock
  ```
- Check `requirements.lock` into repo for reproducibility.
- Keep an offline wheel cache under `/installers/wheels-cache` for air-gapped installs.

---

## 8) Kickoff per Agent (Day 1)
### Agent A – App Shell & UX
- Clone tasks from **Agent_A_App_Backlog.csv**.
- Create `src/App` WinUI 3 project; add pages: Home, Dataset Studio, Voice Hub, Train/Infer, Export, Settings.
- Consume **IPC C# client stubs** (NuGet from `src/IPC` package).

Prompt template (for AI assistant):
> You are Agent A (WinUI 3). Only work in /src/App. Consume IPC client stubs. Do not call Python directly. For long tasks, call Core’s APIs asynchronously. Follow MVVM, accessibility, and offload heavy work.

### Agent B – Core & Job Engine
- Start from **VS-M0-03** schema and **VS-M0-05** IPC host.
- Implement Job DAG orchestrator with content-hash caching and resumable checkpoints.
- Expose CLI in `src/Cli` for batch operations.

Prompt:
> You are Agent B (Core). Only work in /src/Core and /src/Cli and /src/IPC. You own the SQLite schema, job orchestrator, FFmpeg runner, and IPC servers. Ensure idempotency and WAL mode. No UI code.

### Agent C – Plugin Host & SDKs
- Build manifest parser, signature check, supervisor, Job Objects caps, staging/rollback, Safe Mode.
- Ship SDKs and sample plugins under `/plugins`.

Prompt:
> You are Agent C (Plugin Host). Work in /src/PluginHost and /plugins/sdk-*. Provide sample plugins. Enforce isolation, version handshakes, staged updates, rollback, and Safe Mode.

### Agent D – ML Workers (ASR/Align/TTS/VC)
- Implement Python gRPC servers for VAD/ASR/Align/Train/Infer.
- Integrate Piper (TTS) and RVC (VC). Keep config in YAML or JSON.

Prompt:
> You are Agent D (ML Workers). Work only in /workers/python/vsdml. Expose gRPC services as defined in src/IPC. No Windows UI. Provide unit tests and golden audio tests.

### Agent E – Build, Packaging & QA
- Stand up CI workflows, MSIX packaging, contract tests, chaos/load tests, install matrix.
- Maintain offline wheel cache.

Prompt:
> You are Agent E (Build & QA). Work in /installers, /build, and CI config. Ensure every merge to integrate/mX and main runs smoke + contract tests. Gate releases.

---

## 9) Daily Cycle
1) **Standup** (15 min): identify blockers, decide if `integrate/mX` is mergeable today.
2) Agents code on feature branches and open PRs with AC screenshots/logs.
3) CI runs unit + contract tests. Failures block merge.
4) **Integration**: merge to `integrate/mX`; run smoke (launch app, handshake, synth short text).
5) If green for 2 consecutive days, fast-forward `main` to `integrate/mX`.

---

## 10) Milestone Reassembly (Gates)
- **M0 Gate**: App boots; DB WAL; worker health ping; Null plugin loads.
- **M1 Gate**: Import → Convert → VAD pipeline produces segments & waveform view.
- **M2 Gate**: Transcript import + ASR + Align produce manifests; editor re-aligns.
- **M3 Gate**: Piper/RVC train+infer on sample data; Voice Profiles active.
- **M4 Gate**: Crash isolation, staged plugin update → auto-rollback verified; Safe Mode boots.
- **M5 Gate**: Plugin Gallery, export with provenance toggle, accessibility checks pass.
- **M6 Gate**: SDKs + sample plugins pass contract harness.
- **M7 Gate**: Signed MSIX; optional WinGet; offline install works end-to-end.

Each gate has a **checklist** (tick in PR description) and must pass on a clean machine VM.

---

## 11) Risk & Rollback
- If a plugin update crashes repeatedly: auto-quarantine + rollback previous version.
- If DB migration fails: restore pre-flight backup and open a hotfix PR.
- Safe Mode path for users: hold **Shift** at boot or toggle in splash.

---

## 12) First Run (smoke test after M0)
```powershell
# Core launches worker
src\Core\bin\Debug\net8.0\VoiceStudio.Core.exe --start-worker
# App connects
src\App\bin\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe --safe
```
Expected: UI loads, hardware banner appears, “Workers: Healthy”.

---

## 13) Packaging (M7)
- Build MSIX from `installers/msix` with app identity + icons.
- Sign with test certificate; distribute unsigned to staging channel if needed.
- Optional: generate WinGet manifests and submit PR.

---

## 14) Make it durable (save the plan)
- Check **this runbook** into `/docs/Runbook.md`.
- Check all CSV/JSON planning files into `/planning/`.
- Pin IPC, worker requirements, and CI configs in repo. The repo becomes your durable memory.

---

## 15) Success Criteria (Go/No-Go)
- Crash-free rate ≥ 99.5% during dogfood.
- End-to-end offline install succeeds on Win10/11 with GPU/no-GPU.
- MOS ≥ 4.0 (subjective), VC F0 corr ≥ 0.85 on held-out set.
- All contract tests and plugin certification harness pass on main.

---

**You’re ready.** Start with M0 tickets for Agents A–E. Keep `integrate/M0` green, then roll to M1.
