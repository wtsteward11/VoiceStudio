# Update Scenario Tests (Item 29)

Update mechanism hardening scenarios. Execute manually or via `tests/installer/test_update_scenarios.ps1`.

## Scenarios

| # | Scenario | Description | Pass criteria |
|---|----------|-------------|---------------|
| 1 | Partial download | Truncated installer file (e.g. copy only first 50% of installer) | Installer should not proceed; report integrity/download error |
| 2 | Locked files | Hold a DLL handle (e.g. app running) during update | Update retries or reports "file in use" and suggests closing app |
| 3 | Rollback | Install v1, update to v2, then rollback to v1 | App runs correctly on v1 after rollback |

## Execution

- **Simulated**: Run `tests/installer/test_update_scenarios.ps1` to regenerate this doc.
- **Manual**: Perform each scenario with real installer and record results below.

## Results (fill after manual run)

| Scenario | Date | Result | Notes |
|----------|------|--------|-------|
| 1 | | | |
| 2 | | | |
| 3 | | | |
