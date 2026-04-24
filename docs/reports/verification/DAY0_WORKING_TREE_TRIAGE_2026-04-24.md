# Day-0 working tree triage (Tasks 182–190; dispositions **191–199**; execution **200–209**)

**Purpose:** Classify **modified + untracked** paths so merge work does not drag random junk (bulk product/docs buckets **landed** in Tasks **210–217**; residual tree is **small**). **Slice 27** runtime transcript is **closed** (**Tasks 175–181**); this doc stays the **merge-hygiene** inventory.

**Captured:** `main` ahead of `origin/main` by **27** commits (**Tasks 236–243** governance closure on **2026-04-24** includes **Tasks 226–231** residual hygiene below); tip short hash from `git rev-parse --short HEAD`. Working tree: **clean** — run `git status -sb` to confirm after your next local edits.

**Last dispositions pass:** **2026-04-24** (Tasks **226–231** — residual policy + tracked `generated/` engine truth; **Tasks 218–219** prior control-plane refresh unchanged in substance).

**RHVoice CLI check (Tasks 216 / 209):** On this host, **`Get-Command rhvoice-cli`** returns **no** resolvable binary (not on **PATH**). **`engines/audio/rhvoice/engine.manifest.json`** was reset to **HEAD** for the product commit batch — **no** manifest churn without proof.

## Execution log (Tasks 200+)

| Cluster | Action taken | Notes |
| --- | --- | --- |
| **Tasks 226–231 — residual merge hygiene** | **Landed** | **`.vscode/settings.json`**, **`openmemory.md`**: **reverted to `HEAD`** (no machine-specific merge). **`docs/reports/verification/generated/`**: **tracked** `README.md` + `engine_truth.json` + `engine_truth_v2.json` (regen via `python scripts/generate_engine_truth.py --schema all`); **`stt_hardening_regress_summary.json`** **gitignored** (local STT pack; schema test skips if absent). **`.gitignore`**: `runtime/venvs/`, `runtime/vendor/`, `tools/whispercpp/` (local-only); STT summary ignore path. **No** `latest_verify_artifact` bump (**Task 235**). **No** `engines/audio/rhvoice/` edits (**Task 234**). |
| **Tasks 218–220 — control-plane refresh** | **Landed** | **STATE** Truth Sync + **Current Blocker** + **Next 3 Steps** skimmer (**Tasks 218/222/225**); **DAY0** **Captured** + residual clusters; governance-only commit. **No** `latest_verify_artifact` bump (**Task 224**). |
| **Tasks 210–217 — git commits** | **Landed** | **`b2cb21e9`** — governance-only (STATE, DAY0, CANONICAL_REGISTRY, `.gitignore`, overseer prompts, ENGINE_PARITY baseline in that batch). **`8b87ccc3`** — ENGINE_PARITY Slice 22 skimmer clarity (**Task 213**). **`220f6556`** — docs/proof/slice trees + ADR-053–056 + bounded design contracts (**Task 214** docs lane). **`ea81972a`** — app/backend/tests/scripts + `tools/overseer/data/engine_truth_overrides.json` + engine harnesses (**Task 214** product lane). **`144be99a`** — DAY0 execution log refresh (**Task 215**). |
| **`backend/data/stores/effect_chains/*.json` (untracked UUID files)** | **Deleted** from working tree (**22** files) | Ephemeral store dumps per Dispositions; **tracked** fixture JSON under the same directory **left unchanged** (`git ls-files` set preserved). |
| **`processed/`** | **`.gitignore`** — added `processed/` at repo root | Ephemeral outputs; stays local-only. |
| **Product / proof** | **Committed** in **220f6556** + **ea81972a** per bounded lanes above | **`runtime/venvs/`**, **`runtime/vendor/`**, **`tools/whispercpp/`** remain **local/untracked** — not merged in this batch. |
| **`engines/audio/rhvoice/`** | **No edits** in **ea81972a** (manifest at **HEAD**) | **Tasks 209 / 216 / 223** — **frozen** until CLI or `executable_path` proof; **not** the next merge lane. |
| **Residual local-only (post-210–217)** | **Resolved (Tasks 226–231)** | **`.vscode/settings.json`**, **`openmemory.md`**: **reverted to `HEAD`**. **`generated/`**: tracked canonical JSON + README; STT summary ignored. **`runtime/venvs/`**, **`runtime/vendor/`**, **`tools/whispercpp/`**: **`.gitignore`** (local-only; ADR if ever tracking vendor). |
| **Tasks 236–242 — merge-hygiene closure polish** | **Landed** | Clean-tree proof (§ below); **Task 237** downstream **`whisper_cpp`** audit — matrix + overrides + **`engine_truth*.json`** all **PASS** aligned to **Tasks 175–181** (no stale transcript **pending**); **slice27/README.md** authority vs reruns wording; **generated/** strategic policy paragraph; **STATE** clean-merge-state line; truth-lock + `run_verification.py` **PASS**. **No** verify bar bump. |

## Clean working tree proof (Task 236)

Re-run from repo root before merge; values below are an **auditable snapshot** captured **2026-04-24** after landing **Tasks 236–243**. **`git rev-parse --short HEAD`** must match **`git log -1 --oneline`** (tip); the block below omits the tip line so this section stays stable across doc-only amends — use **`git log --oneline -n 8`** when you need the full stack including tip.

```text
$ git status -sb
## main...origin/main [ahead 27]

$ git log --oneline -n 7 --skip=1 HEAD
3a2c8e31 chore(repo): residual merge hygiene (Tasks 226-231)
c032d902 docs(governance): STATE Truth Sync and DAY0 capture (Tasks 218-220)
144be99a docs(verification): refresh DAY0 capture and execution log (Task 215)
ea81972a feat(platform): STT router preflight registry OpenVoice subprocess and engine live harnesses (Task 214)
220f6556 docs(day0): bounded slice PROOF archives design contracts and verification trees (Task 214)
8b87ccc3 docs(verification): clarify whisper_cpp Slice 22 batch-time vs Slice 27 PASS (Task 213)
b2cb21e9 docs(governance): day-0 merge hygiene state and registry sync (Tasks 210-217)
```

**Interpretation:** working tree **clean** (no staged/unstaged paths in `git status -sb` beyond branch aheadness). Remaining **merge gate** is **integration** (`origin/main` / PR / rebase), not unclassified local junk.

### Downstream `whisper_cpp` PASS audit (Task 237)

| Surface | `whisper_cpp` transcript / runtime truth | Checked |
| --- | --- | --- |
| [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) | STT row cites **Slice 27 Tasks 175–181** runtime transcript **PASS**; Slice 22 = readiness only | OK |
| [tools/overseer/data/engine_truth_overrides.json](../../../tools/overseer/data/engine_truth_overrides.json) | `runtime_proof_status: pass`, `first_blocker: null` | OK |
| [generated/engine_truth_v2.json](generated/engine_truth_v2.json) | `runtime_proof_status: pass`, `matrix_status` aligned | OK |
| [generated/engine_truth.json](generated/engine_truth.json) | v1 manifest projection (no stale pending field for this engine) | OK |

## Next lane after merge-hygiene (Task 243)

**Recommended default:** **push / review / rebase / integrate** with `origin/main` — merge-hygiene control plane is **complete** for this wave. **RHVoice:** remain **frozen** until a real **`rhvoice-cli`** (or documented **`executable_path`**) exists; **do not** promote RHVoice as the next primary lane without that binary. After integration, pick the next **product** lane from [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md) / roadmap — not another governance-only sweep unless a new seam opens.

## Commit bucket manifest (Tasks 201)

**Manifest status (Tasks 219):** **Commit 1** and **Commit 2+** bulk paths from §**Commit 2+** are **executed** on `main` (**`220f6556`** + **`ea81972a`**). Remaining actionable buckets: **governance-only** touch-ups (**STATE**, this **DAY0**), optional **`.gitignore`** for `generated/` / local tooling, and **explicit decisions** on whether **`AGENTS.md`** (if still dirty) merits its own doc commit — **not** re-staging already-landed `app/` / `backend/` trees.

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
| `runtime/venvs/`, `runtime/vendor/` | Local / machine | **Gitignored (Tasks 228)** — do not merge venv/vendor trees; ADR + narrow scope if a subtree must be tracked later | See root **`.gitignore`**. |
| `tools/whispercpp/` | Local tool cache | **Gitignored (Tasks 229)** — same family as `tools/whispercpp184/`; not a vendored subtree | Binaries stay out of `main` unless ADR + provenance. |
| `tools/overseer/data/engine_truth_overrides.json`, `docs/reports/verification/generated/*` | Governance | **`README.md` + `engine_truth.json` + `engine_truth_v2.json` tracked**; regen after override/manifest changes; **`stt_hardening_regress_summary.json`** local-only (ignored) | Run `test_engine_truth_verify_artifact_alignment.py`. |
| `openmemory.md`, `.vscode/settings.json` | Local / machine | **Tasks 226:** **reverted to `HEAD`** — do not merge machine-specific editor state | `openmemory.md` remains in index at **HEAD**; `.gitignore` still lists it for untracked hygiene on fresh clones. |

## Untracked hotspots (explicit)

- **`backend/data/stores/effect_chains/*.json`** — treat as **local junk** unless a feature explicitly commits fixture chains; delete or exclude from PR.
- **`docs/reports/verification/generated/`** — **`engine_truth*.json` + `README.md` are tracked** (Tasks 227); **`stt_hardening_regress_summary.json`** is **local-only** (gitignored); see [generated/README.md](generated/README.md).
- **Slice proof trees** (`docs/reports/verification/slice27/`, `slice19/`, etc.) — **proof/governance**; commit when tied to a closed proof row, not ad hoc.

## Next commits (numbered stack — **Task 192**)

1. **Governance-only** — `.cursor/STATE.md` (ACTIVE WINDOW, **LATEST MILESTONE** order, **Next 3 Steps**, **LATEST PROOF INDEX** row **191–199**), this **DAY0** doc (Dispositions + stack), `docs/governance/CANONICAL_REGISTRY.md` (engine parity matrix summary row), stale **PROOF §27** table rows that still said **`whisper_cpp` pending** where **175–181** is authority; `docs/governance/roles/ROLE_0_OVERSEER_GUIDE.md`, `OVERSEER_NEWCOMER_HANDOFF.md` if touched. **Glob:** `.cursor/STATE.md`, `docs/reports/verification/DAY0_*.md`, `docs/governance/CANONICAL_REGISTRY.md`, `docs/reports/verification/PROOF_SLICE27*.md` (surgical). **No** `defaults.latest_verify_artifact` / STATE **Latest verify artifact** bump unless anchored **`verify.ps1`** (**Task 198**).
2. **Vertical product slices** — For each bounded lane: `app/core/engines/*` + `backend/services/*` + matching `tests/integration/*` + `tests/unit/*` + `engines/audio/<engine>/engine.manifest.json` in **one** reviewable commit per slice (e.g. OpenVoice subprocess, STT router, whisper_cpp integrity, chatterbox venv). **Glob:** `app/core/engines/**`, `backend/**`, `tests/**`, `engines/audio/**`, `config/engines.config.yaml`.
3. **Proof / generated artifacts only** — Large `docs/reports/verification/slice**` trees, `tools/overseer/data/*`, `generated/engine_truth*.json` **only** when tied to a **closed** proof row and optional `verify.ps1` anchor; never mix with **effect_chain** junk.

## Acceptance

- Every top-level **untracked** cluster is either **committed with intent**, **ignored**, or **deleted** before declaring merge-ready.
- No PR mixes **effect_chain JSON dumps** with product logic without an explicit reviewer note.
