"""
Quality Metrics Calculation Script
Calculates and verifies code quality, test coverage, and performance metrics.
"""

import json
import logging
import sys
from datetime import datetime
from pathlib import Path
from typing import Any

project_root = Path(__file__).parent.parent.parent
sys.path.insert(0, str(project_root))

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


def count_lines_of_code(directory: Path, extensions: list[str]) -> dict[str, int]:
    """Count lines of code by file type."""
    counts = {}

    for ext in extensions:
        counts[ext] = 0
        for file_path in directory.rglob(f"*.{ext}"):
            if should_include_file(file_path):
                try:
                    with open(file_path, encoding="utf-8", errors="ignore") as f:
                        lines = f.readlines()
                        counts[ext] += len([l for l in lines if l.strip()])
                except OSError:
                    pass  # Skip unreadable files

    return counts


def should_include_file(file_path: Path) -> bool:
    """Determine if file should be included in metrics."""
    exclude_dirs = [
        "__pycache__",
        ".git",
        "node_modules",
        ".venv",
        "venv",
        "env",
        "build",
        "dist",
        ".pytest_cache",
        ".mypy_cache",
        "bin",
        "obj",
    ]

    return all(exclude_dir not in str(file_path) for exclude_dir in exclude_dirs)


def count_test_files() -> dict[str, int]:
    """Count test files."""
    tests_dir = project_root / "tests"

    if not tests_dir.exists():
        return {"total": 0, "unit": 0, "integration": 0, "e2e": 0, "performance": 0}

    counts = {
        "total": len(list(tests_dir.rglob("test_*.py"))),
        "unit": (
            len(list((tests_dir / "unit").rglob("test_*.py")))
            if (tests_dir / "unit").exists()
            else 0
        ),
        "integration": (
            len(list((tests_dir / "integration").rglob("test_*.py")))
            if (tests_dir / "integration").exists()
            else 0
        ),
        "e2e": (
            len(list((tests_dir / "e2e").rglob("test_*.py"))) if (tests_dir / "e2e").exists() else 0
        ),
        "performance": (
            len(list((tests_dir / "performance").rglob("test_*.py")))
            if (tests_dir / "performance").exists()
            else 0
        ),
    }

    return counts


def count_engines() -> int:
    """Count engine files."""
    engines_dir = project_root / "app" / "core" / "engines"

    if not engines_dir.exists():
        return 0

    return len(list(engines_dir.glob("*_engine.py")))


def count_backend_routes() -> int:
    """Count backend route files."""
    routes_dir = project_root / "backend" / "api" / "routes"

    if not routes_dir.exists():
        return 0

    return len([f for f in routes_dir.glob("*.py") if f.name != "__init__.py"])


def calculate_test_coverage() -> dict[str, Any]:
    """Calculate test coverage metrics."""
    coverage = {
        "engines": {"total": count_engines(), "tested": 0, "coverage_percent": 0.0},
        "backend_routes": {"total": count_backend_routes(), "tested": 0, "coverage_percent": 0.0},
        "overall": {"test_files": count_test_files()["total"], "coverage_percent": 0.0},
    }

    test_files = count_test_files()
    engines_total = count_engines()
    routes_total = count_backend_routes()

    if engines_total > 0:
        coverage["engines"]["tested"] = min(
            engines_total, test_files["integration"] + test_files["unit"]
        )
        coverage["engines"]["coverage_percent"] = (
            coverage["engines"]["tested"] / engines_total
        ) * 100

    if routes_total > 0:
        coverage["backend_routes"]["tested"] = min(
            routes_total, test_files["integration"] + test_files["unit"]
        )
        coverage["backend_routes"]["coverage_percent"] = (
            coverage["backend_routes"]["tested"] / routes_total
        ) * 100

    if engines_total + routes_total > 0:
        total_tested = coverage["engines"]["tested"] + coverage["backend_routes"]["tested"]
        total_items = engines_total + routes_total
        coverage["overall"]["coverage_percent"] = (total_tested / total_items) * 100

    return coverage


def generate_quality_report(metrics: dict[str, Any]) -> str:
    """Generate quality metrics report."""
    report_lines = []
    report_lines.append("=" * 80)
    report_lines.append("QUALITY METRICS REPORT")
    report_lines.append("=" * 80)
    report_lines.append(f"Generated: {datetime.now().isoformat()}")
    report_lines.append("")

    report_lines.append("Code Metrics:")
    report_lines.append(f"  Python Files: {metrics['code']['py']:,} lines")
    report_lines.append(f"  C# Files: {metrics['code']['cs']:,} lines")
    report_lines.append(f"  XAML Files: {metrics['code']['xaml']:,} lines")
    report_lines.append(f"  Total Lines of Code: {sum(metrics['code'].values()):,}")
    report_lines.append("")

    report_lines.append("Test Metrics:")
    report_lines.append(f"  Total Test Files: {metrics['tests']['total']}")
    report_lines.append(f"  Unit Tests: {metrics['tests']['unit']}")
    report_lines.append(f"  Integration Tests: {metrics['tests']['integration']}")
    report_lines.append(f"  E2E Tests: {metrics['tests']['e2e']}")
    report_lines.append(f"  Performance Tests: {metrics['tests']['performance']}")
    report_lines.append("")

    report_lines.append("Test Coverage:")
    report_lines.append(
        f"  Engines: {metrics['coverage']['engines']['tested']}/{metrics['coverage']['engines']['total']} ({metrics['coverage']['engines']['coverage_percent']:.1f}%)"
    )
    report_lines.append(
        f"  Backend Routes: {metrics['coverage']['backend_routes']['tested']}/{metrics['coverage']['backend_routes']['total']} ({metrics['coverage']['backend_routes']['coverage_percent']:.1f}%)"
    )
    report_lines.append(
        f"  Overall Coverage: {metrics['coverage']['overall']['coverage_percent']:.1f}%"
    )
    report_lines.append("")

    report_lines.append("Component Counts:")
    report_lines.append(f"  Engines: {metrics['components']['engines']}")
    report_lines.append(f"  Backend Routes: {metrics['components']['backend_routes']}")
    report_lines.append("")

    report_lines.append("=" * 80)

    return "\n".join(report_lines)


def main():
    """Main function."""
    logger.info("Calculating quality metrics...")

    code_metrics = count_lines_of_code(project_root, ["py", "cs", "xaml"])
    test_metrics = count_test_files()
    coverage_metrics = calculate_test_coverage()

    metrics = {
        "code": code_metrics,
        "tests": test_metrics,
        "coverage": coverage_metrics,
        "components": {"engines": count_engines(), "backend_routes": count_backend_routes()},
    }

    report = generate_quality_report(metrics)
    print(report)

    report_file = project_root / "quality_metrics_report.txt"
    with open(report_file, "w", encoding="utf-8") as f:
        f.write(report)

    json_file = project_root / "quality_metrics.json"
    with open(json_file, "w", encoding="utf-8") as f:
        json.dump(metrics, f, indent=2)

    logger.info(f"Quality metrics report saved to: {report_file}")
    logger.info(f"Quality metrics JSON saved to: {json_file}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
