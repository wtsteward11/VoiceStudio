# Overseer — Daily gate discipline

**Companion:** [OVERSEER_NEWCOMER_HANDOFF.md](OVERSEER_NEWCOMER_HANDOFF.md) (onboarding) · **Session oracle:** [.cursor/STATE.md](../../../.cursor/STATE.md) (ACTIVE WINDOW)

Use this checklist **each working day** before approving lane closure or merge-ready claims. Goal: reproducible proof, zero drift between closure docs, STATE, and registry.

## 1. Read operational truth

1. Open `.cursor/STATE.md` — section **ACTIVE WINDOW** only (through **HISTORY LEDGER** divider).
2. Note **Active Task**, **Next 3 Steps**, **Current Blocker**, **Current Target**.
3. Skim **Truth Sync Note** for the newest lane narrative.

## 2. Run gates (commands)

| Step | Command | Evidence |
|------|---------|----------|
| A | `.\scripts\verify.ps1 -Quick` | Latest folder under `artifacts/verify/<timestamp>/` and `verification_report.md` |
| B | `python scripts/run_verification.py` | `.buildlogs/verification/last_run.json` → `timestamp_short`, `all_passed`, `completion_guard` |
| C | (When lanes touched UI/XAML) `python scripts/validate_xaml_resources.py` | Zero missing `VSQ.*` references |

Record the **Quick** folder name and **`timestamp_short`** in STATE **Last Verified Commands** when you are closing a lane in the same change set.

## 3. Risk and backlog skim

1. `docs/archive/Recovery_Plan/QUALITY_LEDGER.md` — open **HIGH** / blocking lines.
2. `docs/design/PROFESSIONAL_GAP_TRACKER.md` — confirm **Open** hero-path rows match STATE **Next 3 Steps**.

## 4. Watchpoints (proof drift)

| Watchpoint | Action if red |
|------------|----------------|
| Closure report vs STATE **LATEST PROOF INDEX** vs `last_run.json` disagree | Single synchronized doc pass; rerun `run_verification.py` |
| Registry “newest rolling proof” stale vs disk | Update `CANONICAL_REGISTRY.md` Session State row |
| `completion_guard` FAIL after doc-only edits | Commit or remove completion markers per guard output |
| `artifacts/verify/latest_pointer.json` lags actual Quick run | Treat **STATE** + latest `artifacts/verify/<ts>/` as authoritative |
| Post-closure chronology edits without a new `run_verification.py` | **Stop** — timestamps drift; rerun gates and sync STATE + registry + tracker in one pass |

## 5. Governance chronology freeze (discipline)

After a lane closure change set that synchronized **STATE.md**, **CANONICAL_REGISTRY.md**, and **PROFESSIONAL_GAP_TRACKER.md** to `.buildlogs/verification/last_run.json`, treat those chronology lines as **frozen** until the next authoritative verification run produces a new `timestamp_short`. “Helpful” narrative-only edits are high-risk for proof drift.

## 6. Optional full matrix (pre-merge / major lanes)

- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`

---

**Changelog**

- **2026-04-04** — §5 governance chronology freeze + watchpoint (post GAP-044 final hygiene).
- **2026-04-04** — Initial checklist (Overseer transition plan Phase 6).
