# Risk Register

**Last Updated:** 2026-02-18  
**Owner:** Overseer (Role 0)

This document tracks technical, operational, and organizational risks for VoiceStudio.

---

## Technical Risks

| ID | Risk | Likelihood | Impact | Mitigation | Status |
|----|------|------------|--------|------------|--------|
| RISK-001 | Chatterbox torch version incompatibility (requires torch>=2.6, venv has 2.2.2+cu121) | High | Medium | Document limitation; use separate venv when compatibility resolved | Deferred |
| RISK-002 | XAML compiler instability (VS-0035: exits code 1 with no output) | Medium | High | XAML wrapper with retry logic; ThemeResource migration; monitor Windows App SDK updates | Mitigating |
| RISK-003 | GPU VRAM contention with concurrent engine runs | Medium | Medium | VRAM scheduler implemented (TD-013 CLOSED 2026-02-02, tests 11/11 PASS). Per-engine budgets, eviction policy, priority preemption in place. | Controlled |
| RISK-004 | Python dependency conflicts across engines | High | Medium | Venv families strategy implemented (TD-015 CLOSED 2026-02-02, tests 14/14 PASS). 3 families, 10 engine manifests, per-family requirements. | Controlled |
| RISK-005 | Windows App SDK version drift | Low | Medium | Pin SDK version in Directory.Build.props; test upgrades in branch first | Controlled |
| RISK-006 | Circuit breaker false positives blocking healthy engines | Low | Low | Configurable thresholds; manual reset endpoint; monitoring | Controlled |
| RISK-014 | Plugin signature verification bypass | Medium | High | Integrated signer.py verification (GAP-PY-007); fallback checks signature file presence | Mitigating |
| RISK-015 | Deprecated plugin API usage | Medium | Low | Deprecation warnings in schema validator (GAP-ARCH-002); migration guide in ADR-038 | Controlled |
| RISK-016 | Phase 6 plugin modules not implemented | Low | Low | pytest.skip markers on Phase 6 tests; tracked in master plan | Controlled |

---

## Operational Risks

| ID | Risk | Likelihood | Impact | Mitigation | Status |
|----|------|------------|--------|------------|--------|
| RISK-007 | Model files missing or corrupted at runtime | Medium | High | Preflight checks; auto-download for HF-backed models; health endpoint | Controlled |
| RISK-008 | Backend process crash during long synthesis | Low | Medium | Job state persistence (VS-0021); restart recovery; crash artifact capture | Controlled |
| RISK-009 | Installer fails on clean Windows install | Low | High | Gate H lifecycle tests on clean VMs; WinAppSDK runtime installer bundling | Controlled |
| RISK-010 | FFmpeg not available on user system | Medium | Medium | Deterministic ffmpeg discovery (VS-0022); bundled fallback; clear error message | Controlled |
| RISK-017 | Silent exception swallowing hiding errors | Low | Medium | GAP-PY-001 remediation added logging to 50+ handlers; audit complete | Controlled |
| RISK-018 | Hardcoded paths failing on non-standard Windows installs | Low | Medium | Centralized path_config.py with environment variable overrides (GAP-PY-002) | Controlled |

---

## Organizational Risks

| ID | Risk | Likelihood | Impact | Mitigation | Status |
|----|------|------------|--------|------------|--------|
| RISK-011 | Documentation drift from implementation | Medium | Low | CANONICAL_REGISTRY enforcement; doc updates in Definition of Done | Controlled |
| RISK-012 | Tech debt accumulation blocking features | Medium | Medium | TECH_DEBT_REGISTER tracking; periodic cleanup sprints | Controlled |
| RISK-013 | Knowledge loss during handoffs | Low | Medium | PROJECT_HANDOFF_GUIDE; role-specific onboarding packets | Controlled |

---

## Risk Response Definitions

- **Open**: Risk identified, no mitigation in place
- **Mitigating**: Active work to reduce risk
- **Controlled**: Mitigation in place, risk acceptable
- **Deferred**: Accepted risk, will address in future
- **Closed**: Risk eliminated or no longer applicable

---

## Review Schedule

This register should be reviewed:
- Before each release (Gate H checkpoint)
- When new blockers are identified
- During Phase 4 QA completion

---

## References

- Quality Ledger: `docs/archive/Recovery_Plan/QUALITY_LEDGER.md`
- Tech Debt Register: `docs/governance/TECH_DEBT_REGISTER.md`
- Production Build Plan: `docs/governance/VoiceStudio_Production_Build_Plan.md`
