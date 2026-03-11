# CI Suppression Policy

> **Version**: 1.0  
> **Last Updated**: 2026-03-11  
> **Classification**: Developer Guide

---

## Purpose

This document defines when CI steps may use non-blocking patterns (e.g. `|| echo "::warning::..."`) instead of failing the workflow. It complements the [no-suppression rule](.cursor/rules/quality/no-suppression.mdc), which prohibits error suppression **in source code**. CI workflows are a separate concern: we allow some steps to warn instead of fail when the failure is advisory or out-of-scope for the gate.

---

## Allowed Patterns

### 1. Advisory / Non-Gate Linters

Steps that run **advisory** quality checks may use `|| echo "::warning::..."` when:

- The check is **not** a merge gate (e.g. verify.ps1, pytest core, dotnet build).
- The finding is informational (formatting, import order, type hints, security scan).
- A documented plan exists to address findings incrementally (e.g. mypy strict burn-down, Bandit B614 exemption).

**Examples (allowed):**

```yaml
- run: black --check backend app tests || echo "::warning::Black formatting issues found (non-blocking)"
- run: ruff check backend app tests || echo "::warning::Ruff lint issues found (non-blocking)"
- run: mypy backend app --ignore-missing-imports || echo "::warning::Mypy type issues found (non-blocking)"
- run: bandit -r backend app ... --skip B614 || echo "::warning::Bandit found issues (non-blocking)"
```

**Rationale:** These tools surface quality debt. Blocking merge on them would halt development. Warnings keep visibility without blocking.

### 2. Optional / Conditional Steps

Steps that depend on optional configuration or environment may use `|| echo` when:

- The step is optional (e.g. MkDocs not configured, requirements-dev.txt missing).
- The failure does not indicate a broken build.

**Example:**

```yaml
- run: mkdocs build --strict || echo "MkDocs not configured"
```

### 3. Continue-on-Error Workarounds (Temporary)

Steps marked `continue-on-error: true` or using `|| echo` for **known infrastructure limitations** are allowed when:

- The limitation is documented (e.g. Sentinel smoke backend startup > 2min).
- A tracking issue or DEFERRED item exists.
- The step is not security-critical.

**Example:** Sentinel smoke backend cold-start in CI can exceed 2min; `continue-on-error` is used until startup time is addressed (see DEFERRED_V1_2.md).

---

## Prohibited Patterns

### 1. Gate Steps Must Not Suppress

The following **must never** use `|| echo` or `continue-on-error`:

- `dotnet build` (C# build)
- `pytest` for core test suites (tests/ci/, tests/unit/ for gate tests)
- `.\scripts\verify.ps1` or `python scripts/run_verification.py`
- Any step that gates merge per [verification-harness.mdc](.cursor/rules/workflows/verification-harness.mdc)

### 2. Security-Critical Steps

Steps that validate security (e.g. `safety check`, dependency audit) should **fail** the workflow when vulnerabilities are found, unless:

- A CVE exception is documented in [CVE_EXCEPTIONS.md](../governance/CVE_EXCEPTIONS.md).
- The step is explicitly advisory (e.g. `safety check --full-report || echo` with documented rationale).

### 3. No Silent Suppression

Never use patterns that hide failures without visibility:

```yaml
# ❌ PROHIBITED
- run: some_command || true
- run: some_command 2>/dev/null
```

Always emit a warning or log when suppressing:

```yaml
# ✅ ALLOWED
- run: some_command || echo "::warning::Reason for non-blocking"
```

---

## Inventory (Current Non-Blocking Steps)

| Workflow | Step | Rationale |
|----------|------|------------|
| ci.yml | black, isort, ruff, mypy, safety, bandit | Advisory quality; not merge gates |
| build.yml | Quality scorecard below threshold | Advisory; scorecard not gate |
| test.yml | Coverage below 95%, hardcoded colors | Advisory |
| release.yml | mkdocs build | Optional; MkDocs may not be configured |
| sentinel_backend_smoke.yml | ruff, mypy | Advisory for sentinel scripts |
| sentinel_ui_smoke_nightly.yml | ruff, mypy | Advisory for page objects |

---

## Process

1. **Before adding** a new `|| echo` or `continue-on-error` step, confirm it fits an Allowed Pattern above.
2. **Document** the rationale in this policy (Inventory section) or in a linked DEFERRED/CVE exception.
3. **Review** quarterly: convert advisory steps to blocking when debt is resolved.

---

## Related

- [no-suppression.mdc](.cursor/rules/quality/no-suppression.mdc) — Code-level suppression prohibited
- [CVE_EXCEPTIONS.md](../governance/CVE_EXCEPTIONS.md) — CVE and Bandit B614 exemptions
- [DEFERRED_V1_2.md](../governance/DEFERRED_V1_2.md) — Sentinel smoke, OpenAPI drift, etc.
- [verification-harness.mdc](.cursor/rules/workflows/verification-harness.mdc) — Gate steps must not be suppressed
