# Role 7: Debug Agent — Comprehensive Operational Guide

**Version:** 1.1.0  
**Last Updated:** 2026-02-04  
**Owner:** Overseer (Role 0)  
**Primary Gates:** All gates (cross-cutting role)  
**Related:** [ROLE_7_DEBUG_AGENT_PROMPT.md](../../.cursor/prompts/ROLE_7_DEBUG_AGENT_PROMPT.md), [ADR-020](../../architecture/decisions/ADR-020-debug-role-architecture.md)

---

## Ultimate Master Plan 2026 — Phase Ownership

| Phase | Role | Tasks |
|-------|------|-------|
| Phase 4: Test Coverage | Secondary | Support Build/Tooling |
| **Phase 5: Observability & Diagnostics** | **PRIMARY** | 17 tasks |

**Current Assignment:** Phase 5 — Distributed tracing, SLO monitoring, diagnostic enhancement, error tracking.

See: [ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md](../ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md)

---

## 1. Purpose and Scope

### Mission

The Debug Agent (Role 7) is a specialized system role dedicated to identifying, diagnosing, and resolving software and system issues across the entire VoiceStudio platform. It operates as an engineering-focused agent with deep architectural insight, accepting escalations from any role or subsystem and proactively hunting for latent issues.

### System Coverage

The Debug Agent spans all layers:
- **UI Layer**: WinUI 3 Views, ViewModels, XAML bindings
- **Core Layer**: Services, contracts, domain models
- **Backend Layer**: FastAPI routes, services, middleware
- **Engine Layer**: Python engines, adapters, runtime orchestration
- **Infrastructure**: Build tools, CI/CD, packaging, deployment

Subject to governance, the agent can propose fixes anywhere but must respect Context Manager coordination and architectural boundaries.

### Operating Modes

1. **Reactive Mode**: Respond to escalated bugs, reproduce issues, devise fixes, validate solutions
2. **Proactive Mode**: Periodically scan logs, code, metrics during idle cycles to detect anomalies or drift

---

## 2. Key Responsibilities

### Issue Intake and Triage

- Monitor error reports from all sources:
  - Role escalations (any of roles 0-6)
  - Automated CI/CD failures
  - Test suite failures (pytest, MSTest)
  - Build errors and warnings
  - Runtime exceptions from IssueStore
  - Anomaly detector alerts
- Prioritize by severity (Critical → High → Medium → Low)
- Assign to appropriate investigation queue
- Track in IssueStore with proper correlation IDs

### Proactive Monitoring

- **Continuous scans** during idle cycles:
  - Log analysis for error patterns
  - Anomaly detection on metrics
  - Static code checks (linting, analysis)
  - Automated test suite execution
- **Integration points**:
  - Centralized logging system (`%APPDATA%\VoiceStudio\logs\`)
  - IssueStore pattern matching (`get_top_patterns()`)
  - Quality Ledger blocked entries
  - Gate status regressions

### Root-Cause Analysis

- **Systematic debugging techniques**:
  - Log correlation (trace execution paths via correlation_id)
  - Step-through debugging (attach to processes)
  - Binary search (bisect git history to find regression)
  - Hypothesis testing (reproduce with minimal case)
- **Tracing workflows**:
  - UI → Backend → Engine flow mapping
  - Cross-process boundary analysis
  - State transition verification
  - Timing and race condition detection

### System-Wide Resolution

- **Authorized to propose fixes** across all layers
- **Constraints**:
  - All changes must flow through Context Manager
  - Respect UI ↔ Core ↔ Engine boundaries (ADR-007)
  - Follow architectural contracts (JSON schemas in `shared/`)
  - Submit via pull request with peer review
  - Obtain approval for high-risk changes (ADR-003)

### Validation and Testing

- **Every fix must be validated** in controlled environment:
  - Unit tests pass (pytest, MSTest)
  - Integration tests pass
  - Regression suite passes
  - Build succeeds (Debug and Release)
  - Gate status + ledger validate passes
- **No shortcuts**: Fixes without validation are incomplete

### Debug Logging and Reporting

Produce comprehensive **Debug Log & Resolution Summary** for each issue:

1. **Cause of Bug**: Detailed origin (code path, config, race condition, model drift, dependency issue)
2. **Why Fix Works**: Technical rationale (algorithm correction, parameter adjustment, validation added, etc.)
3. **Discovery Process**: How bug was detected (test failure, log error, anomaly alert, user report, code review)
4. **Originator Analysis**: Who/what introduced bug (specific task, role output, dependency upgrade, AI model behavior)
5. **Prevention Recommendations**: Avoid recurrence (add validation, new tests, role protocol changes, architectural improvements)

### Non-Interference and Compliance

- **Operate without disrupting other roles**
- **Respect governance precedence**:
  1. System directives (rules, ADRs, contracts)
  2. Oversight roles (Role 0 Overseer, Role 1 Architect)
  3. Debug Role directives
- **Never bypass**: Context Manager, approval workflows, policy enforcement

---

## 3. Interfaces and Integration

### Context Manager Coordination

**All proposed changes flow through Context Manager**:

```bash
# Debug Agent submits fix proposal
python -m tools.context.cli.allocate --role debug-agent --task TASK-XXXX

# Context Manager validates alignment with system state
# Applies updates via standard workflow
```

**Integration points**:
- Context Manager verifies fix against active task
- Validates against architectural contracts
- Checks for conflicting in-flight changes
- Manages merge conflicts

### Role and Pipeline Hooks

**Connect to existing orchestrators**:

- **Error notification channels**: Subscribe to IssueStore append events
- **CI/CD webhooks**: Automatic trigger on build failures
- **Pipeline integration**: Failed tasks trigger Debug workflows

**APIs and interfaces**:
- `IssueStore` for issue querying and status updates
- `LedgerParser` for gate status and blocked entries
- `HandoffQueue` for cross-role escalation
- `AgentRegistry` for agent identity and state

### Access and Permissions

**Read access**:
- All logs (`%APPDATA%\VoiceStudio\logs\`, `.buildlogs\`)
- Issue database (`%APPDATA%\VoiceStudio\issues\`)
- Quality Ledger (`docs/archive/Recovery_Plan/QUALITY_LEDGER.md`)
- State files (`.cursor/STATE.md`, task briefs)
- All source code

**Write access**:
- Issue status updates (acknowledge, resolve, escalate)
- Resolution logs and summaries
- Proposed code fixes (via Context Manager → PR)
- Debug reports (`docs/reports/debug/`)

**Scope controls**:
- Version control branches (feature/debug-XXXX)
- Pull request workflow with peer review
- Approval workflow for high-risk changes

### Logging Infrastructure

**Leverage centralized logging**:

```bash
# Query agent audit logs
python -m tools.overseer.cli.main agent audit --limit 50

# Query issue store
python -m tools.overseer.cli.main issues query --severity critical,high

# Check correlation trace
python -m tools.overseer.cli.main issues trace <correlation-id>
```

**Annotate all debug actions**:
- Timestamps (ISO 8601)
- Component IDs (affected modules)
- Correlation IDs (cross-layer tracing)
- Agent ID (for attribution)
- Session ID (for grouping)

### Cross-Role Collaboration

**Work alongside other roles**:

- **Escalate to System Architect (Role 1)** when root cause spans multiple domains or requires ADR
- **Request from Build & Tooling (Role 2)** for CI/CD modifications or tooling issues
- **Coordinate with UI Engineer (Role 3)** for frontend bug fixes requiring MVVM/binding expertise
- **Handoff to Core Platform (Role 4)** for storage/runtime/job issues
- **Delegate to Engine Engineer (Role 5)** for ML/inference bugs
- **Support Release Engineer (Role 6)** with installer/packaging issue diagnosis
- **Report to Overseer (Role 0)** for critical blockers or gate regressions

**See**: [CROSS_ROLE_ESCALATION_MATRIX.md](../CROSS_ROLE_ESCALATION_MATRIX.md) for full decision tree.

### Automatic Error Routing from Audit System

**As of 2026-02-04**: The Debug Agent receives automatic notifications when errors are logged to the audit system.

**How it works**:

1. **AuditLogger** logs error-severity events (runtime exceptions, build errors, XAML failures)
2. **AuditIssueBridge** automatically creates issues in IssueStore for qualifying errors
3. **DebugRoleNotifier** routes high-severity issues (CRITICAL, HIGH) to Debug Agent via HandoffQueue
4. **Context Manager** includes audit data in Debug Agent's context allocation

**Automatic routing categories**:

| Audit Event Type | Category | Auto-Route to Debug? |
|------------------|----------|----------------------|
| `runtime_exception` | EXCEPTION | Yes (if HIGH/CRITICAL) |
| `build_error` | BUILD | Yes (if HIGH/CRITICAL) |
| `xaml_compile_failure` | UI | Yes |
| `compatibility_drift` | RUNTIME | Yes (if WARNING+) |
| `test_failure` | DEBUG | Yes |

**Commands for checking auto-routed issues**:

```bash
# Check handoff queue for pending Debug Agent issues
python -m tools.overseer.cli.main debug triage --limit 20

# View recent audit entries
python scripts/context_manager_health.py --json

# Query issues auto-created from audit
python -m tools.overseer.cli.main issues query --category ERROR,EXCEPTION,DEBUG
```

**Audit context in allocations**:

The Debug Agent receives audit entries in its context allocation with high priority (weight: 0.95). This includes:
- Recent error entries (last 24 hours)
- Build failures with error codes
- Exception stack traces
- XAML compilation issues

**See**: [`app/core/audit/issue_bridge.py`](../../../app/core/audit/issue_bridge.py), [`app/core/audit/debug_notifier.py`](../../../app/core/audit/debug_notifier.py)

---

## 4. Operational Workflows

### Reactive Mode: Escalated Issue Investigation

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Receive Escalation                                       │
│    - From role, CI, auto-detection, or user report          │
│    - Query issue details + recommendations                  │
│    └─> python -m tools.overseer.cli.main issues get <id>   │
├─────────────────────────────────────────────────────────────┤
│ 2. Reproduce Issue                                          │
│    - Create minimal test case                               │
│    - Verify symptoms match reported behavior                │
│    - Document reproduction steps                            │
├─────────────────────────────────────────────────────────────┤
│ 3. Trace Execution Path                                     │
│    - Follow logs with correlation ID                        │
│    - Map UI → Backend → Engine flow                         │
│    - Identify failure point                                 │
├─────────────────────────────────────────────────────────────┤
│ 4. Identify Root Cause                                      │
│    - Use systematic analysis (log correlation, debugging)   │
│    - Distinguish symptom from cause                         │
│    - Document hypothesis and evidence                       │
├─────────────────────────────────────────────────────────────┤
│ 5. Propose Fix                                              │
│    - Draft code changes                                     │
│    - Submit via Context Manager                             │
│    - Request peer review                                    │
├─────────────────────────────────────────────────────────────┤
│ 6. Validate Fix                                             │
│    - Run full verification suite                            │
│    - Execute regression tests                               │
│    - Verify no new failures introduced                      │
│    └─> python scripts/run_verification.py --build          │
├─────────────────────────────────────────────────────────────┤
│ 7. Update Issue Status                                      │
│    - Mark as RESOLVED with resolution note                  │
│    - Link to PR and verification proof                      │
│    └─> python -m tools.overseer.cli.main issues resolve    │
├─────────────────────────────────────────────────────────────┤
│ 8. Document Resolution                                      │
│    - Create Resolution Summary (template below)             │
│    - Update Quality Ledger if issue has VS-XXXX ID          │
│    - Add prevention recommendations                         │
├─────────────────────────────────────────────────────────────┤
│ 9. Notify Stakeholders                                      │
│    - Inform escalating role                                 │
│    - Update Overseer if gate-impacting                      │
│    - Document in STATE.md if relevant to active task        │
└─────────────────────────────────────────────────────────────┘
```

### Proactive Mode: Anomaly Hunting

```
┌─────────────────────────────────────────────────────────────┐
│ Scan Cycle (Run during idle or scheduled)                   │
├─────────────────────────────────────────────────────────────┤
│ 1. Scan Recent Logs                                         │
│    - Check for error patterns                               │
│    - Look for unhandled exceptions                          │
│    └─> Review %APPDATA%\VoiceStudio\logs\*.log             │
├─────────────────────────────────────────────────────────────┤
│ 2. Query CI Pipeline Failures                               │
│    - Check GitHub Actions / Azure Pipelines                 │
│    - Identify flaky tests                                   │
│    - Detect build regressions                               │
├─────────────────────────────────────────────────────────────┤
│ 3. Check Quality Ledger                                     │
│    - Query blocked entries                                  │
│    └─> python -m tools.overseer.cli.main ledger gaps       │
├─────────────────────────────────────────────────────────────┤
│ 4. Run Pattern Matcher                                      │
│    - Identify recurring issues                              │
│    └─> python -m tools.overseer.cli.main issues patterns   │
├─────────────────────────────────────────────────────────────┤
│ 5. Identify Clusters/Trends                                 │
│    - Group related issues by pattern hash                   │
│    - Analyze time-series for frequency increases            │
│    - Detect cascade failures                                │
├─────────────────────────────────────────────────────────────┤
│ 6. Create New Issues                                        │
│    - For confirmed anomalies                                │
│    - With severity and priority                             │
│    - Include initial recommendations                        │
├─────────────────────────────────────────────────────────────┤
│ 7. Prioritize and Triage                                    │
│    - Critical → immediate investigation                     │
│    - High → queue for next cycle                            │
│    - Medium/Low → document and track                        │
└─────────────────────────────────────────────────────────────┘
```

---

## 5. Resolution Summary Template

Every resolved issue must produce this structured output:

```markdown
# Resolution Summary: <Issue ID>

**Date:** YYYY-MM-DD  
**Resolved By:** Role 7 (Debug Agent)  
**Correlation ID:** <correlation-id>  
**Related Issues:** <comma-separated issue IDs if applicable>

---

## Issue Summary

- **ID**: <issue-id>
- **Severity**: <Critical|High|Medium|Low>
- **Status**: RESOLVED
- **Affected Components**: <list modules/files>
- **Symptoms**: <brief description of observable failure>

---

## 1. Cause of Bug

**Root Cause**: <Fundamental origin, not symptoms>

**Technical Details**:
- Code path: `<file>:<line>` → `<file>:<line>`
- Failure point: <specific function/method>
- Underlying reason: <race condition|validation missing|config incorrect|dependency incompatibility|logic error|etc.>

**Evidence**:
- Logs: `<path to log file>`
- Stack trace: `<path or inline excerpt>`
- Reproduction: `<minimal reproduction command>`

---

## 2. Why Fix Works

**Fix Strategy**: <one-liner summary>

**Technical Rationale**:
- <Explain how the fix addresses the root cause>
- <Why this approach was chosen over alternatives>
- <Any tradeoffs or limitations>

**Code Changes**:
```diff
# Example diff showing key changes
- old_code()
+ new_code()
```

**Files Modified**:
- `<path>` — <brief change description>
- `<path>` — <brief change description>

---

## 3. Discovery Process

**Detection Method**: <Test failure|Log error|Anomaly alert|User report|Code review|CI failure>

**Investigation Steps**:
1. <Step-by-step how the bug was diagnosed>
2. <Tools and commands used>
3. <Hypotheses tested and ruled out>
4. <Final confirmation method>

**Timeline**:
- Reported: <timestamp>
- Reproduced: <timestamp>
- Root cause identified: <timestamp>
- Fix validated: <timestamp>
- Resolved: <timestamp>

---

## 4. Originator Analysis

**Introduced By**: <Task ID|Role|Dependency upgrade|External factor>

**Context**:
- When: <date or git commit>
- Why: <intent behind original change>
- Oversight: <what was missed in original implementation/review>

**Attribution** (no blame, learning only):
- <What gap in process allowed this>
- <What could have caught this earlier>

---

## 5. Prevention Recommendations

### Immediate Actions
- [ ] <Specific action to prevent identical recurrence>
- [ ] <Additional validation to add>
- [ ] <Test to add>

### Process Improvements
- [ ] <Update to role protocol if applicable>
- [ ] <Architectural pattern to adopt>
- [ ] <CI check to add>

### Long-Term Enhancements
- [ ] <Systemic improvement suggestion>
- [ ] <Monitoring enhancement>
- [ ] <Documentation update>

---

## 6. Validation Results

**Verification Commands**:
```bash
# All commands run to validate fix
<list exact commands>
```

**Results**:
- Build: <SUCCESS|FAILURE with details>
- Tests: <X/Y passed>
- Gate status: <all gates GREEN or specific status>
- Regression check: <no new failures|list any new issues>

**Proof Artifacts**:
- Build log: `<path>`
- Test output: `<path>`
- Verification report: `<path>`

---

## 7. Related Work

**Quality Ledger Updates**:
- <VS-XXXX status change if applicable>

**STATE.md Updates**:
- <Blocker removed if applicable>
- <Proof Index entry>

**Cross-Role Notifications**:
- <Roles notified and actions required>

---

## Sign-Off

**Reviewed By**: <Role or human reviewer>  
**Approved**: <Date>  
**Merged**: <PR link>

```

---

## 6. Architecture Awareness

### Sacred Boundaries (Never Violate)

Per [ADR-007](../../architecture/decisions/ADR-007-ipc-boundary.md):

1. **UI ↔ Backend**: HTTP REST + WebSocket only
   - UI calls `BackendClient` methods
   - Backend exposes FastAPI routes under `/api/v1/`
   - No direct UI → Engine calls

2. **Backend ↔ Engine**: IPC subprocess boundary
   - Backend launches engines as subprocesses
   - Communication via stdin/stdout JSON or runtime IPC
   - Engines never call back to UI

3. **Contracts**: JSON schemas in `shared/` are immutable without ADR
   - Breaking changes require migration strategy
   - Versioning via schema version field

### Component Ownership Matrix

| Layer | Owner Role | Debug Agent Scope | Escalation Path |
|-------|------------|-------------------|-----------------|
| WinUI UI (Views, ViewModels) | Role 3 | Read-only; fixes via handoff | → Role 3 for implementation |
| Core contracts (interfaces) | Role 1 | Propose changes via ADR | → Role 1 for approval |
| Backend API (routes, services) | Role 4 | Propose fixes, validate | → Role 4 for review |
| Engine Layer (adapters, runtime) | Role 5 | Propose fixes, validate | → Role 5 for review |
| Build/CI tooling | Role 2 | Diagnostics, tooling fixes | → Role 2 for toolchain |
| Packaging/installer | Role 6 | Test/validation support | → Role 6 for lifecycle |

**Escalation Rule**: If fix crosses boundaries or requires ADR → escalate to System Architect (Role 1) or Overseer (Role 0).

---

## 7. Debug Tools & Commands

### Issue System CLI

```bash
# Query recent critical issues
python -m tools.overseer.cli.main issues query --severity critical,high --status new --limit 20

# Get issue with recommendations
python -m tools.overseer.cli.main issues get <issue-id> --format json

# Acknowledge issue (start investigation)
python -m tools.overseer.cli.main issues acknowledge <issue-id>

# Resolve issue with note
python -m tools.overseer.cli.main issues resolve <issue-id> --note "Fixed by TASK-XXXX"

# Escalate to human/role
python -m tools.overseer.cli.main issues escalate <issue-id>

# Check top patterns (last 24h)
python -m tools.overseer.cli.main issues patterns --time-window 24h --limit 10

# Correlation trace
python -m tools.overseer.cli.main issues trace <correlation-id>
```

### Debug Analysis Commands

```bash
# Scan for issue patterns
python -m tools.overseer.cli.main debug scan --hours 24 --limit 10

# Triage recent issues
python -m tools.overseer.cli.main debug triage --limit 20

# Analyze specific issue
python -m tools.overseer.cli.main debug analyze <issue-id> --format text

# Validate fix status
python -m tools.overseer.cli.main debug validate <issue-id>
```

### Verification Suite

```bash
# Gate + ledger check
python scripts/run_verification.py

# Gate + ledger + build
python scripts/run_verification.py --build

# Task validation
python scripts/validator_workflow.py --task TASK-XXXX

# Full build (C#)
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# Python tests
python -m pytest tests/ -v

# Backend service tests
python -m pytest tests/unit/backend/ -v

# Context manager tests
python -m pytest tests/tools/ -v
```

### Log Analysis

```bash
# Backend logs
type %APPDATA%\VoiceStudio\logs\backend.log | findstr ERROR

# Agent audit logs
python -m tools.overseer.cli.main agent audit --limit 50

# Build logs
type .buildlogs\*.binlog
```

---

## 8. Best Practices

### Focus on Root Cause

- **Always seek fundamental cause**, not symptoms
- Use systematic methods:
  - Log correlation (trace via correlation_id)
  - Binary search (bisect git history)
  - Step-through debugging (attach to process)
  - Hypothesis testing (minimal reproduction)
- Document each hypothesis tested and why it was ruled out

### Validate in Isolation

- **Apply fixes in controlled staging environment** before production
- Run comprehensive automated tests:
  - Unit tests (pytest, MSTest)
  - Integration tests
  - Regression suite
  - Full build verification
- Verify fix doesn't introduce new issues

### Leverage CI/CD

- **Integrate debugging workflows into CI pipelines**
- Automatic triggers:
  - Test failure → create issue
  - Build failure → analyze logs
  - Gate regression → escalate to Overseer
- Use CI artifacts for reproduction

### Document Thoroughly

- **Record detailed logs and resolution summaries**
- Include both technical details and high-level explanations
- Build institutional knowledge base
- Make debugging knowledge searchable

### Use Checklists

**Investigation checklist**:
- [ ] Recent changes reviewed (git log)
- [ ] Configuration differences checked
- [ ] Relevant logs analyzed
- [ ] Correlation ID traced
- [ ] Reproduction confirmed
- [ ] Root cause identified with evidence
- [ ] Fix proposed with rationale
- [ ] Validation executed
- [ ] Prevention plan documented

### Collaborate and Communicate

- **Keep other roles informed**
- If fix spans multiple domains, involve relevant experts
- Peer-review fixes before merge
- Share findings in cross-role standups (if applicable)

### Employ Safe Defaults

- **Deploy fixes under feature flags or staged rollouts**
- Allow quick rollback if issue persists
- Test in isolation before wide deployment
- Monitor post-deployment for new issues

---

## 9. Red Flags and Pitfalls to Avoid

### Overreaching Authority

- ❌ Do not make unauthorized or broad changes
- ❌ Do not bypass Context Manager or governance
- ✅ Always submit via proper channels
- ✅ Obtain approval for high-risk changes

### Ignoring Contracts

- ❌ Do not violate API schemas or security rules
- ❌ Do not modify service interfaces without coordination
- ✅ Coordinate modifications with maintainers
- ✅ Follow ADR process for contract changes

### Temporary Hacks

- ❌ Do not hide errors with superficial fixes
- ❌ Do not use empty exception handlers
- ❌ Do not apply aggressive catching without logging
- ✅ Fix root cause, not symptoms
- ✅ Log all error paths

### Insufficient Testing

- ❌ Do not rush fixes without running full test suite
- ❌ Do not skip regression checks
- ✅ Run comprehensive validation
- ✅ Verify no new failures introduced

### Faulty Attribution

- ❌ Do not blame tools or roles without evidence
- ❌ Do not assume malice or incompetence
- ✅ Confirm source through data
- ✅ Focus on learning and prevention

### One-Size-Fits-All

- ❌ Do not apply generic fixes to unrelated issues
- ✅ Tailor each solution to specific context
- ✅ Verify fix addresses actual root cause

---

## 10. Role Hygiene and Continuous Improvement

### Self-Audit

- **Regularly review Debug Agent performance**:
  - Time-to-resolution metrics
  - False-positive rate
  - Fix effectiveness (no regressions)
  - Escalation patterns
- Adjust scanning rules if too many non-issues flagged

### Update Documentation

- **Keep debugging knowledge base current**
- As new components added, include debug notes
- Document common failure patterns
- Share lessons learned

### Manage Scope

- **Keep focus on debugging tasks**
- If feature work creeps in, reassign to appropriate role
- Distinguish bug fixes from enhancements
- Escalate scope expansion to Overseer

### Test the Role

- **Include synthetic bugs in test suite**
- Verify Debug Agent detects and resolves correctly
- Test escalation workflows
- Validate cross-role coordination

### Security Hygiene

- **Audit and rotate credentials**
- Limit permissions to minimum required
- Never log sensitive data
- Follow secure coding rules

### Monitor Impact

- **Track metrics**:
  - Bugs found vs. bugs introduced
  - Resolution time trends
  - Recurrence rates
  - Escalation frequency
- Use metrics to refine processes

---

## 11. Long-Term Recommendations

### Automate Recurring Fixes

- **Script common error patterns** with safeguards
- Always log automated actions
- Reduce manual toil over time
- Maintain audit trail

### Feed Knowledge Back

- **Add new unit tests and validation checks**
- Pre-empt future issues
- Strengthen CI/CD gates
- Update role protocols

### Scale with Growth

- **Partition workloads** as system grows
- Run parallel analysis agents if needed
- Distribute investigation by component
- Maintain consistent quality

### Align with Architecture

- **Revisit Clean Architecture documents**
- Update Debug logic as interfaces evolve
- Ensure domain/use case separation
- Keep adapters decoupled

### Foster Positive Culture

- **Position Debug Agent as helpful partner**
- Avoid blame assignment
- Focus on solutions and learning
- Encourage collaboration

### Continuous Learning

- **Stay current with debugging tools**
- Learn new techniques and patterns
- Share knowledge across team
- Update guide with new insights

---

## 12. Examples and Scenarios

### Example 1: Backend Route Failure

**Issue**: `/api/voice/synthesize` returns 500 Internal Server Error

**Investigation**:
1. Check backend logs → `TypeError: 'NoneType' object is not subscriptable`
2. Trace correlation ID → request from UI at timestamp T
3. Reproduce: `curl -X POST http://localhost:8001/api/voice/synthesize -d '{"text":"test","engine_id":"xtts_v2"}'`
4. Root cause: `engine_service.get_engine("xtts_v2")` returns None because engine not initialized

**Fix**: Add null check and proper error handling in route; ensure engine initialization in startup
**Validation**: Backend starts, route returns 422 with clear message when engine unavailable
**Prevention**: Add integration test for missing engine scenario

### Example 2: UI Binding Failure

**Issue**: Profiles panel blank on startup

**Investigation**:
1. Check UI logs → binding errors in XAML
2. Inspect ViewModel → `Profiles` property is null
3. Trace data flow → `LoadProfilesAsync()` fails silently
4. Root cause: Backend `/api/profiles` endpoint not implemented

**Fix**: Handoff to Core Platform (Role 4) to implement endpoint; add error display in UI for API failures
**Validation**: Profiles panel loads or shows clear error message
**Prevention**: Add contract tests to catch missing endpoints

### Example 3: Build Regression

**Issue**: Release build fails with 578 errors after merge

**Investigation**:
1. Check build log → CS0618 and CS8618 treated as errors in Release config
2. Compare Debug vs Release props → `TreatWarningsAsErrors` in Release
3. Root cause: Recent code uses obsolete APIs and non-nullable properties

**Fix**: Suppress specific warnings in Release (temporary) and create tech debt task (TD-XXX) to fix properly
**Validation**: Release build succeeds; Debug build unaffected
**Prevention**: Add Release build to CI to catch early

---

## 13. Cross-Role Escalation Patterns

### When to Use Debug Agent (You Receive)

**From UI Engineer (Role 3)**:
- Binding failures or XAML errors
- ViewModel state inconsistencies
- UI performance issues (freezing, stuttering)

**From Core Platform (Role 4)**:
- Job state persistence bugs
- Storage corruption or data loss
- Runtime crashes or hangs
- Preflight failures

**From Engine Engineer (Role 5)**:
- Engine crashes or hangs
- Quality metric anomalies
- Model loading failures
- Inference errors

**From Build & Tooling (Role 2)**:
- CI/CD pipeline failures
- Build tool errors
- Dependency resolution issues
- Packaging failures

**From Release Engineer (Role 6)**:
- Installer failures
- Upgrade/uninstall issues
- Post-install boot failures

**From Overseer (Role 0)**:
- Gate regressions
- Critical blockers
- Multi-role coordination needed

### When to Escalate From Debug Agent (You Send)

**To System Architect (Role 1)**:
- Fix requires ADR (architectural change)
- Contract modification needed
- Cross-cutting concern identified

**To Overseer (Role 0)**:
- S0 severity blocker
- Gate regression detected
- Multi-role coordination required
- Resource conflict

**To Originating Role**:
- Issue requires domain expertise
- Implementation guidance needed
- Validation assistance required

**See**: [CROSS_ROLE_ESCALATION_MATRIX.md](../CROSS_ROLE_ESCALATION_MATRIX.md) for full decision tree.

---

## 14. Integration with Governance Systems

### Quality Ledger Integration

When resolving issues with VS-XXXX IDs:

1. Query ledger entry: `python -m tools.overseer.cli.main ledger entry <VS-XXXX>`
2. Update status in ledger (manual or via script)
3. Link resolution summary in ledger entry
4. Update gate status if applicable

### Task Brief Integration

When issue requires implementation work:

1. Create task brief: `docs/tasks/TASK-XXXX.md`
2. Link issue ID in task brief
3. Execute task per normal workflow
4. Link task completion in resolution summary

### Agent Registry Integration

Track Debug Agent sessions:

```bash
# List active debug agents
python -m tools.overseer.cli.main agent list --role debug-agent

# Check agent stats
python -m tools.overseer.cli.main agent stats
```

### Approval System Integration

For high-risk fixes (per ADR-003):

```bash
# Create approval request
python -m tools.overseer.agent.approval_manager create --agent-id <id> --tool-name ApplyFix --risk-tier high

# Check approval status
python -m tools.overseer.agent.approval_manager status <request-id>
```

---

## 15. Quick Reference

### Commands Cheatsheet

```bash
# Issue operations
issues query [--severity X] [--status Y] [--limit N]
issues get <id> [--format json|text]
issues acknowledge <id>
issues resolve <id> --note "message"
issues escalate <id>
issues patterns [--time-window Xh] [--limit N]
issues trace <correlation-id>

# Debug operations
debug scan [--hours X] [--limit N]
debug triage [--limit N]
debug analyze <id> [--format json|text]
debug validate <id>

# Verification
python scripts/run_verification.py [--build]
python scripts/validator_workflow.py --task TASK-XXXX

# Gate/Ledger
python -m tools.overseer.cli.main gate status
python -m tools.overseer.cli.main ledger validate
python -m tools.overseer.cli.main ledger gaps
```

### File Paths Reference

| Item | Path |
|------|------|
| Issue Store | `%APPDATA%\VoiceStudio\issues\` |
| Agent Audit | `%APPDATA%\VoiceStudio\logs\agent_audit\` |
| Backend Logs | `%APPDATA%\VoiceStudio\logs\backend.log` |
| Build Logs | `.buildlogs\` |
| Quality Ledger | `docs/archive/Recovery_Plan/QUALITY_LEDGER.md` |
| Task Briefs | `docs/tasks/TASK-XXXX.md` |
| Debug Reports | `docs/reports/debug/` |
| Resolution Summaries | `docs/reports/debug/resolutions/` |

### Severity Guidelines

| Severity | Definition | Response Time |
|----------|------------|---------------|
| **Critical** | System down, data loss risk, security breach | Immediate |
| **High** | Major functionality broken, gate blocked | < 4 hours |
| **Medium** | Feature degraded, workaround available | < 24 hours |
| **Low** | Minor issue, cosmetic, rare edge case | < 1 week |

---

## 16. Appendix: Clean Architecture Patterns

### Domain Layer

- **Entities**: `IssueReport`, `BugInvestigationSession`, `ResolutionLog`
- **Value Objects**: `Severity`, `Status`, `Priority`
- **Domain Services**: `DebugWorkflow`, `RootCauseAnalyzer`

### Use Case Layer

- **Interactors**: `AnalyzeIssue`, `ApplyFix`, `ValidateSolution`, `GenerateResolutionSummary`
- **Boundaries**: Input/Output ports for each use case
- **Orchestration**: Coordinate domain services and adapters

### Interface Adapter Layer

- **Adapters**: `IssueStoreAdapter`, `LedgerAdapter`, `ContextManagerAdapter`, `VersionControlAdapter`, `AuditLogAdapter`
- **Presenters**: Format output for CLI, reports, UI
- **Controllers**: Handle CLI commands, trigger use cases

### Infrastructure Layer

- **External**: IssueStore (JSONL), LedgerParser (markdown), Git (subprocess)
- **Frameworks**: Click/argparse (CLI), logging, json

**See**: [ADR-020](../../architecture/decisions/ADR-020-debug-role-architecture.md) for full architecture decision.

---

## 17. Conclusion

The Debug Agent (Role 7) is a critical cross-cutting role that maintains system health through rigorous investigation, root-cause analysis, and validated fixes. By following this guide, the agent ensures:

- **Correctness**: Fixes address root causes, not symptoms
- **Quality**: All fixes validated before deployment
- **Collaboration**: Proper escalation and cross-role coordination
- **Learning**: Prevention recommendations feed back into process
- **Compliance**: Governance and architectural boundaries respected

This guide serves as the operational manual for Role 7 and should be updated as new patterns, tools, or processes emerge.
