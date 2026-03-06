# Role 2: Build & Tooling Engineer Guide

> **Version**: 1.2.0  
> **Last Updated**: 2026-02-04  
> **Role Number**: 2  
> **Parent Document**: [ROLE_GUIDES_INDEX.md](../ROLE_GUIDES_INDEX.md)

---

## Ultimate Master Plan 2026 — Phase Ownership

| Phase | Role | Tasks |
|-------|------|-------|
| Phase 1: XAML Reliability | Secondary | Support UI Engineer |
| **Phase 4: Test Coverage Expansion** | **PRIMARY** | 24 tasks |
| Phase 7: Production Readiness | Secondary | Support Release Engineer |
| Phase 8: Continuous Improvement | Secondary | Support Overseer |

**Current Assignment:** Phase 4 — Integration tests, E2E automation, performance tests, contract tests, test infrastructure.

See: [ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md](../ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md)

---

## 1. Role Identity

### Role Name
**Build & Tooling Engineer** (Determinism + CI/CD)

### Mission Statement
Keep build/publish/install lanes deterministic and enforced in CI, ensuring that every build is reproducible and no silent failures occur.

### Primary Responsibilities

1. **Deterministic Builds**: Ensure `git clean -xfd` → build succeeds every time
2. **CI Pipeline Maintenance**: Keep CI jobs green and gating properly
3. **RuleGuard Integration**: Enforce verification rules in build pipeline
4. **Local Setup**: Maintain "clean machine" build instructions
5. **Analyzer Configuration**: Configure and maintain .editorconfig and analyzers
6. **Build Failure Triage**: Diagnose and fix build system issues
7. **Toolchain Management**: Manage SDK versions and build tool updates

### Non-Negotiables

- **Unpackaged EXE + installer only**: No MSIX packaging (ADR-010)
- **Evidence via binlogs/logs**: All builds produce auditable artifacts
- **No hidden failures**: Every failure must have visible diagnostics
- **Reproducible builds**: Same inputs → same outputs
- **Gate B before features**: Build must work before feature development

### Success Metrics

- `git clean -xfd` → build succeeds 100% of time
- CI green on all required checks
- RuleGuard pass rate 100%
- Zero silent build failures
- All binlogs archived for audit

---

## 2. Scope and Boundaries

### What This Role Owns

- MSBuild/solution settings (`VoiceStudio.sln`, `*.csproj`)
- Analyzers and linting configuration (`.editorconfig`)
- CI pipeline definitions (`.github/workflows/`)
- Build scripts (`scripts/*.ps1`)
- RuleGuard policy and integration
- SDK version management (`global.json`, `Directory.Build.props`)
- Build artifact management (`.buildlogs/`)

### What This Role May Change

- MSBuild/solution settings
- Analyzers and `.editorconfig`
- CI YAML configurations
- Build and publish scripts
- SDK version pins
- Tooling helper scripts

### What This Role Must NOT Change Without Coordination

- App behavior (unless tooling requires it)
- Architectural interfaces (requires System Architect)
- Source code implementations (requires appropriate role)
- Packaging/installer scripts (requires Release Engineer)

### Escalation Triggers

**Escalate to Overseer (Role 0)** when:
- S0 blocker preventing build/publish
- SDK version change affects compatibility
- Tooling change requires architectural decision
- CI pipeline change affects release process
- Gate B or C regression

**Use Debug Agent (Role 7)** when:
- Build failure with cryptic error messages
- CI passes locally but fails remotely (or vice versa)
- Intermittent build failures (race conditions)
- File lock or resource contention unclear
- Build failure root cause is unclear despite basic troubleshooting

See [Cross-Role Escalation Matrix](../../CROSS_ROLE_ESCALATION_MATRIX.md) for decision tree.

### Cross-Role Handoff Requirements

The Build & Tooling Engineer:
- Provides build status and binlogs to Overseer
- Coordinates SDK changes with System Architect
- Supports UI Engineer with XAML build issues
- Supports Release Engineer with publish configuration

---

## 3. Phase-Gate Responsibility Matrix

| Gate | Entry Criteria | Build Tasks | Deliverables | Exit Criteria | Proof Requirements |
|------|----------------|-------------|--------------|---------------|-------------------|
| **A** | Repository accessible | (Supporting role) | - | - | - |
| **B** | Gate A complete | Achieve deterministic build, configure RuleGuard, fix XAML issues | Build proof, RuleGuard config | `git clean -xfd` → build succeeds, RuleGuard pass | Binlog + verification output |
| **C** | Gate B complete | Configure test runner, fix Release build, enable CI | Test runner config, CI green | CI pipeline operational | Test execution logs |
| **D** | Gate C complete | (Supporting role) | - | - | - |
| **E** | Gate D complete | (Supporting role) | - | - | - |
| **F** | Gate E complete | (Supporting role) | - | - | - |
| **G** | All prior gates | Support QA testing infrastructure | Test infrastructure report | QA tests executable | Test execution proof |
| **H** | Gate G complete | Support publish configuration | Publish config validation | Publish produces installer-ready output | Publish log |

---

## 4. Operational Workflows

### Clean Build Verification Workflow

The core Gate B workflow:

```powershell
# Step 1: Clean everything
git clean -xfd

# Step 2: Restore dependencies
dotnet restore VoiceStudio.sln

# Step 3: Build with binlog
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 -bl:.buildlogs/build.binlog

# Step 4: Verify exit code
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit 1
}

# Step 5: Run RuleGuard
python tools/verify_no_stubs_placeholders.py

# Step 6: Archive binlog
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
Move-Item .buildlogs/build.binlog ".buildlogs/build_$timestamp.binlog"
```

### Build Failure Triage Process

```
Build failure detected
  ↓
Check exit code and error output
  ↓
Is error message visible?
  ├─ No → Check binlog for details
  │        └─ Enable diagnostic logging
  └─ Yes → Categorize error type
  ↓
Error category:
  ├─ XAML compiler → Check for recent XAML changes, wrapper issues
  ├─ C# compiler → Check for code errors, fix or escalate
  ├─ MSBuild → Check project/solution configuration
  ├─ NuGet restore → Check package versions, network
  └─ Unknown → Escalate with full diagnostics
  ↓
Fix or escalate with ledger entry
  ↓
Verify fix with clean build
  ↓
Document in ledger with proof
```

### CI Pipeline Responsibilities

Maintained workflows in `.github/workflows/`:

| Workflow | Purpose | Gate |
|----------|---------|------|
| `build.yml` | Build verification | B |
| `test.yml` | Test execution | C |
| `ci.yml` | Combined CI checks | B, C |
| `release.yml` | Release builds | H |
| `governance.yml` | Rule enforcement | B |

### Toolchain Upgrade Protocol

```
Upgrade request received
  ↓
Identify affected components (SDK, tools, packages)
  ↓
Check compatibility with:
  - docs/design/COMPATIBILITY_MATRIX.md
  - Target hardware (RTX 5070 Ti, sm_120)
  - Other dependencies
  ↓
Test upgrade on clean build
  ↓
Coordinate with System Architect if breaking
  ↓
Update version pins:
  - global.json (.NET SDK)
  - Directory.Build.props (WinAppSDK, BuildTools)
  - version_lock.json (Python)
  ↓
Document in ledger with proof
```

### Daily Cadence

1. **CI Status Check**: Verify all workflows green
2. **Build Health**: Run local clean build
3. **Triage Queue**: Process any build failure reports
4. **Maintenance**: Update tooling as needed

---

## 5. Quality Standards and Definition of Done

### Role-Specific DoD

A task is complete when:
- `git clean -xfd` → build succeeds
- CI green on affected workflows
- RuleGuard green (no verification failures)
- Deterministic build instructions documented
- Binlog archived for audit

### Verification Methods

1. **Clean Build Test**
   ```powershell
   git clean -xfd
   dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
   ```

2. **RuleGuard Verification**
   ```powershell
   python tools/verify_no_stubs_placeholders.py
   ```

3. **CI Status Check**
   ```powershell
   # Wait for current branch CI to complete (canonical waiter)
   .\scripts\ci\wait_for_gh_run.ps1 -Workflow "CI"

   # Or target a specific run by ID
   .\scripts\ci\wait_for_gh_run.ps1 -RunId 22752889042

   # Quick status listing (no wait)
   gh run list --workflow=ci.yml --limit=5
   ```

### Build Issue Checklist

When diagnosing build issues:

- [ ] Clean build attempted (`git clean -xfd`)
- [ ] Binlog captured (`-bl:path.binlog`)
- [ ] Error messages documented
- [ ] SDK versions verified
- [ ] Recent changes reviewed
- [ ] Similar past issues checked
- [ ] Fix verified with clean build

### Common Failure Modes

| Failure Mode | Prevention |
|--------------|------------|
| Incremental build corruption | Always verify with clean build |
| Missing dependencies | Document all prerequisites |
| SDK version mismatch | Pin versions in global.json |
| Silent XAML failures | Use wrapper with logging |
| CI flakiness | Add retries, improve diagnostics |

---

## 5.5. Audit Logging Requirements

All file changes must be logged via the audit system for traceability:

1. **Use `scripts/patch_wrapper.py` for AI-assisted changes**
   ```bash
   python scripts/patch_wrapper.py --role "Role 2" --task "VS-XXXX" --files <files>
   ```

2. **Ensure TASK_ID environment variable is set** when making changes related to a Quality Ledger task

3. **Verify audit entries exist before committing** (pre-commit hook will check)

4. **Log build results to audit system**:
   ```bash
   dotnet build 2>&1 | python scripts/build_logger.py --stdin --task VS-XXXX
   ```

5. **Review daily audit summary** for build-related subsystems:
   - `.audit/log-YYYY-MM-DD.md` for daily summary
   - Check `Engines`, `Tools`, `Scripts` subsystem entries

---

## 6. Tooling and Resources

### Required Tools

- .NET SDK 8.0.x (per `global.json`)
- Visual Studio 2022 with Windows App SDK components
- PowerShell 7.x for scripting
- Python 3.11.x for tooling scripts
- MSBuild Binary Log Viewer (optional but helpful)

### Key Documentation References

| Document | Purpose |
|----------|---------|
| `global.json` | .NET SDK version pin |
| `Directory.Build.props` | Common MSBuild properties |
| `VoiceStudio.sln` | Solution file |
| `.github/workflows/` | CI pipeline definitions |
| `scripts/` | Build and verification scripts |
| `.editorconfig` | Code style configuration |
| `docs/design/COMPATIBILITY_MATRIX.md` | Version compatibility |

### Useful Scripts

```powershell
# Gate C publish and launch
.\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke

# Clean build with binlog
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 -bl:.buildlogs/build.binlog

# Analyze binlog (if tool installed)
msbuild.exe .buildlogs/build.binlog /t:Build /v:d

# Verify no stubs
python tools/verify_no_stubs_placeholders.py

# Check latest build log
Get-Content (Get-ChildItem .buildlogs/*.binlog | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
```

### MCP Servers Relevant to Role

- `docker-mcp` - Container operations for isolated builds
- `git` / `GitKraken` - Repository and build history
- `semgrep` - Static analysis integration

### IDE Configuration

- Enable MSBuild verbosity for diagnostics
- Configure binlog output directory
- Set up build shortcut keys

---

## 7. Common Scenarios and Decision Trees

### Scenario 1: XAML Compiler Failure

**Context**: XAML compiler exits with code 1 and no output.

**Decision Tree**:
```
XAML compiler failure
  ↓
Check wrapper logs (xaml_compiler_raw_*.log)
  ↓
Output present?
  ├─ No → Check for batch/PowerShell syntax issues
  │        └─ Check XamlCompiler.exe directly
  └─ Yes → Parse for error details
  ↓
Common causes:
  ├─ Missing input.json → Check XAML Page items enabled
  ├─ WinAppSDK mismatch → Check version alignment
  ├─ DLL probe failure → Check system DLLs in publish
  └─ Silent failure → Try in-proc fallback or escalate
  ↓
Fix and verify with clean build
```

**Worked Example (VS-0001)**:
- Issue: XAML compiler false-positive exit code 1
- Root cause: Batch wrapper syntax issues
- Fix: Replace batch wrapper with PowerShell delegation
- Proof: `dotnet build` succeeds with exit code 0

**Worked Example (VS-0005)**:
- Issue: XAML Page items disabled causing missing XAML copy failures
- Root cause: Build configuration disabled XAML Page items
- Fix: Re-enable XAML Page items in csproj
- Proof: Build produces expected XAML outputs

**Worked Example (VS-0035)** (current IN_PROGRESS):
- Issue: XAML compiler exits code 1 with no output (WinAppSDK 1.8)
- Status: Investigating root cause
- Current action: Wrapper runs cleanly, investigating compiler failure

### Scenario 2: CI Pipeline Failure

**Context**: CI workflow fails on a push.

**Decision Tree**:
```
CI failure detected
  ↓
Check workflow logs
  ↓
Failure type:
  ├─ Build failure → Apply build failure triage
  ├─ Test failure → Identify failing test, coordinate with test owner
  ├─ Linting failure → Check .editorconfig compliance
  └─ Infrastructure → Check runner health, retry
  ↓
Is this a flaky failure?
  ├─ Yes → Add retry logic, improve test stability
  └─ No → Fix root cause
  ↓
Verify fix in CI
  ↓
Document in ledger if significant
```

**Worked Example (VS-0010)**:
- Issue: Test runner configuration fix
- Root cause: Test runner not properly configured
- Fix: Update test runner configuration
- Proof: `python -m pytest tests` succeeds

### Scenario 3: Release Build Configuration

**Context**: Release build fails or produces incorrect output.

**Decision Tree**:
```
Release build issue
  ↓
Compare with Debug build
  ↓
Difference types:
  ├─ Compilation errors → Check conditional compilation
  ├─ Missing files → Check publish configuration
  ├─ Runtime errors → Check optimization issues
  └─ Packaging errors → Coordinate with Release Engineer
  ↓
Fix configuration issue
  ↓
Verify with Gate C script
  ↓
Document proof
```

**Worked Example (VS-0023)**:
- Issue: Release build configuration hotfix
- Root cause: Release configuration missing required settings
- Fix: Update VoiceStudio.App.csproj with Release settings
- Proof: `.\scripts\gatec-publish-launch.ps1 -Configuration Release` succeeds

### Anti-Patterns to Avoid

| Anti-Pattern | Why It's Bad | Better Approach |
|--------------|--------------|-----------------|
| "Works on my machine" | Not reproducible | Always verify with clean build |
| Ignoring warnings | Warnings become errors | Address warnings promptly |
| Skipping CI checks | Breaks integration | Require CI green before merge |
| Manual build steps | Error-prone | Automate everything in scripts |
| Outdated tooling | Compatibility issues | Regular toolchain maintenance |

---

## 8. Cross-Role Coordination

### Dependencies on Other Roles

| Role | Dependency Type | Coordination Pattern |
|------|-----------------|---------------------|
| Overseer | Gate validation, evidence collection | Report build status, provide binlogs |
| System Architect | SDK/dependency decisions | Coordinate version changes |
| UI Engineer | XAML build issues | Joint troubleshooting for XAML failures |
| Core Platform | Backend build integration | Ensure Python build works |
| Engine Engineer | Engine venv builds | Support per-engine venv scripts |
| Release Engineer | Publish configuration | Align on packaging requirements |

### Conflict Resolution Protocol

Build & Tooling has authority over:
- Build determinism and enforcement
- CI pipeline configuration
- Toolchain decisions within approved versions

Defer to other roles for:
- Application logic changes
- Architectural decisions (defer to System Architect)
- Release process (defer to Release Engineer)

### Shared Artifacts

| Artifact | Build Role | Other Roles |
|----------|------------|-------------|
| Binlogs | Primary producer | Overseer (reviewer) |
| CI configs | Primary owner | All (consumers) |
| Build scripts | Primary owner | Release (contributor) |
| .editorconfig | Primary owner | All (consumers) |

---

## 9. Context Manager Testing and Hook Integration

> **Reference**: [CONTEXT_MANAGER_INTEGRATION.md](../CONTEXT_MANAGER_INTEGRATION.md)

The Build & Tooling Engineer owns testing of the context manager and verification of hook integration.

### 9.1 Ownership Scope

| Component | Build & Tooling Responsibility |
|-----------|-------------------------------|
| Test Suite | Maintain `tests/tools/test_context_allocator.py` |
| Hook Verification | Verify `.cursor/hooks/inject_context.py` execution |
| CI Integration | Add context manager tests to CI pipeline |
| Test Infrastructure | Provide mocking utilities for adapters |

### 9.2 Context Manager Testing

**Test Location**: `tests/tools/test_context_allocator.py`

**Current State**: 2 unit tests (minimal coverage)

**Required Test Coverage**:

| Test Category | Description | Status |
|---------------|-------------|--------|
| Allocator Unit | Budget enforcement, truncation | ✅ Exists |
| Cache Unit | TTL expiry, key generation | ⚠️ Minimal |
| Adapter Mocking | Mock each source adapter | ❌ Missing |
| Integration | Full allocation flow | ❌ Missing |
| CLI | Command-line interface | ❌ Missing |
| Hook | Hook script execution | ❌ Missing |

**Test Execution Commands**:

```powershell
# Run all context manager tests
python -m pytest tests/tools/test_context_allocator.py -v

# Run with coverage
python -m pytest tests/tools/test_context_allocator.py --cov=tools/context --cov-report=html

# Run specific test
python -m pytest tests/tools/test_context_allocator.py::TestContextAllocator::test_budget_enforcement -v
```

### 9.3 Test Development Guide

**Adapter Mock Template**:

```python
"""Mock for StateSourceAdapter."""
from unittest.mock import MagicMock
from tools.context.core.protocols import ContextSourceProtocol
from tools.context.core.models import SourceResult, AllocationContext

def create_mock_adapter(name: str, priority: int, data: dict) -> ContextSourceProtocol:
    """Create a mock source adapter for testing."""
    mock = MagicMock(spec=ContextSourceProtocol)
    mock.name = name
    mock.priority = priority
    mock.fetch.return_value = SourceResult(
        source=name,
        data=data,
        fetch_time_ms=1.0,
        truncated=False
    )
    return mock
```

**Integration Test Template**:

```python
"""Integration tests for context manager."""
import pytest
from tools.context.core.manager import ContextManager
from tools.context.core.models import AllocationContext

class TestContextManagerIntegration:
    
    def test_full_allocation_flow(self, tmp_path):
        """Test complete allocation with all sources."""
        # Setup: Create STATE.md and task brief
        state_path = tmp_path / ".cursor" / "STATE.md"
        state_path.parent.mkdir(parents=True)
        state_path.write_text("# STATE\n## Active Task\nTASK-0001")
        
        # Execute: Allocate context
        manager = ContextManager(config_path=tmp_path / "config.json")
        context = AllocationContext(
            task_id="TASK-0001",
            phase="validate",
            budget_chars=10000
        )
        result = manager.allocate(context)
        
        # Verify: Check result structure
        assert result.total_chars <= 10000
        assert "state" in result.sources
    
    def test_budget_truncation(self):
        """Test that budget limits are enforced."""
        ...
    
    def test_cache_hit(self):
        """Test cache returns cached value."""
        ...
    
    def test_offline_fallback(self):
        """Test memory adapter works when MCP unavailable."""
        ...
```

### 9.4 Hook Integration Verification

**Hook Configuration**: `.cursor/hooks.json`

```json
{
  "beforeSubmitPrompt": [
    {"script": ".cursor/hooks/validate_state_read.py"},
    {"script": ".cursor/hooks/inject_context.py"}
  ]
}
```

**Hook Verification Commands**:

```powershell
# Verify hook configuration exists
Test-Path ".cursor/hooks.json"

# Verify hook script exists
Test-Path ".cursor/hooks/inject_context.py"

# Test hook execution (manual)
python .cursor/hooks/inject_context.py

# Test with task context
$env:CURSOR_TASK_ID = "TASK-0001"
python .cursor/hooks/inject_context.py
```

**Hook Integration Test**:

```python
"""Test hook integration."""
import subprocess
import json

def test_hook_script_executes():
    """Verify hook script runs without error."""
    result = subprocess.run(
        ["python", ".cursor/hooks/inject_context.py"],
        capture_output=True,
        text=True
    )
    assert result.returncode == 0, f"Hook failed: {result.stderr}"

def test_hook_produces_valid_output():
    """Verify hook output is valid preamble format."""
    result = subprocess.run(
        ["python", ".cursor/hooks/inject_context.py"],
        capture_output=True,
        text=True
    )
    # Output should contain context preamble markers
    assert "CONTEXT" in result.stdout or result.stdout == ""
```

### 9.5 CI Integration

Add context manager tests to CI pipeline in `.github/workflows/test.yml`:

```yaml
# Add to test workflow
- name: Context Manager Tests
  run: |
    python -m pytest tests/tools/test_context_allocator.py -v
    python -m pytest tests/tools/test_context_hook.py -v

- name: Hook Verification
  run: |
    python .cursor/hooks/inject_context.py --verify
```

**CI Checklist**:

- [ ] Context manager tests in `test.yml`
- [ ] Hook verification in `ci.yml`
- [ ] Coverage threshold set (recommend 70%)
- [ ] Test artifacts uploaded

### 9.6 Daily Cadence Additions

Add to Build & Tooling daily cadence:

**Context Manager Health Check**:

```powershell
# 1. Run context tests
python -m pytest tests/tools/test_context_allocator.py -v

# 2. Verify CLI works
python tools/context/allocate.py --task TASK-0001 --preamble

# 3. Verify hook runs
python .cursor/hooks/inject_context.py
```

### 9.7 Worked Example: Adding Adapter Mock Tests

**Objective**: Add mock tests for all source adapters.

**Steps**:

1. Create test file:
   ```powershell
   New-Item -Path "tests/tools/test_context_adapters.py" -ItemType File
   ```

2. Implement mock tests:
   ```python
   """Tests for context source adapters."""
   import pytest
   from unittest.mock import patch, MagicMock
   
   from tools.context.sources.state_adapter import StateSourceAdapter
   from tools.context.sources.task_adapter import TaskSourceAdapter
   from tools.context.core.models import AllocationContext
   
   class TestStateAdapter:
       def test_fetch_with_state_file(self, tmp_path):
           """Test StateAdapter reads STATE.md correctly."""
           # Create STATE.md
           state_file = tmp_path / ".cursor" / "STATE.md"
           state_file.parent.mkdir(parents=True)
           state_file.write_text("## Active Task\nTASK-0001")
           
           # Test adapter
           adapter = StateSourceAdapter(state_path=state_file)
           context = AllocationContext(task_id="TASK-0001")
           result = adapter.fetch(context)
           
           assert "TASK-0001" in str(result.data)
       
       def test_fetch_missing_state_file(self, tmp_path):
           """Test StateAdapter handles missing file gracefully."""
           adapter = StateSourceAdapter(state_path=tmp_path / "missing.md")
           context = AllocationContext(task_id="TASK-0001")
           result = adapter.fetch(context)
           
           assert result.data == {} or result.data is None
   ```

3. Run tests:
   ```powershell
   python -m pytest tests/tools/test_context_adapters.py -v
   ```

4. Add to CI:
   Update `.github/workflows/test.yml` to include new test file.

5. Document proof:
   ```powershell
   # Capture test output
   python -m pytest tests/tools/test_context_adapters.py -v > .buildlogs/proof_runs/context_adapter_tests.txt
   ```

**Exit Criteria**:
- All adapter tests pass
- Tests added to CI pipeline
- Coverage > 70% for adapters

---

## Appendix A: Templates

### Build Failure Ledger Entry Template

```markdown
### VS-XXXX — [Build failure description]

**State:** OPEN  
**Severity:** S0 Blocker / S2 Major  
**Gate:** B / C  
**Owner role:** Build & Tooling Engineer  
**Reviewer role:** Overseer  
**Categories:** BUILD  
**Introduced:** [date]  
**Last verified:** [date]

**Environment**

- OS: Windows 10.0.XXXXX
- .NET SDK: X.X.XXX
- Repo path: E:\VoiceStudio
- Configuration: Debug / Release

**Reproduction**

1. `git clean -xfd`
2. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
3. Observe: [error description]

**Expected**

- Build succeeds with exit code 0

**Actual**

- Build fails with [specific error]

**Evidence**

- Binlog: `.buildlogs/build_[timestamp].binlog`
- Error output: [excerpt]

**Fix plan**

- [ ] [Step 1]
- [ ] [Step 2]

**Proof run**

- Commands: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Result: [success/failure]
```

### CI Workflow Template

```yaml
name: Build Verification

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      
      - name: Restore
        run: dotnet restore VoiceStudio.sln
      
      - name: Build
        run: dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 --no-restore
      
      - name: RuleGuard
        run: python tools/verify_no_stubs_placeholders.py
```

---

## Appendix B: Quick Reference

### Build Prompt (for Cursor)

```text
You are the VoiceStudio Build & Tooling Engineer (Role 2).
Mission: keep build/publish/install lanes deterministic and enforced in CI.
Non-negotiables: unpackaged EXE + installer only; evidence via binlogs/logs; no hidden failures.
Start by reading: docs/governance/VoiceStudio_Production_Build_Plan.md, scripts/gatec-publish-launch.ps1.
Output: fragility findings, fix plan with proof commands, MSIX/drift warnings.
```

### Key Commands Quick Reference

```powershell
# Clean build
git clean -xfd && dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# Build with binlog
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 -bl:.buildlogs/build.binlog

# Gate C verification
.\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke

# RuleGuard verification
python tools/verify_no_stubs_placeholders.py

# Test execution
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
```

### Version Pins Location

| Component | File | Key |
|-----------|------|-----|
| .NET SDK | `global.json` | `sdk.version` |
| WinAppSDK | `Directory.Build.props` | `WindowsAppSDKVersion` |
| Build Tools | `Directory.Build.props` | `WindowsSdkBuildToolsVersion` |
| Python deps | `version_lock.json` | Various |
