"""
AI-Assisted Code Reviewer for Plugins.

Phase 6B: Combines static analysis with local LLM for comprehensive
code review of plugin submissions.

Review Pipeline:
1. Static Analysis (parallel):
   - Semgrep: Security patterns and custom rules
   - Bandit: Python security issues
   - Ruff: Linting and style
2. AI Analysis (Ollama):
   - Code quality assessment
   - Architecture suggestions
   - Documentation review
3. Scoring and Report:
   - Aggregate quality score
   - Prioritized issues
   - Recommendations

Dependencies:
- semgrep >= 1.50.0 (LGPL-2.1)
- bandit >= 1.7.0 (Apache-2.0)
- ruff >= 0.1.0 (MIT)
- ollama (optional, for AI review)

Usage:
    reviewer = CodeReviewer(ollama_enabled=True)

    result = await reviewer.review_plugin(
        plugin_path=Path("plugins/my-plugin"),
        plugin_id="my-plugin",
        plugin_version="1.0.0",
    )

    print(f"Quality Score: {result.quality_score}/100")
    for issue in result.issues:
        print(f"[{issue.severity}] {issue.message}")
"""

from __future__ import annotations

import asyncio
import json
import logging
import subprocess
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional

logger = logging.getLogger(__name__)


class IssueSeverity(Enum):
    """Severity levels for code issues."""

    CRITICAL = "critical"
    HIGH = "high"
    MEDIUM = "medium"
    LOW = "low"
    INFO = "info"

    @property
    def score_penalty(self) -> int:
        """Score penalty for this severity."""
        penalties = {
            IssueSeverity.CRITICAL: 25,
            IssueSeverity.HIGH: 15,
            IssueSeverity.MEDIUM: 5,
            IssueSeverity.LOW: 2,
            IssueSeverity.INFO: 0,
        }
        return penalties.get(self, 0)


class IssueCategory(Enum):
    """Categories for code issues."""

    SECURITY = "security"
    PERFORMANCE = "performance"
    STYLE = "style"
    MAINTAINABILITY = "maintainability"
    DOCUMENTATION = "documentation"
    COMPATIBILITY = "compatibility"


@dataclass
class CodeIssue:
    """
    A code issue found during review.

    Attributes:
        severity: Issue severity
        category: Issue category
        message: Human-readable description
        file: File path relative to plugin root
        line: Line number (1-indexed)
        column: Column number (1-indexed)
        rule_id: Rule identifier from tool
        tool: Tool that found the issue
        suggestion: Fix suggestion if available
    """

    severity: IssueSeverity
    category: IssueCategory
    message: str
    file: str
    line: int = 0
    column: int = 0
    rule_id: str = ""
    tool: str = ""
    suggestion: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "severity": self.severity.value,
            "category": self.category.value,
            "message": self.message,
            "file": self.file,
            "line": self.line,
            "column": self.column,
            "rule_id": self.rule_id,
            "tool": self.tool,
            "suggestion": self.suggestion,
        }


@dataclass
class ReviewResult:
    """
    Complete review result for a plugin.

    Attributes:
        plugin_id: Plugin identifier
        plugin_version: Plugin version
        quality_score: Overall quality score (0-100)
        issues: List of found issues
        ai_summary: AI-generated summary
        recommendations: AI recommendations
        reviewed_at: Review timestamp
        review_duration_ms: Time taken for review
    """

    plugin_id: str
    plugin_version: str
    quality_score: int
    issues: List[CodeIssue] = field(default_factory=list)
    ai_summary: str = ""
    recommendations: List[str] = field(default_factory=list)
    reviewed_at: datetime = field(default_factory=datetime.utcnow)
    review_duration_ms: float = 0.0
    tools_used: List[str] = field(default_factory=list)

    @property
    def critical_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == IssueSeverity.CRITICAL)

    @property
    def high_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == IssueSeverity.HIGH)

    @property
    def passed(self) -> bool:
        """Check if plugin passed review (no critical issues)."""
        return self.critical_count == 0

    def to_dict(self) -> Dict[str, Any]:
        return {
            "plugin_id": self.plugin_id,
            "plugin_version": self.plugin_version,
            "quality_score": self.quality_score,
            "passed": self.passed,
            "critical_count": self.critical_count,
            "high_count": self.high_count,
            "issue_count": len(self.issues),
            "issues": [i.to_dict() for i in self.issues],
            "ai_summary": self.ai_summary,
            "recommendations": self.recommendations,
            "reviewed_at": self.reviewed_at.isoformat(),
            "review_duration_ms": self.review_duration_ms,
            "tools_used": self.tools_used,
        }


class CodeReviewer:
    """
    AI-assisted code reviewer for plugin submissions.

    Combines static analysis tools with local LLM for comprehensive
    code review. All operations run locally.

    Example:
        reviewer = CodeReviewer(ollama_enabled=True)
        result = await reviewer.review_plugin(
            plugin_path=Path("plugins/audio-fx"),
            plugin_id="audio-fx",
            plugin_version="1.0.0",
        )

        if result.passed:
            print("Plugin approved!")
        else:
            print(f"Found {result.critical_count} critical issues")
    """

    def __init__(
        self,
        ollama_enabled: bool = True,
        ollama_model: str = "codellama:13b",
        ollama_timeout: int = 120,
        semgrep_rules: Optional[List[str]] = None,
        bandit_severity: str = "medium",
        ruff_select: Optional[List[str]] = None,
    ):
        """
        Initialize code reviewer.

        Args:
            ollama_enabled: Enable Ollama AI review
            ollama_model: Ollama model to use
            ollama_timeout: Timeout for Ollama calls
            semgrep_rules: Semgrep rule sets to use
            bandit_severity: Minimum Bandit severity
            ruff_select: Ruff rule codes to enable
        """
        self._ollama_enabled = ollama_enabled
        self._ollama_model = ollama_model
        self._ollama_timeout = ollama_timeout
        self._semgrep_rules = semgrep_rules or ["p/python", "p/security-audit"]
        self._bandit_severity = bandit_severity
        self._ruff_select = ruff_select or ["E", "F", "W", "S", "B"]

        # Check tool availability
        self._semgrep_available = self._check_tool("semgrep")
        self._bandit_available = self._check_tool("bandit")
        self._ruff_available = self._check_tool("ruff")
        self._ollama_available = self._check_tool("ollama")

    def _check_tool(self, tool: str) -> bool:
        """Check if a tool is available."""
        try:
            subprocess.run(
                [tool, "--version"],
                capture_output=True,
                timeout=5,
            )
            return True
        except (subprocess.SubprocessError, FileNotFoundError):
            logger.debug(f"Tool not available: {tool}")
            return False

    async def review_plugin(
        self,
        plugin_path: Path,
        plugin_id: str,
        plugin_version: str,
    ) -> ReviewResult:
        """
        Perform comprehensive code review of a plugin.

        Args:
            plugin_path: Path to plugin directory
            plugin_id: Plugin identifier
            plugin_version: Plugin version

        Returns:
            ReviewResult with issues and score
        """
        import time

        start_time = time.perf_counter()

        issues: List[CodeIssue] = []
        tools_used: List[str] = []

        # Run static analysis tools in parallel
        tasks = []

        if self._semgrep_available:
            tasks.append(self._run_semgrep(plugin_path))
            tools_used.append("semgrep")

        if self._bandit_available:
            tasks.append(self._run_bandit(plugin_path))
            tools_used.append("bandit")

        if self._ruff_available:
            tasks.append(self._run_ruff(plugin_path))
            tools_used.append("ruff")

        # Gather static analysis results
        if tasks:
            results = await asyncio.gather(*tasks, return_exceptions=True)
            for result in results:
                if isinstance(result, list):
                    issues.extend(result)
                elif isinstance(result, Exception):
                    logger.error(f"Static analysis error: {result}")

        # Run AI review if enabled
        ai_summary = ""
        recommendations = []

        if self._ollama_enabled and self._ollama_available:
            try:
                ai_result = await self._run_ollama_review(plugin_path)
                ai_summary = ai_result.get("summary", "")
                recommendations = ai_result.get("recommendations", [])
                tools_used.append("ollama")
            except Exception as e:
                logger.error(f"Ollama review failed: {e}")

        # Calculate quality score
        quality_score = self._calculate_score(issues)

        duration_ms = (time.perf_counter() - start_time) * 1000

        return ReviewResult(
            plugin_id=plugin_id,
            plugin_version=plugin_version,
            quality_score=quality_score,
            issues=sorted(issues, key=lambda i: i.severity.score_penalty, reverse=True),
            ai_summary=ai_summary,
            recommendations=recommendations,
            review_duration_ms=duration_ms,
            tools_used=tools_used,
        )

    async def _run_semgrep(self, plugin_path: Path) -> List[CodeIssue]:
        """Run Semgrep security analysis."""
        issues = []

        try:
            # Build command
            cmd = [
                "semgrep",
                "--json",
                "--config",
                ",".join(self._semgrep_rules),
                str(plugin_path),
            ]

            # Run semgrep
            proc = await asyncio.create_subprocess_exec(
                *cmd,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, stderr = await asyncio.wait_for(
                proc.communicate(),
                timeout=60,
            )

            if stdout:
                data = json.loads(stdout.decode())
                for result in data.get("results", []):
                    severity = self._map_semgrep_severity(
                        result.get("extra", {}).get("severity", "WARNING")
                    )
                    issues.append(
                        CodeIssue(
                            severity=severity,
                            category=IssueCategory.SECURITY,
                            message=result.get("extra", {}).get("message", ""),
                            file=result.get("path", ""),
                            line=result.get("start", {}).get("line", 0),
                            column=result.get("start", {}).get("col", 0),
                            rule_id=result.get("check_id", ""),
                            tool="semgrep",
                        )
                    )

        except asyncio.TimeoutError:
            logger.warning("Semgrep timed out")
        except Exception as e:
            logger.error(f"Semgrep error: {e}")

        return issues

    async def _run_bandit(self, plugin_path: Path) -> List[CodeIssue]:
        """Run Bandit security analysis."""
        issues = []

        try:
            cmd = [
                "bandit",
                "-r",
                "-f",
                "json",
                "-ll" if self._bandit_severity == "low" else "-l",
                str(plugin_path),
            ]

            proc = await asyncio.create_subprocess_exec(
                *cmd,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, _ = await asyncio.wait_for(
                proc.communicate(),
                timeout=60,
            )

            if stdout:
                data = json.loads(stdout.decode())
                for result in data.get("results", []):
                    severity = self._map_bandit_severity(result.get("issue_severity", "MEDIUM"))
                    issues.append(
                        CodeIssue(
                            severity=severity,
                            category=IssueCategory.SECURITY,
                            message=result.get("issue_text", ""),
                            file=result.get("filename", ""),
                            line=result.get("line_number", 0),
                            rule_id=result.get("test_id", ""),
                            tool="bandit",
                        )
                    )

        except asyncio.TimeoutError:
            logger.warning("Bandit timed out")
        except Exception as e:
            logger.error(f"Bandit error: {e}")

        return issues

    async def _run_ruff(self, plugin_path: Path) -> List[CodeIssue]:
        """Run Ruff linting."""
        issues = []

        try:
            select_arg = ",".join(self._ruff_select)
            cmd = [
                "ruff",
                "check",
                "--output-format",
                "json",
                "--select",
                select_arg,
                str(plugin_path),
            ]

            proc = await asyncio.create_subprocess_exec(
                *cmd,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, _ = await asyncio.wait_for(
                proc.communicate(),
                timeout=30,
            )

            if stdout:
                results = json.loads(stdout.decode())
                for result in results:
                    severity = self._map_ruff_severity(result.get("code", ""))
                    category = (
                        IssueCategory.SECURITY
                        if result.get("code", "").startswith("S")
                        else IssueCategory.STYLE
                    )

                    issues.append(
                        CodeIssue(
                            severity=severity,
                            category=category,
                            message=result.get("message", ""),
                            file=result.get("filename", ""),
                            line=result.get("location", {}).get("row", 0),
                            column=result.get("location", {}).get("column", 0),
                            rule_id=result.get("code", ""),
                            tool="ruff",
                            suggestion=result.get("fix", {}).get("message"),
                        )
                    )

        except asyncio.TimeoutError:
            logger.warning("Ruff timed out")
        except Exception as e:
            logger.error(f"Ruff error: {e}")

        return issues

    async def _run_ollama_review(self, plugin_path: Path) -> Dict[str, Any]:
        """Run Ollama AI code review."""
        # Collect Python files
        code_samples = []
        for py_file in plugin_path.rglob("*.py"):
            try:
                content = py_file.read_text(encoding="utf-8")
                if len(content) < 10000:  # Limit size
                    code_samples.append(f"# {py_file.name}\n{content}")
            except (OSError, UnicodeDecodeError) as e:
                # Specific exceptions for file reading failures - acceptable to skip
                logger.warning(f"Skipping {py_file} for code review: {e}")

        if not code_samples:
            return {"summary": "No Python files found", "recommendations": []}

        # Build prompt
        code_context = "\n\n".join(code_samples[:5])  # Limit files
        prompt = f"""Review this VoiceStudio plugin code for quality, security, and best practices.

Code:
```python
{code_context}
```

Provide:
1. A brief summary (2-3 sentences)
2. Top 3 recommendations for improvement

Format as JSON:
{{"summary": "...", "recommendations": ["...", "...", "..."]}}
"""

        try:
            cmd = [
                "ollama",
                "run",
                self._ollama_model,
                prompt,
            ]

            proc = await asyncio.create_subprocess_exec(
                *cmd,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, _ = await asyncio.wait_for(
                proc.communicate(),
                timeout=self._ollama_timeout,
            )

            response = stdout.decode().strip()

            # Try to parse JSON from response
            try:
                # Find JSON in response
                start = response.find("{")
                end = response.rfind("}") + 1
                if start >= 0 and end > start:
                    return json.loads(response[start:end])
            except json.JSONDecodeError as e:
                # GAP-PY-001: AI response wasn't valid JSON, fallback to raw
                logger.debug(f"Failed to parse AI response as JSON: {e}")

            return {"summary": response[:500], "recommendations": []}

        except asyncio.TimeoutError:
            logger.warning("Ollama timed out")
            return {"summary": "AI review timed out", "recommendations": []}
        except Exception as e:
            logger.error(f"Ollama error: {e}")
            return {"summary": f"AI review failed: {e}", "recommendations": []}

    def _calculate_score(self, issues: List[CodeIssue]) -> int:
        """Calculate quality score from issues."""
        score = 100

        for issue in issues:
            score -= issue.severity.score_penalty

        return max(0, min(100, score))

    def _map_semgrep_severity(self, severity: str) -> IssueSeverity:
        """Map Semgrep severity to our severity."""
        mapping = {
            "ERROR": IssueSeverity.HIGH,
            "WARNING": IssueSeverity.MEDIUM,
            "INFO": IssueSeverity.INFO,
        }
        return mapping.get(severity.upper(), IssueSeverity.MEDIUM)

    def _map_bandit_severity(self, severity: str) -> IssueSeverity:
        """Map Bandit severity to our severity."""
        mapping = {
            "HIGH": IssueSeverity.HIGH,
            "MEDIUM": IssueSeverity.MEDIUM,
            "LOW": IssueSeverity.LOW,
        }
        return mapping.get(severity.upper(), IssueSeverity.MEDIUM)

    def _map_ruff_severity(self, code: str) -> IssueSeverity:
        """Map Ruff rule code to severity."""
        if code.startswith("S"):  # Security
            return IssueSeverity.HIGH
        elif code.startswith(("E", "F")):  # Errors
            return IssueSeverity.MEDIUM
        elif code.startswith("W"):  # Warnings
            return IssueSeverity.LOW
        else:
            return IssueSeverity.INFO
