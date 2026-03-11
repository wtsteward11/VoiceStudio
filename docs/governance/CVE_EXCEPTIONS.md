# CVE Exceptions Registry

**Owner:** Build & Tooling Engineer (Role 2)
**Created:** 2026-02-21 (Phase 12 WS1)
**Last Updated:** 2026-03-11

## Purpose

This document tracks known CVEs in VoiceStudio dependencies that have been assessed and accepted with documented rationale. Each exception must include the CVE ID, affected package, risk assessment, and mitigation strategy.

## Exception Criteria

A CVE may be excepted only when:

1. The vulnerability is not exploitable in VoiceStudio's usage context (e.g., server-side only CVE in a local-only app)
2. The fix introduces breaking changes that cannot be absorbed without major rework
3. The vulnerability is in a transitive dependency with no direct exposure path
4. A mitigation is in place that neutralizes the attack vector

## Current Exceptions

| CVE | Package | Severity | Rationale | Mitigation | Review Date |
|-----|---------|----------|-----------|------------|-------------|
| (none) | — | — | All known CVEs resolved in Phase 12 WS1 | — | 2026-02-21 |

## Resolved CVEs (Phase 12 WS1)

The following CVEs were remediated during Phase 12 Strategic Hardening:

| CVE | Package | Resolution |
|-----|---------|------------|
| Various | filelock | Upgraded to patched version |
| Various | pillow | Upgraded to patched version |
| Various | python-multipart | Upgraded to patched version |
| Various | keras | Upgraded to patched version |
| Various | transformers | Upgraded to patched version |

## Process

1. Run `pip-audit` against the active virtual environment
2. Triage each finding against the Exception Criteria above
3. Remediate (upgrade) where possible
4. Document exceptions here with full rationale
5. Review exceptions quarterly or on each major release

## Bandit B614 — torch.load Exemption (2026-03-11)

**Finding:** Bandit B614 flags `torch.load()` as unsafe (arbitrary code execution via pickle deserialization).

**Scope:** ML/engine code in `app/core/engines/`, `app/core/training/`, `backend/voice/rvc/` loads PyTorch checkpoints. Many engine checkpoints use custom classes or legacy formats that require `weights_only=False`.

**Decision:** Exemption documented. CI uses `bandit --skip B614` (see `.github/workflows/ci.yml`).

**Rationale:**
- Checkpoints are loaded from local paths only (user-selected or manifest-configured); no untrusted input.
- `weights_only=True` is used where feasible (e.g., `backend/voice/rvc/engine.py` uses `weights_only=False` only when required).
- Migration to `safetensors` or `weights_only=True` is tracked per-engine; not a v1.1.0 gate.

**Mitigation:** Prefer `safetensors.torch.load_file()` for new code. Use `weights_only=True` when checkpoint format allows. Document any `weights_only=False` with inline comment.

**Review:** Quarterly or on major release. See DEFERRED_V1_2.md.

## Related

- ADR-044: Supply-Chain Integrity
- ADR-041: Python 3.11 Runtime
- VS-0046: pip-audit CVE remediation (DONE)
- DEFERRED_V1_2.md: Bandit B614 formalization (complete)
