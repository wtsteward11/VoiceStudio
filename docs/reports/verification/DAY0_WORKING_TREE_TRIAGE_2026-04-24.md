# Day-0 working tree triage (Tasks 182–190; dispositions **191–199**; execution **200–209**)

**Purpose:** Classify the large **modified + untracked** working tree so merge work does not drag random junk. **Slice 27** runtime transcript is **closed** (**Tasks 175–181**); this doc is **merge hygiene only**.

**Captured:** `main` ahead of `origin/main` by **23** commits; snapshot from `git status -sb` on **2026-04-24** (refresh after **Tasks 210–217** material commits — **Task 215**).

**Last dispositions pass:** **2026-04-24** (Tasks **200–209** — execution: junk removal + `.gitignore` + governance/doc deltas; **not** a full `git commit` of the product stack).

**RHVoice CLI check (Tasks 216 / 209):** On this host, **`Get-Command rhvoice-cli`** returns **no** resolvable binary (not on **PATH**). **`engines/audio/rhvoice/engine.manifest.json`** was reset to **HEAD** for the product commit batch — **no** manifest churn without proof.

## Execution log (Tasks 200+)

| Cluster | Action taken | Notes |
| --- | --- | --- |
| **Tasks 210–217 — git commits** | **Landed** | **`b2cb21e9`** — governance-only (STATE, DAY0, CANONICAL_REGISTRY, `.gitignore`, overseer prompts, ENGINE_PARITY baseline in that batch). **`8b87ccc3`** — ENGINE_PARITY Slice 22 skimmer clarity (**Task 213**). **`220f6556`** — docs/proof/slice trees + ADR-053–056 + bounded design contracts (**Task 214** docs lane). **`ea81972a`** — app/backend/tests/scripts + `tools/overseer/data/engine_truth_overrides.json` + engine harnesses (**Task 214** product lane). |
| **`backend/data/stores/effect_chains/*.json` (untracked UUID files)** | **Deleted** from working tree (**22** files) | Ephemeral store dumps per Dispositions; **tracked** fixture JSON under the same directory **left unchanged** (`git ls-files` set preserved). |
| **`processed/`** | **`.gitignore`** — added `processed/` at repo root | Ephemeral outputs; stays local-only. |
| **Product / proof** | **Committed** in **220f6556** + **ea81972a** per bounded lanes above | **`runtime/venvs/`**, **`runtime/vendor/`**, **`tools/whispercpp/`** remain **local/untracked** — not merged in this batch. |
| **`engines/audio/rhvoice/`** | **No edits** in **ea81972a** (manifest at **HEAD**) | **Tasks 209 / 216** — freeze until CLI or `executable_path` proof. |

## Commit bucket manifest (Tasks 201)

Use **`git add -p` / path-scoped `git add`** so each commit stays reviewable. Order matches **§ Next commits** (governance → product → proof/gen).

### Commit 1 — Governance + registry + triage artifact (no `latest_verify_artifact` bump)

- `.cursor/STATE.md`
- `docs/reports/verification/DAY0_WORKING_TREE_TRIAGE_2026-04-24.md`
- `docs/governance/CANONICAL_REGISTRY.md`
- `.gitignore` (includes `processed/` + prior Day-0 junk entries)
- Targeted governance deltas: `docs/governance/roles/ROLE_0_OVERSEER_GUIDE.md`, `docs/governance/overseer/OVERSEER_NEWCOMER_HANDOFF.md`, `.cursor/prompts/ROLE_PROMPTS_INDEX.md`, `.cursor/prompts/ROLE_3_UI_ENGINEER_PROMPT.md` (if part of same governance slice)
- Surgical proof rows only: `docs/reports/verification/ENGINE_PARITY_MATRIX.md`, `docs/reports/verification/PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md` (when changelog archaeology must align with **175–181**)

### Commit 2+ — Product vertical slices (engine + backend + tests + manifest together)

Representative **modified** prefixes from `git status` (group by bounded slice in practice):

- `app/core/engines/*.py`, `app/core/runtime/venv_family_manager.py`, `app/cli/*.py` (new workers)
- `backend/api/routes/`, `backend/services/`, `backend/ml/models/model_preflight.py`, `backend/core/settings.py`, `backend/platform/config/unified_config.py`, `backend/config/engine_config.json`
- `config/engines.config.yaml`, `engines/audio/chatterbox|openvoice|whisper_cpp/engine.manifest.json` (**exclude `rhvoice` manifest** from drive-by edits — **209**)
- `tests/integration/*.py`, `tests/unit/backend/`, `tests/unit/core/engines/`, `pytest.ini`
- `src/VoiceStudio.App.Tests/` (live-backend / playback harnesses)
- `scripts/verify.ps1`, `scripts/engine_readiness_probe.py`, `scripts/generate_engine_truth.py`, `requirements_engines.txt`, `tools/context/sources/*.py`, `tools/overseer_monitor.py`

### Commit 3 — Proof trees + generated truth (only when tied to closed proof rows)

- `docs/reports/verification/slice**/` (large trees), `docs/design/VOICESTUDIO_BOUNDED_SLICE*.md`, ADRs, GAP-069 rows
- `tools/overseer/data/engine_truth_overrides.json` + `docs/reports/verification/generated/engine_truth_v2.json` **only** after manifest/override edits + `python scripts/generate_engine_truth.py` per repo policy

**Task 208:** Do **not** bump `defaults.latest_verify_artifact` for commits **1**–**3** unless a new anchored **`verify.ps1`** run is part of the same intentional batch.

## Buckets (path groups)

| Category | Scope (representative) | Action |
| --- | --- | --- |
| **Product / engine** | `app/core/engines/*`, `backend/services/model_preflight.py`, `backend/api/routes/health.py`, `config/engines.config.yaml`, `engines/audio/*/engine.manifest.json`, `tests/integration/test_*_real.py`, `tests/unit/core/engines/*` | **Commit in coherent slices** (engine + tests + manifests together); verify with targeted pytest + `dotnet test` filters before merge. |
| **Proof / governance** | `docs/reports/verification/**`, `docs/design/VOICESTUDIO_BOUNDED_SLICE*.md`, `docs/governance/*.md`, `.cursor/STATE.md`, ADRs, `tools/overseer/data/*` | **Commit separately** from product when possible; keep links file-targeted; run `test_truth_doc_markdown_links.py` + `test_state_ledger_contract.py`. |
| **Local junk / do not merge** | `backend/data/stores/effect_chains/*.json` (untracked UUID files), `processed/`, large WAV under `docs/reports/verification/slice*/**/*.wav` if unintended | **Discard or `.gitignore`** after confirming not required for proof; **never** merge ephemeral store dumps. |
| **Runtime / venv / vendor (often local)** | `runtime/venvs/`, `runtime/vendor/`, `tools/whispercpp/` | **Usually local-only** or vendor pins with explicit ADR; confirm policy before committing binaries; prefer documented paths over bloating `main`. |
| **Tooling / CI** | `scripts/verify.ps1`, `scripts/generate_engine_truth.py`, `pytest.ini`, `tests/unit/scripts/*` | **Commit with** the governance batch they support; keep `verify.ps1` green. |

## Dispositions (explicit cluster decisions)

| Path / cluster | Category | Disposition | Owner note |
| --- | --- | --- | --- |
| `.cursor/STATE.md`, `DAY0` triage, `CANONICAL_REGISTRY` matrix row, milestone / proof index prose | Governance | **Commit slice 1** (governance-only stack) | Tasks **191–199** hygiene; **no** `defaults.latest_verify_artifact` bump on prose (**Task 198**). |
| `docs/reports/verification/**` PROOF + `slice*/**` (JSON, logs, `session_20260424_175_181_18293`, matrix) | Proof | **Commit with** matching proof index / Truth Sync when closure narrative changes | Already anchored **Tasks 175–181**; do not regress **PASS** language. |
| `docs/design/VOICESTUDIO_BOUNDED_SLICE*.md`, ADRs **053–056**, GAP-069 execution rows, archive STATE | Proof / design | **Commit slice 2+** (vertical “bounded slice” batches) | Group by slice (19–30) to keep review readable. |
| `app/core/engines/*`, `backend/services/*`, `backend/api/routes/health.py`, `config/engines.config.yaml` | Product | **Commit per engine / bounded slice** (OpenVoice, whisper_cpp, router, chatterbox, silero, rhvoice *read-only policy*) | Pair with `tests/unit` + `tests/integration` for same slice. |
| `engines/audio/*/engine.manifest.json` | Product | **Same commit as** adapter + tests for that engine | **RHVoice** (`engines/audio/rhvoice/`): **no edits** without CLI / `executable_path` proof (**Task 199**). |
| `tests/integration/test_*_real.py`, `slice27_whisper_cpp_evidence.py`, `tests/unit/scripts/*` | Product + harness | **Commit with** STT / slice they prove | Keeps `real_*` markers honest. |
| `src/VoiceStudio.App.Tests/**` (live backend, playback) | Product | **Commit with** backend route + engine slice | C# filters align to Python `real_*` lanes. |
| `backend/data/stores/effect_chains/*.json` (untracked UUIDs) | Local junk | **Executed (Tasks 200+):** deleted untracked dumps | Tracked fixture JSON in same dir preserved. |
| `processed/` | Local junk | **Executed (Tasks 200+):** **`processed/`** in root **`.gitignore`** | Ephemeral; stays local-only. |
| `runtime/venvs/`, `runtime/vendor/` | Local / machine | **Defer / do not merge** unless ADR + shrink policy | Document path overrides; avoid committing full venvs. |
| `tools/whispercpp/` | Vendor / optional | **Split:** small shim **commit** if policy allows; large binaries **defer** with ADR | Align with Slice 22 / 27 proof paths. |
| `tools/overseer/data/engine_truth_overrides.json`, `docs/reports/verification/generated/*` | Governance | **Commit only** when overrides or manifests changed + `generate_engine_truth.py` run | Run `test_engine_truth_verify_artifact_alignment.py`. |
| `openmemory.md`, `.vscode/settings.json`, `AGENTS.md` | Mixed | **Review** — commit with governance if doc-only; else product batch | No secrets. |

## Untracked hotspots (explicit)

- **`backend/data/stores/effect_chains/*.json`** — treat as **local junk** unless a feature explicitly commits fixture chains; delete or exclude from PR.
- **`docs/reports/verification/generated/`** — may contain **regenerated** truth JSON; align with `generate_engine_truth.py` policy; do not hand-edit.
- **Slice proof trees** (`docs/reports/verification/slice27/`, `slice19/`, etc.) — **proof/governance**; commit when tied to a closed proof row, not ad hoc.

## Next commits (numbered stack — **Task 192**)

1. **Governance-only** — `.cursor/STATE.md` (ACTIVE WINDOW, **LATEST MILESTONE** order, **Next 3 Steps**, **LATEST PROOF INDEX** row **191–199**), this **DAY0** doc (Dispositions + stack), `docs/governance/CANONICAL_REGISTRY.md` (engine parity matrix summary row), stale **PROOF §27** table rows that still said **`whisper_cpp` pending** where **175–181** is authority; `docs/governance/roles/ROLE_0_OVERSEER_GUIDE.md`, `OVERSEER_NEWCOMER_HANDOFF.md` if touched. **Glob:** `.cursor/STATE.md`, `docs/reports/verification/DAY0_*.md`, `docs/governance/CANONICAL_REGISTRY.md`, `docs/reports/verification/PROOF_SLICE27*.md` (surgical). **No** `defaults.latest_verify_artifact` / STATE **Latest verify artifact** bump unless anchored **`verify.ps1`** (**Task 198**).
2. **Vertical product slices** — For each bounded lane: `app/core/engines/*` + `backend/services/*` + matching `tests/integration/*` + `tests/unit/*` + `engines/audio/<engine>/engine.manifest.json` in **one** reviewable commit per slice (e.g. OpenVoice subprocess, STT router, whisper_cpp integrity, chatterbox venv). **Glob:** `app/core/engines/**`, `backend/**`, `tests/**`, `engines/audio/**`, `config/engines.config.yaml`.
3. **Proof / generated artifacts only** — Large `docs/reports/verification/slice**` trees, `tools/overseer/data/*`, `generated/engine_truth*.json` **only** when tied to a **closed** proof row and optional `verify.ps1` anchor; never mix with **effect_chain** junk.

## Acceptance

- Every top-level **untracked** cluster is either **committed with intent**, **ignored**, or **deleted** before declaring merge-ready.
- No PR mixes **effect_chain JSON dumps** with product logic without an explicit reviewer note.
