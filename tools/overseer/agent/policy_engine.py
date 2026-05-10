"""
Policy Engine

Central policy enforcement for agent governance.
Evaluates actions against loaded policies and returns allow/deny decisions.
"""

from __future__ import annotations

import fnmatch
import os
from dataclasses import dataclass
from enum import Enum
from pathlib import Path
from typing import Any

from .identity import AgentIdentity, AgentRole
from .policy_loader import PolicyLoader


class PolicyDecision(str, Enum):
    """Result of a policy evaluation."""

    ALLOW = "allow"
    DENY = "deny"
    REQUIRE_APPROVAL = "require_approval"


@dataclass
class PolicyResult:
    """
    Result of a policy evaluation.

    Attributes:
        decision: The policy decision
        risk_tier: Risk tier of the action
        reason: Human-readable reason for the decision
        requires_approval: Whether approval is required
        violations: List of policy violations
    """

    decision: PolicyDecision
    risk_tier: str
    reason: str
    requires_approval: bool = False
    violations: list[str] | None = None

    def __post_init__(self):
        if self.violations is None:
            self.violations = []

    @property
    def is_allowed(self) -> bool:
        """Check if action is allowed (possibly with approval)."""
        return self.decision in (PolicyDecision.ALLOW, PolicyDecision.REQUIRE_APPROVAL)

    @property
    def is_denied(self) -> bool:
        """Check if action is denied."""
        return self.decision == PolicyDecision.DENY


class PolicyEngine:
    """
    Central policy enforcement engine.

    Evaluates agent actions against loaded policies and determines
    whether to allow, deny, or require approval.
    """

    def __init__(
        self,
        policy_name: str = "base_policy",
        loader: PolicyLoader | None = None,
    ):
        """
        Initialize the policy engine.

        Args:
            policy_name: Name of the policy to load
            loader: Policy loader instance
        """
        self._loader = loader or PolicyLoader()
        self._policy = self._loader.load(policy_name)
        self._policy_name = policy_name

        # Snapshot for pattern expansion; refreshed on each evaluate() so tests or
        # callers that update os.environ (e.g. PROJECT_ROOT) see current values.
        self._env_vars: dict[str, str] = {}
        self._sync_env_vars_from_os()

    def _sync_env_vars_from_os(self) -> None:
        """Refresh path-related variables from os.environ for policy matching."""
        self._env_vars.update(
            {
                "PROJECT_ROOT": os.environ.get("PROJECT_ROOT", os.getcwd()),
                "APPDATA": os.environ.get("APPDATA", os.path.expanduser("~")),
                "TEMP": os.environ.get("TEMP", "/tmp"),
                "PROGRAMFILES": os.environ.get("PROGRAMFILES", "C:\\Program Files"),
                "PROGRAMFILES(X86)": os.environ.get(
                    "PROGRAMFILES(X86)", "C:\\Program Files (x86)"
                ),
                "WINDIR": os.environ.get("WINDIR", "C:\\Windows"),
            }
        )

    def reload(self, policy_name: str | None = None) -> None:
        """Reload the policy from disk."""
        name = policy_name or self._policy_name
        self._policy = self._loader.load(name)
        self._policy_name = name

    def evaluate(
        self,
        agent: AgentIdentity,
        tool_name: str,
        parameters: dict[str, Any],
    ) -> PolicyResult:
        """
        Evaluate an action against the policy.

        Args:
            agent: The agent requesting the action
            tool_name: Name of the tool to execute
            parameters: Tool parameters

        Returns:
            PolicyResult with decision and details
        """
        self._sync_env_vars_from_os()

        violations = []

        # Get tool restrictions
        tool_config = self._get_tool_config(tool_name, agent.role)

        if tool_config is None:
            # Tool not in policy - use default risk tier
            default_tier = self._policy.get("default_risk_tier", "medium")
            tier_config = self._policy.get("risk_tiers", {}).get(default_tier, {})
            requires_approval = tier_config.get("requires_approval", True)

            if requires_approval:
                return PolicyResult(
                    decision=PolicyDecision.REQUIRE_APPROVAL,
                    risk_tier=default_tier,
                    reason=f"Tool '{tool_name}' not in policy, requires approval",
                    requires_approval=True,
                )
            else:
                return PolicyResult(
                    decision=PolicyDecision.ALLOW,
                    risk_tier=default_tier,
                    reason=f"Tool '{tool_name}' allowed by default policy",
                )

        # Get risk tier
        risk_tier = tool_config.get("risk_tier", "medium")
        tier_config = self._policy.get("risk_tiers", {}).get(risk_tier, {})

        # Check safe zones first
        safe_zone_violation = self._check_safe_zones(tool_name, parameters)
        if safe_zone_violation:
            return PolicyResult(
                decision=PolicyDecision.DENY,
                risk_tier="critical",
                reason=f"Safe zone violation: {safe_zone_violation}",
                violations=[safe_zone_violation],
            )

        # Check path restrictions
        path_violations = self._check_path_restrictions(tool_config, parameters)
        violations.extend(path_violations)

        # Check executable restrictions
        exec_violations = self._check_executable_restrictions(tool_config, parameters)
        violations.extend(exec_violations)

        # Check domain restrictions (for network tools)
        domain_violations = self._check_domain_restrictions(tool_config, parameters)
        violations.extend(domain_violations)

        # Check for denied patterns
        pattern_violations = self._check_denied_patterns(tool_config, parameters)
        violations.extend(pattern_violations)

        # If any violations, deny
        if violations:
            return PolicyResult(
                decision=PolicyDecision.DENY,
                risk_tier=risk_tier,
                reason=f"Policy violations: {len(violations)} issue(s)",
                violations=violations,
            )

        # Check if approval required
        requires_approval = tier_config.get("requires_approval", False)

        if requires_approval:
            return PolicyResult(
                decision=PolicyDecision.REQUIRE_APPROVAL,
                risk_tier=risk_tier,
                reason=f"Tool '{tool_name}' requires approval (risk tier: {risk_tier})",
                requires_approval=True,
            )

        return PolicyResult(
            decision=PolicyDecision.ALLOW,
            risk_tier=risk_tier,
            reason=f"Tool '{tool_name}' allowed by policy",
        )

    def _get_tool_config(
        self,
        tool_name: str,
        role: AgentRole,
    ) -> dict[str, Any] | None:
        """Get tool configuration with role overrides applied."""
        # Base tool config
        base_config: dict[str, Any] | None = self._policy.get("tool_restrictions", {}).get(tool_name)

        if base_config is None:
            return None

        # Check for role override
        role_overrides = self._policy.get("role_overrides", {}).get(role.value, {})
        role_tool_config = role_overrides.get("tool_restrictions", {}).get(tool_name)

        if role_tool_config:
            # Merge role override with base
            merged: dict[str, Any] = base_config.copy()
            merged.update(role_tool_config)
            return merged

        return base_config

    def _expand_path(self, pattern: str) -> str:
        """Expand environment variables in a path pattern."""
        result = pattern
        for var, value in self._env_vars.items():
            result = result.replace(f"${{{var}}}", value)
        return result

    def _match_path(self, path: str, pattern: str) -> bool:
        """Check if path matches a glob pattern."""
        expanded_pattern = self._expand_path(pattern)

        # Normalize paths
        path_n = path.replace("\\", "/")
        pat = expanded_pattern.replace("\\", "/")

        # Overseer-style allow-all
        if pat == "**":
            return True

        # Deny / cross-root patterns from base_policy.yaml (before generic PREFIX/**)
        if pat == "**/.env":
            return path_n.endswith("/.env") or path_n == ".env"
        if pat == "**/.env.*":
            leaf = path_n.rsplit("/", 1)[-1]
            return leaf.startswith(".env.") and leaf != ".env"
        if pat == "**/secrets/**":
            return "/secrets/" in path_n or path_n.startswith("secrets/")
        if pat == "**/credentials/**":
            return "/credentials/" in path_n or path_n.startswith("credentials/")
        if pat == "**/config/api-keys.json":
            return path_n.endswith("/config/api-keys.json")
        for ext in (".exe", ".dll", ".sys", ".key", ".pem"):
            if pat == f"**/*{ext}":
                return path_n.lower().endswith(ext)

        # Any path under a directory prefix (covers ${ROOT}/**, ${ROOT}/src/**, etc.)
        # Not for patterns starting with **/ — those are handled above.
        if pat.endswith("/**") and not pat.startswith("**/"):
            root = pat[:-2].rstrip("/")
            if not root:
                return True
            return path_n == root or path_n.startswith(root + "/")

        if "**" in pat:
            # Conservative: unknown ** shape — avoid the old buggy regex
            return False

        return fnmatch.fnmatch(path_n, pat)

    def _check_path_restrictions(
        self,
        tool_config: dict[str, Any],
        parameters: dict[str, Any],
    ) -> list[str]:
        """Check path parameters against allowed/denied lists."""
        violations: list[str] = []

        # Get path parameter (could be 'path', 'file', 'target', etc.)
        path = None
        for key in ("path", "file", "target", "source", "destination"):
            if key in parameters:
                path = parameters[key]
                break

        if path is None:
            return violations

        # Normalize path
        path = str(path).replace("\\", "/")

        # Check denied paths first
        denied_paths = tool_config.get("denied_paths", [])
        for pattern in denied_paths:
            if self._match_path(path, pattern):
                violations.append(f"Path '{path}' matches denied pattern '{pattern}'")
                return violations  # No need to check allowed if denied

        # Check allowed paths
        allowed_paths = tool_config.get("allowed_paths", [])
        if allowed_paths:
            matched = False
            for pattern in allowed_paths:
                if self._match_path(path, pattern):
                    matched = True
                    break

            if not matched:
                violations.append(f"Path '{path}' not in allowed paths")

        return violations

    def _check_executable_restrictions(
        self,
        tool_config: dict[str, Any],
        parameters: dict[str, Any],
    ) -> list[str]:
        """Check executable against allowed/denied lists."""
        violations: list[str] = []

        # Get executable parameter
        exe = parameters.get("exe") or parameters.get("executable") or parameters.get("command")
        if exe is None:
            return violations

        # Extract just the executable name
        exe_name = Path(exe).stem.lower()

        # Check denied executables
        denied = tool_config.get("denied_executables", [])
        for denied_exe in denied:
            if exe_name == denied_exe.lower():
                violations.append(f"Executable '{exe}' is denied")
                return violations

        # Check allowed executables
        allowed = tool_config.get("allowed_executables", [])
        if allowed:
            allowed_lower = [a.lower() for a in allowed]
            if exe_name not in allowed_lower:
                violations.append(f"Executable '{exe}' not in allowed list")

        # Check argument patterns
        args = parameters.get("args") or parameters.get("arguments") or ""
        if isinstance(args, list):
            args = " ".join(args)

        denied_patterns = tool_config.get("denied_args_patterns", [])
        for pattern in denied_patterns:
            if pattern in args:
                violations.append(f"Arguments contain denied pattern '{pattern}'")

        return violations

    def _check_domain_restrictions(
        self,
        tool_config: dict[str, Any],
        parameters: dict[str, Any],
    ) -> list[str]:
        """Check domain/URL against allowed/denied lists."""
        violations: list[str] = []

        # Get URL or domain
        url = parameters.get("url") or parameters.get("domain") or parameters.get("host")
        if url is None:
            return violations

        # Extract domain from URL
        from urllib.parse import urlparse
        try:
            parsed = urlparse(url)
            domain = parsed.netloc or url
        except Exception:
            domain = url

        # Check denied domains
        denied = tool_config.get("denied_domains", [])
        for denied_domain in denied:
            if denied_domain == "*":
                # Wildcard deny - only allowed domains pass
                pass
            elif domain == denied_domain or domain.endswith(f".{denied_domain}"):
                violations.append(f"Domain '{domain}' is denied")
                return violations

        # Check allowed domains
        allowed = tool_config.get("allowed_domains", [])
        if allowed:
            matched = False
            for allowed_domain in allowed:
                if domain == allowed_domain or domain.endswith(f".{allowed_domain}"):
                    matched = True
                    break

            if not matched and "*" in denied:
                violations.append(f"Domain '{domain}' not in allowed list")

        return violations

    def _check_denied_patterns(
        self,
        tool_config: dict[str, Any],
        parameters: dict[str, Any],
    ) -> list[str]:
        """Check parameters for denied patterns (e.g., SQL injection)."""
        violations: list[str] = []

        denied_patterns = tool_config.get("denied_patterns", [])
        if not denied_patterns:
            return violations

        # Check all string parameters
        def check_value(value: Any, key: str) -> None:
            if isinstance(value, str):
                for pattern in denied_patterns:
                    if pattern.upper() in value.upper():
                        violations.append(
                            f"Parameter '{key}' contains denied pattern '{pattern}'"
                        )
            elif isinstance(value, dict):
                for k, v in value.items():
                    check_value(v, f"{key}.{k}")
            elif isinstance(value, list):
                for i, item in enumerate(value):
                    check_value(item, f"{key}[{i}]")

        for key, value in parameters.items():
            check_value(value, key)

        return violations

    def _check_safe_zones(
        self,
        tool_name: str,
        parameters: dict[str, Any],
    ) -> str | None:
        """Check if action touches a safe zone."""
        safe_zones = self._policy.get("safe_zones", {})
        protected_paths = safe_zones.get("paths", [])

        if not protected_paths:
            return None

        # Get any path parameter
        path = None
        for key in ("path", "file", "target", "source", "destination"):
            if key in parameters:
                path = str(parameters[key]).replace("\\", "/")
                break

        if path is None:
            return None

        for protected in protected_paths:
            expanded = self._expand_path(protected).replace("\\", "/")
            if self._match_path(path, expanded):
                return f"Path '{path}' is in protected safe zone"

        return None

    def get_risk_tier(self, tool_name: str, role: AgentRole) -> str:
        """Get the risk tier for a tool."""
        tool_config = self._get_tool_config(tool_name, role)
        if tool_config:
            return str(tool_config.get("risk_tier", "medium"))
        return str(self._policy.get("default_risk_tier", "medium"))

    def requires_approval(self, tool_name: str, role: AgentRole) -> bool:
        """Check if a tool requires approval."""
        risk_tier = self.get_risk_tier(tool_name, role)
        tier_config = self._policy.get("risk_tiers", {}).get(risk_tier, {})
        return bool(tier_config.get("requires_approval", False))

    def get_circuit_breaker_config(self) -> dict[str, Any]:
        """Get circuit breaker configuration."""
        config: dict[str, Any] = self._policy.get("circuit_breaker", {})
        return config

    def get_audit_config(self) -> dict[str, Any]:
        """Get audit configuration."""
        config: dict[str, Any] = self._policy.get("audit", {})
        return config
