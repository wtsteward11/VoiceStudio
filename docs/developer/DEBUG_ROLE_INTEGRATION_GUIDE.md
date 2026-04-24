# Debug Role Integration Guide

**Version:** 1.0.0  
**Last Updated:** 2026-01-30  
**Audience:** Developers, DevOps, Role Implementers  
**Related:** [ROLE_7_DEBUG_AGENT_GUIDE.md](../governance/roles/ROLE_7_DEBUG_AGENT_GUIDE.md), [ADR-017](../architecture/decisions/ADR-017-debug-role-architecture.md)

---

## Overview

This guide explains how to integrate the Debug Agent (Role 7) into your workflow, including:
- Issue-to-task automation
- CLI command reference
- Integration with IssueStore and handoff system
- Workflow examples
- Troubleshooting

---

## Quick Start

### 1. Query Recent Issues

```bash
# Check critical/high severity issues
python -m tools.overseer.cli.main issues query --severity critical,high --status new --limit 20
```

### 2. Investigate Specific Issue

```bash
# Get issue details with recommendations
python -m tools.overseer.cli.main issues get VS-0033 --format text

# Analyze for root cause
python -m tools.overseer.cli.main debug analyze VS-0033
```

### 3. Apply and Validate Fix

```bash
# After implementing fix, validate
python scripts/run_verification.py --build

# Update issue status
python -m tools.overseer.cli.main issues resolve VS-0033 --note "Fixed by TASK-0022"
```

---

## Issue-to-Task Workflow Automation

### Automatic Task Creation

When an issue requires significant implementation work, create a task brief:

```bash
# Create task brief from issue
python -m tools.overseer.cli.main issues create-task VS-0033
```

This generates `docs/tasks/TASK-XXXX.md` with:
- Issue ID cross-reference
- Affected components (from issue context)
- Acceptance criteria (from recommendations)
- Required proofs (validation commands)

### Manual Task Creation

If automatic task creation isn't suitable:

1. Copy `docs/tasks/TASK_TEMPLATE.md` to `docs/tasks/TASK-XXXX.md`
2. Fill in sections referencing the issue:
   ```markdown
   **Related Issues**: VS-0033
   **Objective**: Resolve <issue description>
   ```
3. Add to `.cursor/STATE.md` Active Task
4. Execute per normal task workflow
5. Link task completion in issue resolution

### Task Execution with Debug Context

```bash
# Allocate context for debug task
python -m tools.context.cli.allocate --role debug-agent --task TASK-XXXX --preamble

# Generate onboarding packet for Role 7
python -m tools.onboarding.cli.onboard --role 7 --output .cursor/debug_packet.md

# Invoke role skill wrapper (includes context + onboarding)
python .cursor/skills/roles/debug-agent/scripts/invoke.py
```

---

## CLI Command Reference

### Issue Commands

#### Query Issues

```bash
# Basic query
python -m tools.overseer.cli.main issues query

# Filter by severity
python -m tools.overseer.cli.main issues query --severity critical,high

# Filter by status
python -m tools.overseer.cli.main issues query --status new,acknowledged

# Filter by time window
python -m tools.overseer.cli.main issues query --since 24h --limit 50

# Filter by instance type
python -m tools.overseer.cli.main issues query --instance-type backend,engine

# Combine filters
python -m tools.overseer.cli.main issues query \
  --severity high \
  --status new \
  --instance-type backend \
  --limit 20
```

#### Get Issue Details

```bash
# Text format (human-readable)
python -m tools.overseer.cli.main issues get VS-0033

# JSON format (machine-readable)
python -m tools.overseer.cli.main issues get VS-0033 --format json

# With recommendations
python -m tools.overseer.cli.main issues get VS-0033 --include-recommendations
```

#### Update Issue Status

```bash
# Acknowledge (start investigation)
python -m tools.overseer.cli.main issues acknowledge VS-0033

# Resolve with note
python -m tools.overseer.cli.main issues resolve VS-0033 --note "Fixed in TASK-0022"

# Escalate to human/role
python -m tools.overseer.cli.main issues escalate VS-0033 --to role-1 --reason "Requires ADR"
```

#### Pattern Analysis

```bash
# Top patterns (last 24h)
python -m tools.overseer.cli.main issues patterns --time-window 24h --limit 10

# By instance type
python -m tools.overseer.cli.main issues patterns --instance-type engine

# Trace correlation
python -m tools.overseer.cli.main issues trace a3f7d8c9-4e2b-4a1f-9d6e-7c8f5b3a1e2d
```

### Debug Commands

#### Scan for Issues

```bash
# Scan logs for patterns (last 24h)
python -m tools.overseer.cli.main debug scan --hours 24 --limit 10

# Scan specific time window
python -m tools.overseer.cli.main debug scan --hours 168 --limit 50  # Last week
```

#### Triage Issues

```bash
# List recent issues for triage
python -m tools.overseer.cli.main debug triage --limit 20

# Filter by severity
python -m tools.overseer.cli.main debug triage --severity critical,high
```

#### Analyze Issue

```bash
# Text format (for human review)
python -m tools.overseer.cli.main debug analyze VS-0033

# JSON format (for automation)
python -m tools.overseer.cli.main debug analyze VS-0033 --format json

# Output includes:
# - Issue summary
# - Root cause hypothesis
# - Recommendations with confidence scores
# - Similar issues
# - Risk assessment
```

#### Validate Fix

```bash
# Check if issue is resolved
python -m tools.overseer.cli.main debug validate VS-0033

# Outputs latest status and validation state
```

---

## Integration with Handoff System

### Handoff Queue

The `HandoffQueue` manages cross-role issue escalation:

```python
from tools.overseer.issues.handoff import HandoffQueue

queue = HandoffQueue()

# Get issues assigned to a role
entries = queue.get_role_queue("core-platform", unacknowledged_only=True)

# Handoff issue to another role
queue.handoff(
    issue_id="VS-0033",
    from_role="debug-agent",
    to_role="core-platform",
    reason="Storage durability issue requires Role 4 expertise",
    priority="high",
)

# Acknowledge receipt
queue.acknowledge(entry_id="<id>", role="core-platform")
```

### CLI Handoff Commands

```bash
# View role's handoff queue
python -m tools.overseer.cli.main handoff list --role core-platform

# Handoff issue to role
python -m tools.overseer.cli.main handoff create \
  --issue VS-0033 \
  --from debug-agent \
  --to core-platform \
  --reason "Requires storage expertise"

# Acknowledge handoff
python -m tools.overseer.cli.main handoff acknowledge <entry-id>

# Complete handoff with resolution
python -m tools.overseer.cli.main handoff complete <entry-id> --resolution "Fixed in TASK-0023"
```

---

## Workflow Examples

### Example 1: Simple Bug Fix

**Scenario**: Backend route returns 500 error

```bash
# 1. Issue detected automatically
# IssueStore has new entry: ISS-backend-20260130-001

# 2. Query issue
python -m tools.overseer.cli.main issues get ISS-backend-20260130-001
# Output shows: TypeError in /api/voice/synthesize route

# 3. Acknowledge and investigate
python -m tools.overseer.cli.main issues acknowledge ISS-backend-20260130-001

# 4. Reproduce locally
curl -X POST http://localhost:8001/api/voice/synthesize -d '{"text":"test"}'
# Confirms 500 error

# 5. Identify root cause
# Check backend logs, find: engine_service.get_engine() returns None
# Root cause: engine not initialized in startup

# 6. Implement fix
# Add null check in route, proper error handling
# Ensure engine initialization in main.py startup

# 7. Validate fix
python -m pytest tests/unit/backend/api/routes/test_voice.py -v
python scripts/run_verification.py --build
# All pass

# 8. Resolve issue
python -m tools.overseer.cli.main issues resolve ISS-backend-20260130-001 \
  --note "Fixed: Added null check and engine init verification"

# 9. Generate resolution summary
python -m tools.overseer.cli.main debug generate-summary ISS-backend-20260130-001 \
  --output docs/reports/debug/resolutions/ISS-backend-20260130-001.md
```

### Example 2: Complex Cross-Layer Issue

**Scenario**: Wizard flow fails intermittently

```bash
# 1. Pattern detected
python -m tools.overseer.cli.main issues patterns
# Shows cluster of wizard failures with similar stack traces

# 2. Create investigation task
python -m tools.overseer.cli.main issues create-task VS-0034
# Generates TASK-0024.md

# 3. Allocate context for deep investigation
python -m tools.context.cli.allocate --role debug-agent --task TASK-0024 --preamble

# 4. Analyze patterns
python -m tools.overseer.cli.main debug analyze VS-0034
# Recommendations suggest race condition in state persistence

# 5. Reproduce with timing variations
# Use correlation ID to trace across UI → Backend → Engine
python -m tools.overseer.cli.main issues trace <correlation-id>

# 6. Identify root cause: JobStateStore write not atomic
# Race condition when wizard state updated during concurrent access

# 7. Fix requires Core Platform expertise
python -m tools.overseer.cli.main handoff create \
  --issue VS-0034 \
  --from debug-agent \
  --to core-platform \
  --reason "Storage atomicity issue"

# 8. Role 4 implements atomic write pattern
# (Role 4 workflow here)

# 9. Validate fix with stress test
python scripts/wizard_flow_proof.py --concurrent 5 --iterations 10

# 10. Resolve and document
python -m tools.overseer.cli.main issues resolve VS-0034 --note "Fixed: Atomic writes in JobStateStore"
python -m tools.overseer.cli.main debug generate-summary VS-0034
```

### Example 3: Proactive Anomaly Detection

```bash
# 1. Scheduled scan (e.g., nightly cron)
python -m tools.overseer.cli.main debug scan --hours 24 --limit 20
# Output: 3 potential issues detected

# 2. Triage findings
python -m tools.overseer.cli.main debug triage
# Lists: ISS-auto-001 (HIGH), ISS-auto-002 (MEDIUM), ISS-auto-003 (LOW)

# 3. Investigate high-priority anomaly
python -m tools.overseer.cli.main debug analyze ISS-auto-001
# Recommendations: Memory leak suspected in engine process

# 4. Confirm with detailed investigation
# Monitor engine memory usage over time
# Find: Memory grows unbounded during long synthesis tasks

# 5. Create task for engine memory profiling
python -m tools.overseer.cli.main issues create-task ISS-auto-001

# 6. Handoff to Engine Engineer (Role 5)
python -m tools.overseer.cli.main handoff create \
  --issue ISS-auto-001 \
  --from debug-agent \
  --to engine-engineer \
  --reason "Engine memory leak requires ML expertise"

# 7. Role 5 investigates and fixes
# (Role 5 workflow)

# 8. Validate fix with long-running test
python scripts/engine_stress_test.py --duration 3600

# 9. Resolve and close
python -m tools.overseer.cli.main issues resolve ISS-auto-001
```

---

## Integration Points

### IssueStore Integration

**Location**: `tools/overseer/issues/store.py`

**Key Methods**:
```python
from tools.overseer.issues.store import IssueStore

store = IssueStore()

# Append new issue
store.append(issue)

# Query issues
issues = store.query(
    severity=[IssueSeverity.CRITICAL, IssueSeverity.HIGH],
    status=[IssueStatus.NEW],
    limit=20
)

# Get by ID
issue = store.get_by_id("VS-0033")

# Update status
store.update_status("VS-0033", IssueStatus.RESOLVED, resolved_by="Role 7")

# Pattern analysis
patterns = store.get_top_patterns(limit=10, time_window_hours=24)
```

### Quality Ledger Integration

**Location**: `docs/archive/Recovery_Plan/QUALITY_LEDGER.md`

**Integration**:
```bash
# Check ledger for blocked entries
python -m tools.overseer.cli.main ledger gaps

# Get specific entry
python -m tools.overseer.cli.main ledger entry VS-0033

# After resolving issue, update ledger (manual edit):
# Change state from BLOCKED → IN_PROGRESS or DONE
```

**Automatic Updates** (planned):
```bash
# Update ledger from resolved issue
python -m tools.overseer.cli.main ledger update-from-issue VS-0033
```

### Agent Registry Integration

**Location**: `tools/overseer/agent/registry.py`

**Registration**:
```python
from tools.overseer.agent.identity import AgentIdentity, AgentRole
from tools.overseer.agent.registry import AgentRegistry

# Register Debug Agent
registry = AgentRegistry()
identity = AgentIdentity.create(
    role=AgentRole.DEBUGGER,
    user_id="voicestudio",
    session_id="<session-id>",
)
registry.register(identity)

# Query active debug agents
agents = registry.get_by_role(AgentRole.DEBUGGER)
```

### Approval Workflow Integration

**Location**: `tools/overseer/agent/approval_manager.py`

**For High-Risk Fixes**:
```python
from tools.overseer.agent.approval_manager import ApprovalManager

manager = ApprovalManager()

# Create approval request
request = manager.create_request(
    agent_id="<agent-id>",
    user_id="voicestudio",
    correlation_id="<correlation-id>",
    tool_name="ApplyFix",
    parameters={"issue_id": "VS-0033", "files": ["backend/api/routes/voice.py"]},
    risk_tier="high",
    reason="Modifies critical API endpoint",
)

# Wait for approval (blocking)
status = manager.wait_for_approval(request.request_id, timeout_minutes=30)

# Or check asynchronously
status = manager.get_request(request.request_id).status
```

---

## Resolution Summary Generation

### Automatic Generation

```bash
# Generate from resolved issue
python -m tools.overseer.cli.main debug generate-summary VS-0033 \
  --output docs/reports/debug/resolutions/VS-0033.md
```

### Manual Template

If automatic generation isn't available, use this template:

```markdown
# Resolution Summary: VS-0033

**Date:** 2026-01-30
**Resolved By:** Role 7 (Debug Agent)
**Correlation ID:** <correlation-id>

## Issue Summary
- **ID**: VS-0033
- **Severity**: High
- **Affected Components**: backend/api/routes/voice.py, backend/services/EngineService.py

## 1. Cause of Bug
<Root cause description>

## 2. Why Fix Works
<Fix rationale>

## 3. Discovery Process
<Investigation steps>

## 4. Originator Analysis
<What introduced the bug>

## 5. Prevention Recommendations
- [ ] Add integration test for missing engine
- [ ] Add null check validation in route layer
- [ ] Document engine initialization requirements

## 6. Validation Results
```bash
python -m pytest tests/unit/backend/api/routes/test_voice.py -v
# Result: 12/12 PASSED
```

**Proof**: `.buildlogs/verification/last_run.json`
```

---

## Handoff System Integration

### HandoffQueue Usage

```python
from tools.overseer.issues.handoff import HandoffQueue

queue = HandoffQueue()

# Check your role's queue
entries = queue.get_role_queue("debug-agent", unacknowledged_only=True)
for entry in entries:
    print(f"Issue {entry['issue_id']} from {entry['from_role']}: {entry['message']}")

# Process handoff
entry_id = entries[0]["id"]
queue.acknowledge(entry_id, role="debug-agent")

# After investigation, handoff back or to another role
queue.handoff(
    issue_id="VS-0033",
    from_role="debug-agent",
    to_role="core-platform",
    reason="Root cause in storage layer",
    priority="high",
)
```

### CLI Handoff Commands

```bash
# List handoffs for role
python -m tools.overseer.cli.main handoff list --role debug-agent

# Show specific handoff
python -m tools.overseer.cli.main handoff show <entry-id>

# Acknowledge handoff
python -m tools.overseer.cli.main handoff acknowledge <entry-id>

# Create new handoff
python -m tools.overseer.cli.main handoff create \
  --issue VS-0033 \
  --from debug-agent \
  --to engine-engineer \
  --reason "Engine-specific bug" \
  --priority high

# Complete handoff
python -m tools.overseer.cli.main handoff complete <entry-id> \
  --resolution "Fixed in TASK-0025"
```

---

## Troubleshooting

### Issue: "Issue not found"

**Cause**: Issue ID doesn't exist in IssueStore

**Solution**:
```bash
# List all issues
python -m tools.overseer.cli.main issues query --limit 100

# Check if issue in Quality Ledger instead
python -m tools.overseer.cli.main ledger entry VS-XXXX
```

### Issue: "ModuleNotFoundError: tools.overseer.cli"

**Cause**: Running from wrong directory or Python path not set

**Solution**:
```bash
# Ensure you're in project root
cd e:\VoiceStudio

# Run as module
python -m tools.overseer.cli.main issues query

# Or add to PYTHONPATH
set PYTHONPATH=e:\VoiceStudio
python -m tools.overseer.cli.main issues query
```

### Issue: "Permission denied" on Windows

**Cause**: Writing to protected directory or file in use

**Solution**:
- Run as administrator if needed (for system paths)
- Check file isn't locked by another process
- Use `%APPDATA%\VoiceStudio\` for user-scoped files

### Issue: Validation fails after fix

**Cause**: Fix incomplete or introduces regression

**Solution**:
```bash
# Run comprehensive verification
python scripts/run_verification.py --build

# Check specific test failures
python -m pytest tests/ -v --failed-first

# Review build log for errors
type .buildlogs\*.binlog | findstr /i error
```

### Issue: Handoff not received by target role

**Cause**: Target role not monitoring handoff queue

**Solution**:
- Manually notify target role (mention in STATE.md or direct communication)
- Check handoff entry created: `python -m tools.overseer.cli.main handoff list --role <target>`
- Escalate to Overseer if urgent

---

## Best Practices

### 1. Always Reproduce First

Don't start fixing until you can reproduce the issue consistently. A fix for an unreproducible issue is unverifiable.

### 2. Document Evidence

Capture logs, stack traces, correlation IDs before they're rotated or overwritten. Save reproduction commands.

### 3. Use Correlation IDs

Trace issues across layers using correlation IDs. This is the fastest way to understand distributed failures.

### 4. Check Recent Changes

Often bugs are introduced by recent commits. Review git log for affected files:
```bash
git log --oneline --since="1 week ago" -- <file-path>
```

### 5. Validate Thoroughly

Run full verification suite, not just the specific failing test. Ensure no regressions.

### 6. Document Prevention

Every fix should include recommendations to prevent similar issues. This builds institutional knowledge.

### 7. Escalate When Needed

Don't spend hours on issues outside your expertise. Handoff to the appropriate role with context.

---

## Automation Opportunities

### Scheduled Proactive Scans

```powershell
# Windows Task Scheduler or cron equivalent
# Run nightly scan
python -m tools.overseer.cli.main debug scan --hours 24 --limit 20
```

### CI/CD Integration

**GitHub Actions / Azure Pipelines**:

```yaml
# .github/workflows/debug-scan.yml
name: Proactive Debug Scan
on:
  schedule:
    - cron: '0 2 * * *'  # 2 AM daily
jobs:
  scan:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Run debug scan
        run: python -m tools.overseer.cli.main debug scan --hours 24
      - name: Triage findings
        run: python -m tools.overseer.cli.main debug triage --limit 20
      - name: Create issues for critical findings
        run: python scripts/auto_create_issues.py
```

### Automatic Issue Creation from Test Failures

```python
# In pytest conftest.py or CI script
def pytest_runtest_logreport(report):
    if report.failed:
        from tools.overseer.issues.aggregator import log_issue
        log_issue(
            instance_type=InstanceType.BUILD,
            instance_id="pytest",
            severity=IssueSeverity.HIGH,
            category="test_failure",
            error_type=report.longrepr.reprcrash.message,
            message=f"Test failed: {report.nodeid}",
            context={"test": report.nodeid, "stage": report.when},
            correlation_id=os.getenv("CI_RUN_ID", "local"),
        )
```

---

## Advanced Topics

### Custom Adapters

To integrate with external systems (Jira, GitHub Issues, etc.):

```python
from tools.overseer.adapters.base import IssueTrackerAdapter

class JiraAdapter(IssueTrackerAdapter):
    """Adapter for Jira integration."""
    
    def sync_issues(self) -> List[IssueReport]:
        """Fetch issues from Jira and convert to domain."""
        # Implementation
    
    def update_status(self, issue_id: str, status: IssueStatus) -> None:
        """Update Jira issue status."""
        # Implementation
```

### Domain Event Sourcing (Future)

For full audit trail and replay capability:

```python
# Event store for issue lifecycle
class IssueCreated(DomainEvent): ...
class IssueAcknowledged(DomainEvent): ...
class RootCauseIdentified(DomainEvent): ...
class FixApplied(DomainEvent): ...
class IssueResolved(DomainEvent): ...

# Rebuild issue state from events
def rebuild_issue(events: List[DomainEvent]) -> IssueReport:
    issue = IssueReport.empty()
    for event in events:
        issue = issue.apply(event)
    return issue
```

---

## References

- **Role 7 Guide**: [ROLE_7_DEBUG_AGENT_GUIDE.md](../governance/roles/ROLE_7_DEBUG_AGENT_GUIDE.md)
- **ADR-017**: [ADR-017-debug-role-architecture.md](../architecture/decisions/ADR-017-debug-role-architecture.md)
- **Issue System**: [OVERSEER_ISSUE_SYSTEM.md](OVERSEER_ISSUE_SYSTEM.md)
- **Handoff Protocol**: [HANDOFF_PROTOCOL.md](../governance/HANDOFF_PROTOCOL.md)
- **Cross-Role Escalation**: [CROSS_ROLE_ESCALATION_MATRIX.md](../governance/CROSS_ROLE_ESCALATION_MATRIX.md)

---

## Support

For questions or issues with Debug Role integration:

1. Check this guide first
2. Review Role 7 Guide for operational procedures
3. Check ADR-017 for architectural rationale
4. Escalate to Overseer (Role 0) if blocked
