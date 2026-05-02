"""
Policy Loader

Loads and validates policy files from YAML/JSON.
"""

from __future__ import annotations

import os
import re
from pathlib import Path
from typing import Any

import yaml


class PolicyValidationError(Exception):
    """Raised when policy validation fails."""
    pass


class PolicyLoader:
    """
    Loads and validates agent governance policies.

    Supports YAML and JSON formats with environment variable expansion.
    Path-related placeholders (${PROJECT_ROOT}, ${TEMP}, etc.) stay literal in
    the loaded document so PolicyEngine resolves them at evaluation time.
    """

    # Resolved at policy evaluation, not at YAML load (tests set PROJECT_ROOT).
    DEFERRED_ENV_PLACEHOLDERS: frozenset[str] = frozenset(
        {
            "PROJECT_ROOT",
            "APPDATA",
            "TEMP",
            "PROGRAMFILES",
            "PROGRAMFILES(X86)",
            "WINDIR",
        }
    )

    # Required top-level keys
    REQUIRED_KEYS = {"version", "risk_tiers", "tool_restrictions"}

    # Valid risk tier names
    VALID_RISK_TIERS = {"low", "medium", "high", "critical"}

    def __init__(self, policies_dir: Path | None = None):
        """
        Initialize the policy loader.

        Args:
            policies_dir: Directory containing policy files.
                         Defaults to tools/overseer/agent/policies/
        """
        if policies_dir:
            self._policies_dir = policies_dir
        else:
            self._policies_dir = Path(__file__).parent / "policies"

        self._policies_dir.mkdir(parents=True, exist_ok=True)
        self._env_cache: dict[str, str] = {}

    def _expand_env_vars(self, value: Any) -> Any:
        """
        Expand environment variables in strings.

        Supports ${VAR} syntax.
        """
        if isinstance(value, str):
            # Find all ${VAR} patterns
            pattern = r'\$\{([^}]+)\}'

            def replace(match: re.Match[str]) -> str:
                var_name = match.group(1)
                if var_name in self.DEFERRED_ENV_PLACEHOLDERS:
                    return match.group(0)
                if var_name not in self._env_cache:
                    self._env_cache[var_name] = os.environ.get(var_name, "")
                return self._env_cache[var_name]

            return re.sub(pattern, replace, value)

        elif isinstance(value, dict):
            return {k: self._expand_env_vars(v) for k, v in value.items()}

        elif isinstance(value, list):
            return [self._expand_env_vars(item) for item in value]

        return value

    def load(self, policy_name: str = "base_policy") -> dict[str, Any]:
        """
        Load a policy by name.

        Args:
            policy_name: Name of the policy file (without extension)

        Returns:
            The parsed and validated policy dictionary

        Raises:
            FileNotFoundError: If policy file not found
            PolicyValidationError: If policy is invalid
        """
        # Try YAML first, then JSON
        yaml_path = self._policies_dir / f"{policy_name}.yaml"
        json_path = self._policies_dir / f"{policy_name}.json"

        if yaml_path.exists():
            policy_path = yaml_path
        elif json_path.exists():
            policy_path = json_path
        else:
            raise FileNotFoundError(f"Policy not found: {policy_name}")

        return self.load_file(policy_path)

    def load_file(self, path: Path) -> dict[str, Any]:
        """
        Load a policy from a specific file.

        Args:
            path: Path to the policy file

        Returns:
            The parsed and validated policy dictionary
        """
        content = path.read_text(encoding="utf-8")

        if path.suffix in (".yaml", ".yml"):
            policy = yaml.safe_load(content)
        else:
            import json
            policy = json.loads(content)

        # Expand environment variables
        policy = self._expand_env_vars(policy)

        # Validate
        self.validate(policy)

        result: dict[str, Any] = policy
        return result

    def validate(self, policy: dict[str, Any]) -> None:
        """
        Validate a policy dictionary.

        Args:
            policy: The policy to validate

        Raises:
            PolicyValidationError: If validation fails
        """
        errors = []

        # Check required keys
        missing_keys = self.REQUIRED_KEYS - set(policy.keys())
        if missing_keys:
            errors.append(f"Missing required keys: {missing_keys}")

        # Validate version
        if "version" in policy and not isinstance(policy["version"], str):
            errors.append("version must be a string")

        # Validate risk tiers
        if "risk_tiers" in policy:
            risk_tiers = policy["risk_tiers"]
            if not isinstance(risk_tiers, dict):
                errors.append("risk_tiers must be a dictionary")
            else:
                for tier_name, tier_config in risk_tiers.items():
                    if tier_name not in self.VALID_RISK_TIERS:
                        errors.append(f"Invalid risk tier: {tier_name}")
                    if not isinstance(tier_config, dict):
                        errors.append(f"risk_tiers.{tier_name} must be a dictionary")
                    elif "requires_approval" not in tier_config:
                        errors.append(f"risk_tiers.{tier_name} missing requires_approval")

        # Validate tool restrictions
        if "tool_restrictions" in policy:
            tool_restrictions = policy["tool_restrictions"]
            if not isinstance(tool_restrictions, dict):
                errors.append("tool_restrictions must be a dictionary")
            else:
                for tool_name, tool_config in tool_restrictions.items():
                    if not isinstance(tool_config, dict):
                        errors.append(f"tool_restrictions.{tool_name} must be a dictionary")
                    else:
                        # Validate risk tier reference
                        if "risk_tier" in tool_config:
                            tier = tool_config["risk_tier"]
                            if tier not in self.VALID_RISK_TIERS:
                                errors.append(
                                    f"tool_restrictions.{tool_name}.risk_tier "
                                    f"references invalid tier: {tier}"
                                )

                        # Validate path patterns
                        for key in ("allowed_paths", "denied_paths"):
                            if key in tool_config:
                                if not isinstance(tool_config[key], list):
                                    errors.append(
                                        f"tool_restrictions.{tool_name}.{key} "
                                        "must be a list"
                                    )

        # Validate circuit breaker
        if "circuit_breaker" in policy:
            cb = policy["circuit_breaker"]
            if not isinstance(cb, dict):
                errors.append("circuit_breaker must be a dictionary")
            else:
                for key in ("denied_action_threshold", "failure_threshold"):
                    if key in cb and not isinstance(cb[key], int):
                        errors.append(f"circuit_breaker.{key} must be an integer")

        if errors:
            raise PolicyValidationError(
                f"Policy validation failed with {len(errors)} error(s):\n"
                + "\n".join(f"  - {e}" for e in errors)
            )

    def merge_policies(
        self,
        base: dict[str, Any],
        override: dict[str, Any],
    ) -> dict[str, Any]:
        """
        Merge two policies with override taking precedence.

        Args:
            base: Base policy
            override: Override policy

        Returns:
            Merged policy
        """
        result = self._deep_merge(base, override)
        self.validate(result)
        return result

    def _deep_merge(
        self,
        base: dict[str, Any],
        override: dict[str, Any],
    ) -> dict[str, Any]:
        """Deep merge two dictionaries."""
        result = base.copy()

        for key, value in override.items():
            if key in result and isinstance(result[key], dict) and isinstance(value, dict):
                result[key] = self._deep_merge(result[key], value)
            else:
                result[key] = value

        return result

    def list_policies(self) -> list[str]:
        """List available policy names."""
        policies = []
        for path in self._policies_dir.glob("*.yaml"):
            policies.append(path.stem)
        for path in self._policies_dir.glob("*.json"):
            if path.stem not in policies:
                policies.append(path.stem)
        return sorted(policies)
