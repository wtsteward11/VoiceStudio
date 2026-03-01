#!/usr/bin/env python3
"""
Architecture Boundary Enforcement Script

Per ADR-008 Layer 3: Pre-Commit Hook enforcement of sacred boundaries.

Sacred Boundaries (from .cursor/rules/core/architecture.mdc):
1. UI may NOT call engine internals directly
2. UI interacts through stable core contracts (interfaces/protocols)
3. Engines attach via adapters that implement those contracts

This script checks for violations:
- C# UI code importing app.core.engines (forbidden)
- Python routes importing engines directly (should use EngineService)

Usage:
    python scripts/check_architecture_boundaries.py
    python scripts/check_architecture_boundaries.py --fix  # Show fix suggestions
    python scripts/check_architecture_boundaries.py --ci   # Exit with error code on violations

Returns:
    0: No violations found
    1: Violations found
"""

import argparse
import re
import sys
from pathlib import Path
from typing import NamedTuple


class Violation(NamedTuple):
    """Represents an architecture boundary violation."""
    file: Path
    line_number: int
    line_content: str
    violation_type: str
    suggestion: str


# Patterns that indicate boundary violations

# C# UI code should not import engine internals
CSHARP_FORBIDDEN_PATTERNS = [
    (
        r"using\s+app\.core\.engines",
        "UI importing engine internals",
        "Use IBackendClient or core contracts instead"
    ),
    (
        r"from\s+app\.core\.engines",
        "UI importing engine internals (Python-style in C#)",
        "Use IBackendClient or core contracts instead"
    ),
    (
        r"new\s+\w+Engine\s*\(",
        "Direct engine instantiation in UI",
        "Use EngineService via backend API"
    ),
]

# Python routes should use EngineService, not direct imports
# GAP-ARCH-002: Expanded patterns per Comprehensive Gap Remediation Plan Phase 5
PYTHON_ROUTE_FORBIDDEN_PATTERNS = [
    (
        r"from\s+app\.core\.engines\.\w+_engine\s+import",
        "Route directly importing engine class",
        "Use EngineService from backend/services/engine_service.py"
    ),
    (
        r"from\s+app\.core\.engines\s+import\s+(?!EngineProtocol)",
        "Route importing from engines package",
        "Use EngineService for engine access"
    ),
    (
        r"from\s+app\.core\.engines\.router\s+import",
        "Route importing engine router directly",
        "Use EngineService.select_engine() instead"
    ),
    (
        r"from\s+app\.core\.engines\.metrics\s+import",
        "Route importing engine metrics directly",
        "Access metrics via EngineService or PerformanceMiddleware"
    ),
    (
        r"from\s+backend\.voice\.\w+\.engine\s+import",
        "Route importing voice engine directly",
        "Use feature_status_service or EngineService for engine access"
    ),
    # GAP-008: LLM provider patterns
    (
        r"from\s+app\.core\.engines\.llm_\w+_adapter\s+import\s+\w+Provider",
        "Route importing LLM provider directly",
        "Use LLMProviderService from backend/services/llm_provider_service.py"
    ),
    (
        r"from\s+app\.core\.engines\.llm_interface\s+import",
        "Route importing LLM interface directly",
        "Move LLM logic to service layer or use existing LLM service"
    ),
]

# Allowed patterns (exceptions)
ALLOWED_PATTERNS = [
    r"#\s*ALLOWED:",  # Explicit allowlist comment
    r"#\s*BOUNDARY_EXCEPTION:",  # Explicit exception marker
    r"from\s+\.protocols\s+import",  # Protocol imports are fine
    r"from\s+\.base\s+import\s+EngineProtocol",  # Protocol import
    r"from\s+app\.core\.engines\.protocols\s+import",  # Protocol import
    r"from\s+app\.core\.engines\.base\s+import\s+EngineProtocol",  # Protocol import
    # Interface types (DTOs/contracts) are allowed everywhere
    r"from\s+app\.core\.engines\.llm_interface\s+import\s+(LLMConfig|Message|MessageRole)",
]

# Files exempt from certain checks (service layer files that legitimately need engine access)
EXEMPT_FILES = [
    "backend/services/llm_provider_service.py",  # GAP-008: Service layer wraps LLM providers
    "backend/services/engine_service.py",  # Engine service wraps engines
    "backend/services/llm_function_calling.py",  # LLM function calling service
    "backend/services/model_preflight.py",  # Preflight service uses engine paths
]


def is_allowed(line: str) -> bool:
    """Check if a line has an explicit allowlist marker."""
    return any(re.search(pattern, line, re.IGNORECASE) for pattern in ALLOWED_PATTERNS)


def check_csharp_ui_boundaries(repo_root: Path) -> list[Violation]:
    """Check C# UI code for boundary violations."""
    violations = []

    # UI code is in src/VoiceStudio.App/
    ui_path = repo_root / "src" / "VoiceStudio.App"
    if not ui_path.exists():
        return violations

    # Exclude test files and generated files
    exclude_patterns = ["*.g.cs", "*.Designer.cs", "obj/", "bin/"]

    for cs_file in ui_path.rglob("*.cs"):
        # Skip excluded patterns
        if any(pattern.replace("*", "") in str(cs_file) for pattern in exclude_patterns):
            continue
        if "obj" in cs_file.parts or "bin" in cs_file.parts:
            continue

        try:
            content = cs_file.read_text(encoding="utf-8")
            lines = content.split("\n")

            for line_num, line in enumerate(lines, 1):
                if is_allowed(line):
                    continue

                for pattern, violation_type, suggestion in CSHARP_FORBIDDEN_PATTERNS:
                    if re.search(pattern, line, re.IGNORECASE):
                        violations.append(Violation(
                            file=cs_file,
                            line_number=line_num,
                            line_content=line.strip(),
                            violation_type=violation_type,
                            suggestion=suggestion
                        ))
        except Exception as e:
            print(f"Warning: Could not read {cs_file}: {e}", file=sys.stderr)

    return violations


def check_python_route_boundaries(repo_root: Path) -> list[Violation]:
    """Check Python route files for boundary violations."""
    violations = []

    # Routes are in backend/api/routes/
    routes_path = repo_root / "backend" / "api" / "routes"
    if not routes_path.exists():
        return violations

    for py_file in routes_path.rglob("*.py"):
        try:
            content = py_file.read_text(encoding="utf-8")
            lines = content.split("\n")

            for line_num, line in enumerate(lines, 1):
                if is_allowed(line):
                    continue

                for pattern, violation_type, suggestion in PYTHON_ROUTE_FORBIDDEN_PATTERNS:
                    if re.search(pattern, line, re.IGNORECASE):
                        # Additional check: allow EngineProtocol imports
                        if "EngineProtocol" in line:
                            continue
                        violations.append(Violation(
                            file=py_file,
                            line_number=line_num,
                            line_content=line.strip(),
                            violation_type=violation_type,
                            suggestion=suggestion
                        ))
        except Exception as e:
            print(f"Warning: Could not read {py_file}: {e}", file=sys.stderr)

    return violations


# Patterns for service layer violations (GAP-007)
PYTHON_SERVICE_FORBIDDEN_PATTERNS = [
    (
        r"raise\s+HTTPException",
        "Service layer raising HTTPException",
        "Raise service-layer exception; route handler converts to HTTPException"
    ),
]


def is_file_exempt(file_path: Path, repo_root: Path) -> bool:
    """Check if a file is exempt from certain checks."""
    try:
        rel_path = file_path.relative_to(repo_root).as_posix()
    except ValueError:
        rel_path = str(file_path)

    return any(rel_path.endswith(exempt) or exempt in rel_path for exempt in EXEMPT_FILES)


def check_python_service_boundaries(repo_root: Path) -> list[Violation]:
    """Check Python service files for boundary violations (e.g., HTTPException in services)."""
    violations = []

    # Services are in backend/services/
    services_path = repo_root / "backend" / "services"
    if not services_path.exists():
        return violations

    for py_file in services_path.rglob("*.py"):
        # Skip exempt files
        if is_file_exempt(py_file, repo_root):
            continue

        try:
            content = py_file.read_text(encoding="utf-8")
            lines = content.split("\n")

            for line_num, line in enumerate(lines, 1):
                if is_allowed(line):
                    continue

                for pattern, violation_type, suggestion in PYTHON_SERVICE_FORBIDDEN_PATTERNS:
                    if re.search(pattern, line, re.IGNORECASE):
                        violations.append(Violation(
                            file=py_file,
                            line_number=line_num,
                            line_content=line.strip(),
                            violation_type=violation_type,
                            suggestion=suggestion
                        ))
        except Exception as e:
            print(f"Warning: Could not read {py_file}: {e}", file=sys.stderr)

    return violations


def format_violations(violations: list[Violation], show_suggestions: bool = False) -> str:
    """Format violations for display."""
    if not violations:
        return ""

    lines = []
    lines.append(f"\n{'='*70}")
    lines.append(f"ARCHITECTURE BOUNDARY VIOLATIONS FOUND: {len(violations)}")
    lines.append(f"{'='*70}\n")

    for v in violations:
        rel_path = v.file
        try:
            rel_path = v.file.relative_to(Path.cwd())
        # ALLOWED: bare except - File may not be relative to cwd, keep absolute path
        except ValueError:
            pass

        lines.append(f"{rel_path}:{v.line_number}")
        lines.append(f"  Type: {v.violation_type}")
        lines.append(f"  Line: {v.line_content[:80]}{'...' if len(v.line_content) > 80 else ''}")
        if show_suggestions:
            lines.append(f"  Fix:  {v.suggestion}")
        lines.append("")

    lines.append(f"{'='*70}")
    lines.append("Sacred Boundaries (ADR-008):")
    lines.append("  - UI may NOT call engine internals directly")
    lines.append("  - UI interacts through stable core contracts")
    lines.append("  - Engines attach via adapters implementing contracts")
    lines.append(f"{'='*70}")

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Check architecture boundary compliance (ADR-008)"
    )
    parser.add_argument(
        "--fix", "-f",
        action="store_true",
        help="Show fix suggestions for violations"
    )
    parser.add_argument(
        "--ci",
        action="store_true",
        help="CI mode: exit with error code 1 on violations"
    )
    parser.add_argument(
        "--quiet", "-q",
        action="store_true",
        help="Only output if violations found"
    )

    args = parser.parse_args()

    # Find repo root (look for .cursor or .git)
    repo_root = Path.cwd()
    for parent in [repo_root, *list(repo_root.parents)]:
        if (parent / ".cursor").exists() or (parent / ".git").exists():
            repo_root = parent
            break

    if not args.quiet:
        print(f"Checking architecture boundaries in: {repo_root}")
        print("Per ADR-008: Sacred Boundaries (UI <-> Core <-> Engines)")
        print()

    # Run checks
    all_violations = []

    if not args.quiet:
        print("Checking C# UI code for engine imports...")
    csharp_violations = check_csharp_ui_boundaries(repo_root)
    all_violations.extend(csharp_violations)

    if not args.quiet:
        print("Checking Python routes for direct engine imports...")
    python_violations = check_python_route_boundaries(repo_root)
    all_violations.extend(python_violations)

    if not args.quiet:
        print("Checking Python services for HTTPException usage...")
    service_violations = check_python_service_boundaries(repo_root)
    all_violations.extend(service_violations)

    # Report results
    if all_violations:
        print(format_violations(all_violations, show_suggestions=args.fix))
        if args.ci:
            return 1
    else:
        if not args.quiet:
            print("\n[PASS] No architecture boundary violations found!")
            print("  Sacred boundaries are intact.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
