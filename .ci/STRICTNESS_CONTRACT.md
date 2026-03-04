# Workflow Strictness Contract

Single source of truth for CI workflow gate vs advisory classification.

## Gate Jobs

These jobs **must never** use `continue-on-error: true` or `|| true` / `|| echo "::warning::"` on core steps. Suppressions are forbidden unless explicitly allowlisted in `tests/ci/test_ci_suppression_guard.py`.

| Workflow | Job ID |
|----------|--------|
| ci.yml | python-tests |
| ci.yml | dotnet-build |
| ci.yml | integration-tests |
| ci.yml | golden-path |
| ci.yml | security-scan |
| build.yml | build-frontend |
| build.yml | build-backend |
| build.yml | verify-gates |
| build.yml | validate-contracts |
| test.yml | test-backend |
| test.yml | test-frontend |
| test.yml | verify-gates |

## Advisory Jobs

These jobs may use suppressions when labeled in the suppression guard allowlist:

- code-quality (formatting, linting)
- security-scan (vuln scans, static analysis)
- performance-tests
- quality-scorecard
- nightly-ui-automation
- regression-detection

## Canonical Release Pipeline

**ci.yml** is the canonical release pipeline. build.yml and test.yml are supplementary.
