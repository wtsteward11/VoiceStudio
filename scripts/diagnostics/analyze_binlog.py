#!/usr/bin/env python3
"""
Analyze MSBuild binlog to extract XamlCompiler.exe invocations and identify failing XAML files.

This script parses an MSBuild binary log (.binlog) to extract XAML compiler information,
helping diagnose silent WinUI 3 XAML compiler failures where XamlCompiler.exe exits
with code 1 and produces no output.json.

References:
- GitHub Issue #10027: Can't get error output from XamlCompiler.exe
- GitHub Issue #10947: XamlCompiler.exe exits code 1 for Views subfolders

Usage:
    python scripts/analyze_binlog.py
    python scripts/analyze_binlog.py --binlog .buildlogs/build_diagnostic_*.binlog
    python scripts/analyze_binlog.py --format json
"""

import argparse
import glob
import json
import os
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any


@dataclass
class AnalysisResults:
    """Results of binlog analysis."""

    binlog_path: str = ""
    timestamp: str = ""
    xaml_compiler_tasks: list[str] = field(default_factory=list)
    input_json_files: list[str] = field(default_factory=list)
    xaml_pages: list[dict[str, Any]] = field(default_factory=list)
    nested_views_xaml: list[dict[str, str]] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    recommendations: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "binlog_path": self.binlog_path,
            "timestamp": self.timestamp,
            "xaml_compiler_tasks": self.xaml_compiler_tasks,
            "input_json_files": self.input_json_files,
            "xaml_pages": self.xaml_pages,
            "nested_views_xaml": self.nested_views_xaml,
            "warnings": self.warnings,
            "recommendations": self.recommendations,
        }


def find_binlog(binlog_path: str, repo_root: Path) -> Path | None:
    """Find the binlog file to analyze."""
    buildlogs_dir = repo_root / ".buildlogs"

    if not binlog_path:
        # Find most recent diagnostic binlog
        pattern = str(buildlogs_dir / "build_diagnostic_*.binlog")
        files = sorted(glob.glob(pattern), key=os.path.getmtime, reverse=True)

        if not files:
            # Try any binlog
            pattern = str(buildlogs_dir / "*.binlog")
            files = sorted(glob.glob(pattern), key=os.path.getmtime, reverse=True)

        if files:
            return Path(files[0])
        return None

    if "*" in binlog_path:
        # Wildcard pattern
        files = sorted(glob.glob(binlog_path), key=os.path.getmtime, reverse=True)
        if files:
            return Path(files[0])
        return None

    path = Path(binlog_path)
    if path.exists():
        return path
    return None


def search_with_structured_logger(binlog_path: Path) -> dict[str, list[str]] | None:
    """Try to use StructuredLogger CLI for more accurate binlog analysis."""
    import shutil
    import subprocess

    # Check if slv (StructuredLogViewer CLI) is available
    slv_path = shutil.which("slv")
    if not slv_path:
        # Try dotnet tool
        try:
            result = subprocess.run(
                ["dotnet", "tool", "list", "--global"],
                capture_output=True,
                text=True,
                timeout=10,
            )
            if "msbuild.structuredlogger" not in result.stdout.lower():
                print("  StructuredLogger CLI not available - using binary fallback")
                return None
            slv_path = "slv"
        except Exception:
            print("  StructuredLogger CLI not available - using binary fallback")
            return None

    print("  Using StructuredLogger CLI for analysis")

    results: dict[str, list[str]] = {
        "xaml_compiler_commands": [],
        "input_json_paths": [],
        "output_json_paths": [],
    }

    try:
        # Use slv to search for XamlCompiler tasks
        search_result = subprocess.run(
            [slv_path, "search", str(binlog_path), "XamlCompiler"],
            capture_output=True,
            text=True,
            timeout=60,
        )

        if search_result.returncode == 0 and search_result.stdout:
            for line in search_result.stdout.splitlines():
                if "XamlCompiler" in line:
                    results["xaml_compiler_commands"].append(line.strip())
                if "input.json" in line:
                    match = re.search(r"([A-Za-z]:\\[^\s\"<>|*?]+input\.json)", line)
                    if match:
                        results["input_json_paths"].append(match.group(1))

        return results

    except subprocess.TimeoutExpired:
        print("  StructuredLogger search timed out", file=sys.stderr)
        return None
    except Exception as e:
        print(f"  StructuredLogger search failed: {e}", file=sys.stderr)
        return None


def search_binlog_content(binlog_path: Path) -> dict[str, list[str]]:
    """Search binlog binary content for XamlCompiler patterns (fallback method)."""
    results: dict[str, list[str]] = {
        "xaml_compiler_commands": [],
        "input_json_paths": [],
        "output_json_paths": [],
    }

    try:
        with open(binlog_path, "rb") as f:
            content = f.read()

        # Try to decode as UTF-8 (binlogs contain embedded strings)
        try:
            text = content.decode("utf-8", errors="ignore")
        except Exception:
            text = str(content)

        # Search for XamlCompiler.exe patterns
        xaml_matches = re.findall(r"XamlCompiler\.exe[^\x00]{0,500}", text)
        for match in xaml_matches:
            cleaned = re.sub(r"[\x00-\x1f]", " ", match).strip()
            if cleaned and cleaned not in results["xaml_compiler_commands"]:
                results["xaml_compiler_commands"].append(cleaned)

        # Search for input.json patterns
        input_matches = re.findall(r"[A-Za-z]:\\[^\"<>|*?\x00]+input\.json", text)
        for match in input_matches:
            cleaned = re.sub(r"[\x00-\x1f]", "", match).strip()
            if cleaned and cleaned not in results["input_json_paths"]:
                results["input_json_paths"].append(cleaned)

        # Search for output.json patterns
        output_matches = re.findall(r"[A-Za-z]:\\[^\"<>|*?\x00]+output\.json", text)
        for match in output_matches:
            cleaned = re.sub(r"[\x00-\x1f]", "", match).strip()
            if cleaned and cleaned not in results["output_json_paths"]:
                results["output_json_paths"].append(cleaned)

    except Exception as e:
        print(f"  Warning: Could not parse binlog binary content: {e}", file=sys.stderr)

    return results


def parse_input_json(input_json_path: Path) -> dict[str, Any]:
    """Parse a XAML compiler input.json file."""
    result: dict[str, Any] = {
        "xaml_pages": [],
        "application_definition": None,
        "root_namespace": None,
    }

    if not input_json_path.exists():
        return result

    try:
        with open(input_json_path) as f:
            data = json.load(f)

        if "XamlPages" in data:
            for page in data["XamlPages"]:
                result["xaml_pages"].append(
                    {
                        "full_path": page.get("FullPath", ""),
                        "item_spec": page.get("ItemSpec", ""),
                        "link": page.get("Link", ""),
                    }
                )

        if "ApplicationDefinition" in data:
            app_def = data["ApplicationDefinition"]
            if isinstance(app_def, dict):
                result["application_definition"] = app_def.get("FullPath", "")
            else:
                result["application_definition"] = str(app_def)

        if "RootNamespace" in data:
            result["root_namespace"] = data["RootNamespace"]

    except Exception as e:
        print(f"  Warning: Could not parse input.json: {e}", file=sys.stderr)

    return result


def detect_nested_views_xaml(xaml_pages: list[dict[str, Any]]) -> list[dict[str, str]]:
    """Detect XAML files in nested Views subfolders (GitHub #10947)."""
    nested = []

    # Pattern: Views\subfolder\file.xaml (more than one level under Views)
    pattern = re.compile(r"\\Views\\[^\\]+\\[^\\]+\.xaml$", re.IGNORECASE)

    for page in xaml_pages:
        full_path = page.get("full_path", "")
        if not full_path:
            continue

        if pattern.search(full_path):
            filename = os.path.basename(full_path)
            nested.append(
                {
                    "full_path": full_path,
                    "issue": "XAML file in nested Views subfolder may cause XamlCompiler.exe to fail silently (GitHub #10947)",
                    "recommendation": f"Move to Views/ root: {filename}",
                }
            )

    return nested


def analyze_binlog(binlog_path: Path, repo_root: Path) -> AnalysisResults:
    """Perform full binlog analysis."""
    results = AnalysisResults(
        binlog_path=str(binlog_path),
        timestamp=datetime.now().isoformat(),
    )

    # Step 1: Search binlog content - try StructuredLogger first, fall back to binary
    print("Step 1: Searching binlog for XamlCompiler invocations...")

    # Method 0: Try StructuredLogger CLI (preferred)
    binlog_content = search_with_structured_logger(binlog_path)

    # Method 1: Fall back to binary parsing if StructuredLogger not available
    if binlog_content is None:
        print("  Falling back to binary content parsing...")
        binlog_content = search_binlog_content(binlog_path)

    if binlog_content["xaml_compiler_commands"]:
        results.xaml_compiler_tasks = binlog_content["xaml_compiler_commands"]
        print(f"  Found {len(results.xaml_compiler_tasks)} XamlCompiler references")
    else:
        print("  No XamlCompiler references found in binlog")
        results.warnings.append("No XamlCompiler.exe invocations found in binlog")

    # Step 2: Find and parse input.json files
    print("\nStep 2: Locating input.json files...")
    input_json_paths = binlog_content["input_json_paths"]

    if not input_json_paths:
        # Fallback: search in known obj locations
        obj_dirs = [
            repo_root / "src" / "VoiceStudio.App" / "obj",
            repo_root / "obj",
        ]

        for obj_dir in obj_dirs:
            if obj_dir.exists():
                for input_json in obj_dir.rglob("input.json"):
                    input_json_paths.append(str(input_json))

    results.input_json_files = list(set(input_json_paths))

    if input_json_paths:
        print(f"  Found {len(input_json_paths)} input.json file(s)")

        for input_json_path in input_json_paths:
            path = Path(input_json_path)
            if path.exists():
                print(f"  Parsing: {input_json_path}")
                parsed = parse_input_json(path)
                results.xaml_pages.extend(parsed["xaml_pages"])
    else:
        print("  No input.json files found")
        results.warnings.append(
            "No input.json files found - build may not have reached XAML compilation"
        )

    # Step 3: Analyze XAML pages for known issues
    print("\nStep 3: Analyzing XAML pages for known issues...")
    unique_pages = list({p["full_path"] for p in results.xaml_pages if p.get("full_path")})
    print(f"  Total XAML pages: {len(unique_pages)}")

    # Detect nested Views
    nested_views = detect_nested_views_xaml(results.xaml_pages)
    results.nested_views_xaml = nested_views

    if nested_views:
        print(f"  WARNING: Found {len(nested_views)} XAML file(s) in nested Views subfolders!")
        for nested in nested_views:
            results.warnings.append(nested["issue"])
    else:
        print("  No nested Views subfolders detected (good)")

    # Step 4: Generate recommendations
    print("\nStep 4: Generating recommendations...")

    if nested_views:
        results.recommendations.append(
            "CRITICAL: Flatten XAML files in nested Views subfolders to Views/ root"
        )
        results.recommendations.append(
            "See GitHub #10947: https://github.com/microsoft/microsoft-ui-xaml/issues/10947"
        )

    if not results.warnings:
        results.recommendations.extend(
            [
                "Build appears healthy. If issues persist:",
                "  1. Open binlog in MSBuild Structured Log Viewer",
                "  2. Search for 'XamlCompiler' to find exact task",
                "  3. Check for TextElement.* attached properties on ContentPresenter",
                "  4. Run xaml-binary-search.ps1 to isolate problematic file",
            ]
        )

    return results


def format_results(results: AnalysisResults, output_format: str) -> str:
    """Format analysis results in the specified format and return as string."""
    lines: list[str] = []

    lines.append("=" * 60)
    lines.append("  XAML COMPILER BINLOG ANALYSIS")
    lines.append("=" * 60)
    lines.append("")
    lines.append(f"Binlog: {results.binlog_path}")
    lines.append(f"Analyzed: {results.timestamp}")
    lines.append("")

    if output_format == "json":
        return json.dumps(results.to_dict(), indent=2)

    if output_format == "summary":
        unique_pages = list({p["full_path"] for p in results.xaml_pages if p.get("full_path")})
        lines.append(f"XAML Pages     : {len(unique_pages)}")
        lines.append(f"Nested Views   : {len(results.nested_views_xaml)}")
        lines.append(f"Warnings       : {len(results.warnings)}")
        lines.append("")
        if results.recommendations:
            lines.append("Recommendations:")
            for rec in results.recommendations:
                lines.append(f"  - {rec}")
        return "\n".join(lines)

    # Text output with LIKELY CULPRIT section
    if results.xaml_pages:
        first_xaml = results.xaml_pages[0].get("full_path", "")
        if first_xaml:
            lines.append("=" * 60)
            lines.append("  LIKELY CULPRIT (first XAML in input.json)")
            lines.append("=" * 60)
            lines.append("")
            lines.append(f"  {first_xaml}")
            lines.append("")

            # Check if this file is nested
            if re.search(r"\\Views\\[^\\]+\\[^\\]+\.xaml$", first_xaml, re.IGNORECASE):
                lines.append("  This file is in a nested Views subfolder (GitHub #10947).")
                lines.append("")
                filename = os.path.basename(first_xaml)
                parent_dir = os.path.dirname(os.path.dirname(first_xaml))
                lines.append("  FIX: Move to Views root:")
                lines.append(f'    Move-Item "{first_xaml}" "{parent_dir}\\{filename}"')
            lines.append("")

    if results.nested_views_xaml:
        lines.append("=" * 60)
        lines.append("  NESTED VIEWS XAML (likely cause of silent failures)")
        lines.append("=" * 60)
        lines.append("")
        for nested in results.nested_views_xaml:
            full_path = nested["full_path"]
            lines.append(f"  - {full_path}")
            lines.append(f"    Recommendation: {nested['recommendation']}")
            lines.append("")
            # Generate specific Move-Item command
            filename = os.path.basename(full_path)
            parent_dir = os.path.dirname(os.path.dirname(full_path))
            lines.append(f'    FIX: Move-Item "{full_path}" "{parent_dir}\\{filename}"')
            lines.append("")

        lines.append("  Alternative: Enable automatic flattener in .csproj:")
        lines.append("    <EnableViewsFlattener>true</EnableViewsFlattener>")
        lines.append("")

    if results.warnings:
        lines.append("WARNINGS:")
        for warning in results.warnings:
            lines.append(f"  - {warning}")
        lines.append("")

    lines.append("RECOMMENDATIONS:")
    for rec in results.recommendations:
        lines.append(f"  {rec}")
    lines.append("")

    lines.append("NEXT STEPS:")
    lines.append("  1. Open binlog in MSBuild Structured Log Viewer:")
    lines.append("     https://msbuildlog.com/")
    lines.append("")
    lines.append("  2. Search for 'XamlCompiler.exe' to find compiler task")
    lines.append("")
    lines.append("  3. If exit code 1 with no error, check:")
    lines.append("     - XAML Change Protocol: docs/developer/XAML_CHANGE_PROTOCOL.md")
    lines.append("     - Run: .\\scripts\\xaml-binary-search.ps1")
    lines.append("")

    return "\n".join(lines)


def print_results(results: AnalysisResults, output_format: str, output_file: str = "") -> None:
    """Print analysis results and optionally write to file."""
    print("\n" + "=" * 60)
    print("  ANALYSIS RESULTS")
    print("=" * 60 + "\n")

    output_content = format_results(results, output_format)
    print(output_content)

    # Write to file if specified
    if output_file and output_file.strip():
        output_path = Path(output_file)
        # Ensure parent directory exists
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(output_content, encoding="utf-8")
        print(f"\nAnalysis written to: {output_file}")


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Analyze MSBuild binlog for XAML compiler issues"
    )
    parser.add_argument(
        "--binlog",
        "-b",
        default="",
        help="Path to binlog file (supports wildcards). Default: most recent in .buildlogs/",
    )
    parser.add_argument(
        "--format",
        "-f",
        choices=["text", "json", "summary"],
        default="text",
        help="Output format (default: text)",
    )
    parser.add_argument(
        "--repo-root",
        "-r",
        default="",
        help="Repository root path. Default: auto-detect from script location",
    )
    parser.add_argument(
        "--output-file",
        "-o",
        default="",
        help="Write analysis results to file (for CI consumption)",
    )

    args = parser.parse_args()

    # Determine repo root
    if args.repo_root:
        repo_root = Path(args.repo_root)
    else:
        # Assume script is in scripts/ directory
        repo_root = Path(__file__).parent.parent

    if not repo_root.exists():
        print(f"ERROR: Repository root not found: {repo_root}", file=sys.stderr)
        return 1

    print()
    print("=" * 60)
    print("  MSBuild Binlog Analysis for XAML Compiler Issues")
    print("=" * 60)
    print()

    # Find binlog
    binlog_path = find_binlog(args.binlog, repo_root)
    if not binlog_path:
        print(f"ERROR: No binlog files found in {repo_root / '.buildlogs'}", file=sys.stderr)
        print()
        print("Run a diagnostic build first:")
        print("  .\\scripts\\build-with-binlog.ps1")
        return 1

    print(f"Binlog: {binlog_path}")
    print(f"Size  : {binlog_path.stat().st_size / 1024 / 1024:.2f} MB")
    print()

    # Analyze
    results = analyze_binlog(binlog_path, repo_root)

    # Print results
    print_results(results, args.format, args.output_file)

    # Return exit code based on findings
    return 1 if results.nested_views_xaml else 0


if __name__ == "__main__":
    sys.exit(main())
