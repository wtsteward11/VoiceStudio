# [Report Title] — [YYYY-MM-DD]

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: [REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN]
proof_type: [voice_synthesis | generated_audio | other]
engine_mode_source: [runtime_probe | test_mode_env | mock_fixture | blocked_unknown | manual_unknown | not_applicable]
runtime_claim: false
operator_claim: false
-->

<!--
CLASSIFICATION GUIDE
====================
REAL_ENGINE   — Real ML engine performed synthesis (e.g. XTTS v2, Piper, eSpeak NG).
                Requires: routed_engine evidence, artifact size/format, library and timeline evidence.
STUB_ENGINE   — Stub/test-mode engine (VOICESTUDIO_TEST_MODE=1). Proves orchestration only.
                Must NOT claim real synthesis. Must include Non-Claims section.
MOCK_ENGINE   — Mock engine in unit/integration tests. Proves call paths only.
                Must NOT claim real synthesis. Must include Non-Claims section.
UNKNOWN       — Engine mode could not be determined (blocker condition).
                Must include blocker explanation and Non-Claims section.
-->

**Classification:** [REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN]
**Date:** [YYYY-MM-DD]
**HEAD:** [git rev-parse HEAD]
**Purpose:** [One sentence describing what this proof establishes]

---

## 1. Repo Reality

| Field | Value |
|---|---|
| HEAD commit | `[hash]` |
| Branch | `[branch]` |
| `origin/main` | `[hash]` |
| Ahead/behind | `[N ahead, M behind]` |
| Dirty files | [none / list] |

---

## 2. Engine Mode Classification

**VERDICT:** [REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN]

| Evidence Item | Value |
|---|---|
| `VOICESTUDIO_TEST_MODE` | [empty — not set / set to value] |
| Stub gate result | [Not triggered / triggered] |
| `routed_engine` in synthesis response | `[xtts_v2 / piper / stub / mock]` |
| Engine readiness probe | [PASS / FAIL / not run] |

<!-- For STUB_ENGINE / MOCK_ENGINE: remove or mark N/A the rows that don't apply -->
<!-- For UNKNOWN: add a dedicated Blocker section below -->

---

<!-- === REAL_ENGINE SECTIONS (remove for STUB/MOCK/UNKNOWN) === -->

## 3. Environment and Test-Mode Evidence

| Check | Result |
|---|---|
| `VOICESTUDIO_TEST_MODE` | _(empty — not set)_ |
| `_is_voice_studio_stub_test_mode()` | `False` |
| Engine probe mode | `manifest_scan_plus_full_router` |
| Probe PASS | YES |

---

## 4. Synthesis Request and Response

**Request:**
```json
{
  "text": "[synthesis text]",
  "profile_id": "[profile uuid]",
  "engine": "[engine_name]"
}
```

**Response (HTTP 200):**
```json
{
  "audio_id": "[audio_id]",
  "duration": [seconds],
  "quality_score": [0.0-1.0],
  "routed_engine": "[engine_name]",
  "quality_metrics": {
    "mos_score": [1.0-5.0],
    "snr_db": [value]
  }
}
```

---

## 5. Audio Artifact Validation

| Check | Result |
|---|---|
| File size | **[N bytes (N KiB)]** |
| RIFF header (bytes 0–3) | `52 49 46 46` = `"RIFF"` ✓ |
| WAVE marker (bytes 8–11) | `"WAVE"` ✓ |
| Content-Type | `audio/wav` |

---

## 6. Library Evidence

| Field | Value |
|---|---|
| Library asset id | `[uuid]` |
| `audio_id` (upload_id) | `[uuid]` |
| Upload HTTP status | **201 Created** |

---

## 7. Timeline Evidence

| Step | Result |
|---|---|
| Track created | `[track_uuid]` |
| Clip added | `[clip_uuid]` |
| Timeline revision after clip | **[N]** |
| Clip start/end | `[start]s – [end]s` |

---

## 8. Durability Evidence (or Non-Claim)

<!-- Either add durability checks, or explicitly state this is out of scope: -->

[Durability not tested in this proof. See Explicit Non-Claims section.]

---

<!-- === END REAL_ENGINE SECTIONS === -->

<!-- === STUB/MOCK ENGINE SECTIONS === -->

## 3. Orchestration Evidence

| Check | Result |
|---|---|
| `VOICESTUDIO_TEST_MODE` | `1` (stub mode) |
| Synthesis API call | HTTP 200 (stub response) |
| `routed_engine` | `stub` |

[Stub/mock engine was active. No real audio was generated.]

---

<!-- === END STUB/MOCK ENGINE SECTIONS === -->

<!-- === UNKNOWN BLOCKER SECTION === -->

## 3. Blocker Evidence

**Engine mode could not be determined.**

| Blocker | Description |
|---|---|
| Reason | [could not determine / backend unreachable / unavailable] |
| Attempted checks | [list what was tried] |
| Resolution | [what is needed to unblock] |

---

<!-- === END UNKNOWN BLOCKER SECTION === -->

## 9. Verification Commands

```powershell
# Validator (run before commit)
python scripts/ci/check_voice_synthesis_proof_boundary.py --changed-from origin/main

# Gate status
python scripts/run_verification.py

# Quick verification
.\scripts\verify.ps1 -Quick
```

**Validator output:**
```
[voice_synthesis_proof_boundary] Checked 1 report(s) ...
[voice_synthesis_proof_boundary] All 1 report(s) PASS
```

---

## 10. Explicit Non-Claims

- [Describe what this report does NOT prove]
- This is NOT a runtime FULL PASS
- This is NOT an operator/human proof
- [NOT GAP-008 / NOT Slice 46 / NOT MainWindow*ShellBridge — if applicable]
- [NOT RHVoice / NOT ENGINE_PARITY_MATRIX — if applicable]

---

## 11. Final Verdict

**VERDICT: [REAL_ENGINE | STUB_ENGINE | MOCK_ENGINE | UNKNOWN]**

[One or two sentences summarising what this proof establishes and its limitations.]
