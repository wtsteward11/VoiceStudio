#!/usr/bin/env python
"""
Dependency Rule Validation Script.

Task 3.2.1: Automated layer violation detection.
Validates that dependencies flow in the correct direction according to Clean Architecture.

Layer hierarchy (outer to inner):
    API/Routes → Services → Domain → (nothing)
    Infrastructure → Domain → (nothing)

Rules:
    - Domain layer MUST NOT import from any other layer
    - Services MUST NOT import from API layer
    - API layer can import from Services and Domain
    - Infrastructure can import from Domain only
"""

import ast
import sys
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path


class Layer(Enum):
    """Architecture layers from outermost to innermost."""
    EXTERNAL = "external"        # Third-party libraries
    INFRASTRUCTURE = "infra"     # Adapters, persistence implementations
    API = "api"                  # HTTP routes, controllers
    APPLICATION = "application"  # Use cases, commands, queries
    SERVICES = "services"        # Business services
    DOMAIN = "domain"           # Entities, value objects, domain services


# Layer definitions by path pattern
LAYER_PATTERNS: dict[str, Layer] = {
    "backend/api/routes/": Layer.API,
    "backend/api/middleware/": Layer.API,
    "backend/api/": Layer.API,
    "backend/application/": Layer.APPLICATION,
    "backend/services/": Layer.SERVICES,
    "backend/domain/": Layer.DOMAIN,
    "backend/infrastructure/": Layer.INFRASTRUCTURE,
    "backend/data/": Layer.INFRASTRUCTURE,
    "app/core/engines/": Layer.INFRASTRUCTURE,
    "app/core/runtime/": Layer.INFRASTRUCTURE,
}

# Allowed dependencies (key can import from values)
ALLOWED_DEPENDENCIES: dict[Layer, set[Layer]] = {
    Layer.API: {Layer.APPLICATION, Layer.SERVICES, Layer.DOMAIN, Layer.EXTERNAL},
    Layer.APPLICATION: {Layer.SERVICES, Layer.DOMAIN, Layer.EXTERNAL},
    Layer.SERVICES: {Layer.DOMAIN, Layer.INFRASTRUCTURE, Layer.EXTERNAL},
    Layer.INFRASTRUCTURE: {Layer.DOMAIN, Layer.EXTERNAL},
    Layer.DOMAIN: {Layer.EXTERNAL},  # Domain should only import stdlib/external
}


@dataclass
class Violation:
    """A dependency rule violation."""
    file: Path
    line: int
    importing_layer: Layer
    imported_module: str
    imported_layer: Layer
    message: str


@dataclass
class ValidationResult:
    """Result of dependency validation."""
    files_checked: int = 0
    violations: list[Violation] = field(default_factory=list)

    @property
    def is_valid(self) -> bool:
        return len(self.violations) == 0


class ImportVisitor(ast.NodeVisitor):
    """AST visitor to extract imports from Python files."""

    def __init__(self):
        self.imports: list[tuple[str, int]] = []

    def visit_Import(self, node: ast.Import):
        for alias in node.names:
            self.imports.append((alias.name, node.lineno))
        self.generic_visit(node)

    def visit_ImportFrom(self, node: ast.ImportFrom):
        if node.module:
            self.imports.append((node.module, node.lineno))
        self.generic_visit(node)


def get_layer(path: Path, project_root: Path) -> Layer:
    """Determine which layer a file belongs to."""
    relative = path.relative_to(project_root).as_posix()

    for pattern, layer in LAYER_PATTERNS.items():
        if relative.startswith(pattern):
            return layer

    return Layer.EXTERNAL


def get_import_layer(module: str) -> Layer:
    """Determine which layer an imported module belongs to."""
    # Check known patterns
    if module.startswith("backend.api.routes"):
        return Layer.API
    if module.startswith("backend.api"):
        return Layer.API
    if module.startswith("backend.application"):
        return Layer.APPLICATION
    if module.startswith("backend.services"):
        return Layer.SERVICES
    if module.startswith("backend.domain"):
        return Layer.DOMAIN
    if module.startswith("backend.infrastructure"):
        return Layer.INFRASTRUCTURE
    if module.startswith("backend.data"):
        return Layer.INFRASTRUCTURE
    if module.startswith("app.core.engines"):
        return Layer.INFRASTRUCTURE
    if module.startswith("app.core"):
        return Layer.INFRASTRUCTURE

    # Everything else is external
    return Layer.EXTERNAL


def validate_file(path: Path, project_root: Path) -> list[Violation]:
    """Validate a single file's dependencies."""
    violations = []

    try:
        source = path.read_text(encoding="utf-8")
        tree = ast.parse(source)
    except (SyntaxError, UnicodeDecodeError) as e:
        print(f"  Warning: Could not parse {path}: {e}")
        return []

    file_layer = get_layer(path, project_root)

    visitor = ImportVisitor()
    visitor.visit(tree)

    allowed = ALLOWED_DEPENDENCIES.get(file_layer, set())

    for module, line in visitor.imports:
        import_layer = get_import_layer(module)

        if import_layer == Layer.EXTERNAL:
            continue  # External imports are always allowed

        if import_layer not in allowed and import_layer != file_layer:
            violations.append(Violation(
                file=path,
                line=line,
                importing_layer=file_layer,
                imported_module=module,
                imported_layer=import_layer,
                message=f"{file_layer.value} layer cannot import from {import_layer.value} layer",
            ))

    return violations


def validate_project(project_root: Path) -> ValidationResult:
    """Validate all Python files in the project."""
    result = ValidationResult()

    # Define directories to scan
    scan_dirs = [
        project_root / "backend",
        project_root / "app/core",
    ]

    for scan_dir in scan_dirs:
        if not scan_dir.exists():
            continue

        for py_file in scan_dir.rglob("*.py"):
            if "__pycache__" in str(py_file):
                continue

            result.files_checked += 1
            violations = validate_file(py_file, project_root)
            result.violations.extend(violations)

    return result


def main():
    """Main entry point."""
    # Determine project root
    if len(sys.argv) > 1:
        project_root = Path(sys.argv[1]).resolve()
    else:
        # Default to parent of scripts directory
        project_root = Path(__file__).parent.parent.resolve()

    print(f"Validating dependencies in: {project_root}")
    print("=" * 60)

    result = validate_project(project_root)

    print(f"Files checked: {result.files_checked}")
    print(f"Violations found: {len(result.violations)}")
    print()

    if result.violations:
        print("VIOLATIONS:")
        print("-" * 60)

        # Group by file
        by_file: dict[Path, list[Violation]] = {}
        for v in result.violations:
            by_file.setdefault(v.file, []).append(v)

        for file, violations in sorted(by_file.items()):
            rel_path = file.relative_to(project_root)
            print(f"\n{rel_path}:")

            for v in sorted(violations, key=lambda x: x.line):
                print(f"  Line {v.line}: {v.message}")
                print(f"    Imported: {v.imported_module}")

        print()
        print("=" * 60)
        print("FAILED: Dependency violations detected")
        return 1

    print("=" * 60)
    print("PASSED: No dependency violations")
    return 0


if __name__ == "__main__":
    sys.exit(main())
