# GOV — CI verify-harness first GHA signal (execution row)

**Status:** Open (bounded CI-only)  
**Opened:** 2026-04-14  
**Scope:** Single root cause — **hosted runner pip install SSL failure** before **`verify.ps1`** executes; **not** harness logic drift.

## Outcome bucket (frozen)

**`BucketC_InfraRed`** — workflow failed on **`windows-latest`** during **`pip install -e ".[dev,extras]"`** with transient SSL:

`pip._vendor.urllib3.exceptions.SSLError: [SSL: DECRYPTION_FAILED_OR_BAD_RECORD_MAC]`

**First failing stage:** `Verify Quick Gate` → **Install Python dependencies** (step 5).

## Immutable run evidence (observed)

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24379285704 |
| **Run ID** | `24379285704` |
| **Commit SHA** | `18abac073f4f324aea71125c92c4236883275a25` |
| **Trigger** | `push` (path-filtered workflow paths) — **not** `workflow_dispatch` |
| **Verify Quick Gate** | **failure** (pip install) |
| **Verify Checkpoint + Resume Chain** | **skipped** (by design: only `workflow_dispatch` + `run_full_chain` or `schedule`) |

**Note:** This run does **not** satisfy “first authoritative full-chain” certification; it only proves first GHA attempt on the workflow landed and failed in CI env before harness signal.

## Dispatch path (operator)

| Path | Status |
| --- | --- |
| **GitHub Actions UI** | **Primary** — **Actions → Verify Harness (Checkpoint + Resume) → Run workflow**, check **Run checkpoint+resume chain** (`run_full_chain: true`). |
| **`gh workflow run "Verify Harness (Checkpoint + Resume)" -f run_full_chain=true`** | **Blocked here** with **HTTP 403** (`Resource not accessible by personal access token`). Use a token allowed to **dispatch workflows** (fine-grained: **Actions: Read and write** on repo; or classic PAT with **`workflow`** + **`repo`**, subject to org policy). |

## Remediation scope (one slice)

1. **Re-run** the workflow (prefer **`workflow_dispatch`** with **`run_full_chain: true`**) after transient SSL clears, **or** add a **single** bounded retry on pip install in `.github/workflows/verify-harness.yml` if flakes persist (document rationale; no blind loops).
2. **Do not** change **`scripts/verify.ps1`** lineage or checkpoint semantics under this row — failure occurred **before** Quick.

### Applied (2026-04-14)

- **Pip resilience:** both **Install Python dependencies** steps in [`.github/workflows/verify-harness.yml`](../../.github/workflows/verify-harness.yml) now use explicit **`--retries 5`** and **`--timeout 60`** on **`pip`** / **`python -m pip`** (see [closure report](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md) § Workflow hardening).
- **Next:** operator **`workflow_dispatch`** on **`main`** with **`run_full_chain: true`**; if still red, freeze **first failing step only** and update this row (do not smear root causes).

## Rerun command (after token/UI access)

```powershell
gh workflow run "Verify Harness (Checkpoint + Resume)" --repo wtsteward11/VoiceStudio -f run_full_chain=true
# If 403: use Actions UI per table above.
```

## Closure

Close this row when:

- A **`workflow_dispatch`** run with **`run_full_chain: true`** completes, **and**
- Outcome bucket + job conclusions are recorded in [VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md), **and**
- If **`BucketA_Green`**: operational certification may be claimed; if **`BucketB_Partial`** / **`BucketC_InfraRed`**: open a **new** single-row slice (do not smear root causes).

**Related:** [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md) §8 · [verify-harness.yml](../../.github/workflows/verify-harness.yml)
