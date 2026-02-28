"""
Plugin Dependency Resolver.

Phase 5C M6: Topological dependency resolution and conflict detection.

Provides dependency resolution for plugin installations including:
- Topological sorting of dependencies
- Version constraint satisfaction
- Conflict detection and reporting
- Resolution suggestions
- Circular dependency detection
"""

from __future__ import annotations

import logging
import re
from collections import defaultdict
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Callable

logger = logging.getLogger(__name__)


class VersionOperator(Enum):
    """Version comparison operators."""

    EQ = "=="  # Exact match
    NE = "!="  # Not equal
    GT = ">"  # Greater than
    GE = ">="  # Greater than or equal
    LT = "<"  # Less than
    LE = "<="  # Less than or equal
    COMPATIBLE = "~="  # Compatible release
    CARET = "^"  # Compatible (npm-style)


@dataclass
class Version:
    """Semantic version representation."""

    major: int
    minor: int
    patch: int
    prerelease: str = ""
    build: str = ""

    @classmethod
    def parse(cls, version_str: str) -> Version:
        """Parse a version string (e.g., '1.2.3', '1.2.3-beta.1')."""
        version_str = version_str.strip().lstrip("v")

        # Handle prerelease and build metadata
        prerelease = ""
        build = ""

        if "+" in version_str:
            version_str, build = version_str.split("+", 1)
        if "-" in version_str:
            version_str, prerelease = version_str.split("-", 1)

        parts = version_str.split(".")
        major = int(parts[0]) if len(parts) > 0 and parts[0].isdigit() else 0
        minor = int(parts[1]) if len(parts) > 1 and parts[1].isdigit() else 0
        patch = int(parts[2]) if len(parts) > 2 and parts[2].isdigit() else 0

        return cls(major, minor, patch, prerelease, build)

    def __str__(self) -> str:
        """Convert to string."""
        version = f"{self.major}.{self.minor}.{self.patch}"
        if self.prerelease:
            version += f"-{self.prerelease}"
        if self.build:
            version += f"+{self.build}"
        return version

    def to_tuple(self) -> tuple[int, int, int, str]:
        """Convert to comparable tuple."""
        # Empty prerelease is higher than any prerelease
        pre = self.prerelease if self.prerelease else "~"
        return (self.major, self.minor, self.patch, pre)

    def __lt__(self, other: Version) -> bool:
        return self.to_tuple() < other.to_tuple()

    def __le__(self, other: Version) -> bool:
        return self.to_tuple() <= other.to_tuple()

    def __gt__(self, other: Version) -> bool:
        return self.to_tuple() > other.to_tuple()

    def __ge__(self, other: Version) -> bool:
        return self.to_tuple() >= other.to_tuple()

    def __eq__(self, other: object) -> bool:
        if not isinstance(other, Version):
            return False
        return self.to_tuple() == other.to_tuple()

    def __hash__(self) -> int:
        return hash(self.to_tuple())


@dataclass
class VersionConstraint:
    """A version constraint (e.g., '>=1.0.0', '^2.0.0')."""

    operator: VersionOperator
    version: Version

    @classmethod
    def parse(cls, constraint_str: str) -> VersionConstraint:
        """Parse a constraint string."""
        constraint_str = constraint_str.strip()

        # Detect operator
        if constraint_str.startswith("~="):
            op = VersionOperator.COMPATIBLE
            version_str = constraint_str[2:]
        elif constraint_str.startswith("^"):
            op = VersionOperator.CARET
            version_str = constraint_str[1:]
        elif constraint_str.startswith(">="):
            op = VersionOperator.GE
            version_str = constraint_str[2:]
        elif constraint_str.startswith("<="):
            op = VersionOperator.LE
            version_str = constraint_str[2:]
        elif constraint_str.startswith("!="):
            op = VersionOperator.NE
            version_str = constraint_str[2:]
        elif constraint_str.startswith("=="):
            op = VersionOperator.EQ
            version_str = constraint_str[2:]
        elif constraint_str.startswith(">"):
            op = VersionOperator.GT
            version_str = constraint_str[1:]
        elif constraint_str.startswith("<"):
            op = VersionOperator.LT
            version_str = constraint_str[1:]
        else:
            # Assume exact match if no operator
            op = VersionOperator.EQ
            version_str = constraint_str

        return cls(operator=op, version=Version.parse(version_str))

    def satisfies(self, version: Version) -> bool:
        """Check if a version satisfies this constraint."""
        if self.operator == VersionOperator.EQ:
            return version == self.version
        elif self.operator == VersionOperator.NE:
            return version != self.version
        elif self.operator == VersionOperator.GT:
            return version > self.version
        elif self.operator == VersionOperator.GE:
            return version >= self.version
        elif self.operator == VersionOperator.LT:
            return version < self.version
        elif self.operator == VersionOperator.LE:
            return version <= self.version
        elif self.operator == VersionOperator.COMPATIBLE:
            # ~= compatible: same major.minor, patch >= constraint
            return (
                version.major == self.version.major
                and version.minor == self.version.minor
                and version >= self.version
            )
        elif self.operator == VersionOperator.CARET:
            # ^: same major (if major > 0), or same major.minor (if major == 0)
            if self.version.major > 0:
                return version.major == self.version.major and version >= self.version
            else:
                return (
                    version.major == self.version.major
                    and version.minor == self.version.minor
                    and version >= self.version
                )
        return False

    def __str__(self) -> str:
        return f"{self.operator.value}{self.version}"


@dataclass
class VersionSpec:
    """A version specification that may contain multiple constraints."""

    constraints: list[VersionConstraint] = field(default_factory=list)

    @classmethod
    def parse(cls, spec_str: str) -> VersionSpec:
        """Parse a version spec string (e.g., '>=1.0.0,<2.0.0')."""
        constraints = []

        # Split by comma
        parts = [p.strip() for p in spec_str.split(",") if p.strip()]

        for part in parts:
            constraints.append(VersionConstraint.parse(part))

        # Default to any version if no constraints
        if not constraints:
            constraints.append(
                VersionConstraint(
                    operator=VersionOperator.GE,
                    version=Version(0, 0, 0),
                )
            )

        return cls(constraints=constraints)

    def satisfies(self, version: Version) -> bool:
        """Check if a version satisfies all constraints."""
        return all(c.satisfies(version) for c in self.constraints)

    def __str__(self) -> str:
        return ",".join(str(c) for c in self.constraints)


@dataclass
class Dependency:
    """A plugin dependency declaration."""

    plugin_id: str
    version_spec: VersionSpec
    optional: bool = False

    @classmethod
    def parse(cls, plugin_id: str, spec_str: str, optional: bool = False) -> Dependency:
        """Create a dependency from plugin ID and version spec string."""
        return cls(
            plugin_id=plugin_id,
            version_spec=VersionSpec.parse(spec_str),
            optional=optional,
        )

    def __str__(self) -> str:
        opt = " (optional)" if self.optional else ""
        return f"{self.plugin_id} {self.version_spec}{opt}"


class ConflictType(Enum):
    """Type of dependency conflict."""

    VERSION_MISMATCH = "version_mismatch"  # Incompatible version requirements
    CIRCULAR = "circular"  # Circular dependency
    MISSING = "missing"  # Missing required dependency
    MULTIPLE_VERSIONS = "multiple_versions"  # Same plugin, different versions needed


@dataclass
class Conflict:
    """A dependency conflict."""

    conflict_type: ConflictType
    plugins: list[str]
    message: str
    suggestions: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "type": self.conflict_type.value,
            "plugins": self.plugins,
            "message": self.message,
            "suggestions": self.suggestions,
        }


@dataclass
class ResolvedDependency:
    """A resolved dependency with its version."""

    plugin_id: str
    version: Version
    required_by: list[str] = field(default_factory=list)
    optional: bool = False

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "plugin_id": self.plugin_id,
            "version": str(self.version),
            "required_by": self.required_by,
            "optional": self.optional,
        }


@dataclass
class ResolutionResult:
    """Result of dependency resolution."""

    success: bool
    resolved: list[ResolvedDependency] = field(default_factory=list)
    conflicts: list[Conflict] = field(default_factory=list)
    install_order: list[str] = field(default_factory=list)
    message: str = ""

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "success": self.success,
            "resolved": [r.to_dict() for r in self.resolved],
            "conflicts": [c.to_dict() for c in self.conflicts],
            "install_order": self.install_order,
            "message": self.message,
        }


# Type for version provider function
VersionProvider = Callable[[str], list[Version]]


class DependencyResolver:
    """
    Plugin dependency resolver with topological sorting.

    Features:
    - Topological sort for install order
    - Version constraint satisfaction
    - Conflict detection and reporting
    - Circular dependency detection
    - Resolution suggestions
    """

    def __init__(
        self,
        version_provider: VersionProvider | None = None,
    ):
        """
        Initialize the resolver.

        Args:
            version_provider: Function that returns available versions for a plugin ID
        """
        self._version_provider = version_provider or self._default_version_provider

        # Plugin graph: plugin_id -> list of dependencies
        self._graph: dict[str, list[Dependency]] = {}

        # Available versions cache
        self._available_versions: dict[str, list[Version]] = {}

        # Resolved versions
        self._resolved: dict[str, Version] = {}

        # Required by tracking
        self._required_by: dict[str, list[str]] = defaultdict(list)

    @staticmethod
    def _default_version_provider(plugin_id: str) -> list[Version]:
        """Default version provider returns empty list."""
        return []

    def add_plugin(
        self,
        plugin_id: str,
        dependencies: dict[str, str],
        optional_dependencies: dict[str, str] | None = None,
    ) -> None:
        """
        Add a plugin and its dependencies to the graph.

        Args:
            plugin_id: Plugin identifier
            dependencies: Map of dependency ID -> version spec
            optional_dependencies: Map of optional dependency ID -> version spec
        """
        deps = []

        for dep_id, spec_str in dependencies.items():
            deps.append(Dependency.parse(dep_id, spec_str, optional=False))
            self._required_by[dep_id].append(plugin_id)

        for dep_id, spec_str in (optional_dependencies or {}).items():
            deps.append(Dependency.parse(dep_id, spec_str, optional=True))
            self._required_by[dep_id].append(plugin_id)

        self._graph[plugin_id] = deps

    def add_available_version(self, plugin_id: str, version: Version | str) -> None:
        """Add an available version for a plugin."""
        if isinstance(version, str):
            version = Version.parse(version)

        if plugin_id not in self._available_versions:
            self._available_versions[plugin_id] = []

        if version not in self._available_versions[plugin_id]:
            self._available_versions[plugin_id].append(version)
            # Keep sorted, newest first
            self._available_versions[plugin_id].sort(reverse=True)

    def get_available_versions(self, plugin_id: str) -> list[Version]:
        """Get available versions for a plugin."""
        if plugin_id in self._available_versions:
            return self._available_versions[plugin_id]
        return self._version_provider(plugin_id)

    def _detect_circular(
        self,
        plugin_id: str,
        visited: set[str],
        rec_stack: set[str],
        path: list[str],
    ) -> list[str] | None:
        """
        Detect circular dependencies using DFS.

        Returns the cycle path if found, None otherwise.
        """
        visited.add(plugin_id)
        rec_stack.add(plugin_id)
        path.append(plugin_id)

        for dep in self._graph.get(plugin_id, []):
            if dep.plugin_id not in visited:
                cycle = self._detect_circular(dep.plugin_id, visited, rec_stack, path)
                if cycle:
                    return cycle
            elif dep.plugin_id in rec_stack:
                # Found cycle - return path from cycle start
                cycle_start = path.index(dep.plugin_id)
                return path[cycle_start:] + [dep.plugin_id]

        rec_stack.remove(plugin_id)
        path.pop()
        return None

    def _find_satisfying_version(
        self,
        plugin_id: str,
        constraints: list[VersionSpec],
    ) -> Version | None:
        """Find a version that satisfies all constraints."""
        available = self.get_available_versions(plugin_id)

        for version in available:
            if all(spec.satisfies(version) for spec in constraints):
                return version

        return None

    def _collect_constraints(
        self,
        plugin_id: str,
    ) -> list[VersionSpec]:
        """Collect all version constraints for a plugin."""
        constraints = []

        for source_id, deps in self._graph.items():
            for dep in deps:
                if dep.plugin_id == plugin_id:
                    constraints.append(dep.version_spec)

        return constraints

    def _topological_sort(self, plugins: list[str]) -> list[str] | None:
        """
        Perform topological sort on the dependency graph.

        Returns sorted list (install order) or None if cyclic.
        """
        in_degree: dict[str, int] = defaultdict(int)

        # Calculate in-degrees
        for plugin_id in plugins:
            for dep in self._graph.get(plugin_id, []):
                if dep.plugin_id in plugins:
                    in_degree[plugin_id] += 1

        # Start with nodes having no dependencies
        queue = [p for p in plugins if in_degree[p] == 0]
        result: list[str] = []

        while queue:
            # Sort to ensure deterministic order
            queue.sort()
            plugin_id = queue.pop(0)
            result.append(plugin_id)

            # Find plugins that depend on this one
            for other_id in plugins:
                if other_id in result:
                    continue
                for dep in self._graph.get(other_id, []):
                    if dep.plugin_id == plugin_id:
                        in_degree[other_id] -= 1
                        if in_degree[other_id] == 0:
                            queue.append(other_id)

        if len(result) != len(plugins):
            return None  # Cycle exists

        return result

    def resolve(
        self,
        target_plugins: list[str] | None = None,
    ) -> ResolutionResult:
        """
        Resolve dependencies for target plugins.

        Args:
            target_plugins: Plugins to resolve (all in graph if None)

        Returns:
            Resolution result with resolved versions or conflicts
        """
        conflicts: list[Conflict] = []
        resolved: list[ResolvedDependency] = []

        # Default to all plugins in graph
        if target_plugins is None:
            target_plugins = list(self._graph.keys())

        # Check for circular dependencies
        for plugin_id in target_plugins:
            visited: set[str] = set()
            rec_stack: set[str] = set()
            cycle = self._detect_circular(plugin_id, visited, rec_stack, [])

            if cycle:
                conflicts.append(
                    Conflict(
                        conflict_type=ConflictType.CIRCULAR,
                        plugins=cycle,
                        message=f"Circular dependency detected: {' -> '.join(cycle)}",
                        suggestions=[
                            "Remove one of the dependencies to break the cycle",
                            "Consider using optional dependencies",
                        ],
                    )
                )
                return ResolutionResult(
                    success=False,
                    conflicts=conflicts,
                    message="Circular dependency detected",
                )

        # Collect all plugins to resolve (including transitive dependencies)
        all_plugins: set[str] = set(target_plugins)

        def collect_deps(plugin_id: str) -> None:
            for dep in self._graph.get(plugin_id, []):
                if dep.plugin_id not in all_plugins:
                    all_plugins.add(dep.plugin_id)
                    if dep.plugin_id in self._graph:
                        collect_deps(dep.plugin_id)

        for plugin_id in target_plugins:
            collect_deps(plugin_id)

        # Resolve versions for each plugin
        for plugin_id in all_plugins:
            constraints = self._collect_constraints(plugin_id)

            if not constraints:
                # No constraints - use latest
                available = self.get_available_versions(plugin_id)
                if available:
                    version = available[0]
                else:
                    # Check if it's in the graph (a root plugin)
                    if plugin_id in target_plugins:
                        # Root plugins don't need to be in available versions
                        continue
                    else:
                        conflicts.append(
                            Conflict(
                                conflict_type=ConflictType.MISSING,
                                plugins=[plugin_id],
                                message=f"Required plugin '{plugin_id}' not found",
                                suggestions=[
                                    f"Install '{plugin_id}' from the catalog",
                                    "Check the plugin name for typos",
                                ],
                            )
                        )
                        continue
            else:
                # Check if any versions are available first
                available = self.get_available_versions(plugin_id)

                if not available:
                    # No versions available at all - missing dependency
                    conflicts.append(
                        Conflict(
                            conflict_type=ConflictType.MISSING,
                            plugins=[plugin_id],
                            message=f"Required plugin '{plugin_id}' not found",
                            suggestions=[
                                f"Install '{plugin_id}' from the catalog",
                                "Check the plugin name for typos",
                            ],
                        )
                    )
                    continue

                version = self._find_satisfying_version(plugin_id, constraints)

                if version is None:
                    # Version conflict - versions exist but none satisfy constraints
                    constraint_strs = [str(c) for c in constraints]
                    requesting = self._required_by.get(plugin_id, [])

                    conflicts.append(
                        Conflict(
                            conflict_type=ConflictType.VERSION_MISMATCH,
                            plugins=[plugin_id] + requesting,
                            message=(
                                f"No version of '{plugin_id}' satisfies all constraints: "
                                f"{', '.join(constraint_strs)}"
                            ),
                            suggestions=[
                                "Update one or more plugins to compatible versions",
                                "Check if a newer version of the dependency is available",
                            ],
                        )
                    )
                    continue

            # Check for multiple versions needed
            if plugin_id in self._resolved and self._resolved[plugin_id] != version:
                conflicts.append(
                    Conflict(
                        conflict_type=ConflictType.MULTIPLE_VERSIONS,
                        plugins=[plugin_id],
                        message=(
                            f"Multiple versions of '{plugin_id}' required: "
                            f"{self._resolved[plugin_id]} and {version}"
                        ),
                        suggestions=[
                            "Choose a common version that satisfies all constraints",
                        ],
                    )
                )
                continue

            self._resolved[plugin_id] = version

            # Find if it's optional (all requesters mark it optional)
            optional = True
            for source_id, deps in self._graph.items():
                for dep in deps:
                    if dep.plugin_id == plugin_id and not dep.optional:
                        optional = False
                        break
                if not optional:
                    break

            resolved.append(
                ResolvedDependency(
                    plugin_id=plugin_id,
                    version=version,
                    required_by=self._required_by.get(plugin_id, []),
                    optional=optional,
                )
            )

        if conflicts:
            return ResolutionResult(
                success=False,
                resolved=resolved,
                conflicts=conflicts,
                message=f"{len(conflicts)} conflict(s) found",
            )

        # Topological sort for install order
        install_order = self._topological_sort(list(all_plugins))

        if install_order is None:
            return ResolutionResult(
                success=False,
                resolved=resolved,
                conflicts=[
                    Conflict(
                        conflict_type=ConflictType.CIRCULAR,
                        plugins=list(all_plugins),
                        message="Circular dependency in resolution",
                    )
                ],
                message="Failed to determine install order",
            )

        return ResolutionResult(
            success=True,
            resolved=resolved,
            install_order=install_order,
            message=f"Successfully resolved {len(resolved)} dependencies",
        )

    def clear(self) -> None:
        """Clear all state."""
        self._graph.clear()
        self._available_versions.clear()
        self._resolved.clear()
        self._required_by.clear()


# Singleton instance
_resolver: DependencyResolver | None = None


def get_dependency_resolver() -> DependencyResolver:
    """Get or create the dependency resolver singleton."""
    global _resolver
    if _resolver is None:
        _resolver = DependencyResolver()
    return _resolver


def resolve_dependencies(
    plugins: dict[str, dict[str, str]],
    available_versions: dict[str, list[str]] | None = None,
) -> ResolutionResult:
    """
    Convenience function to resolve dependencies.

    Args:
        plugins: Map of plugin_id -> {dependency_id: version_spec}
        available_versions: Map of plugin_id -> [available_versions]

    Returns:
        Resolution result
    """
    resolver = DependencyResolver()

    # Add available versions
    if available_versions:
        for plugin_id, versions in available_versions.items():
            for v in versions:
                resolver.add_available_version(plugin_id, v)

    # Add plugins and dependencies
    for plugin_id, deps in plugins.items():
        resolver.add_plugin(plugin_id, deps)

    return resolver.resolve()


def check_compatibility(
    plugin_id: str,
    version: str,
    constraints: list[str],
) -> bool:
    """
    Check if a version satisfies all constraints.

    Args:
        plugin_id: Plugin identifier (for logging)
        version: Version to check
        constraints: List of version constraint strings

    Returns:
        True if compatible
    """
    v = Version.parse(version)

    for constraint_str in constraints:
        spec = VersionSpec.parse(constraint_str)
        if not spec.satisfies(v):
            return False

    return True
