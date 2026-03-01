#!/usr/bin/env python3
"""
Generate TOOLS_REGISTRY.md from script headers and docstrings.

GAP-X03: Tools Registry Generator

This script scans the scripts/ and tools/ directories for Python and PowerShell
scripts, extracts metadata from their docstrings and headers, and generates
a comprehensive markdown registry document.

Usage:
    python scripts/generate_tools_registry.py
    python scripts/generate_tools_registry.py --output docs/developer/TOOLS_REGISTRY.md
    python scripts/generate_tools_registry.py --check  # CI mode: exits 1 if out of date
"""

import argparse
import ast
import re
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path
from typing import Any, Optional

# Category detection patterns
CATEGORY_PATTERNS = {
    "Build": [
        r"build",
        r"compile",
        r"package",
        r"bundle",
        r"msix",
        r"installer",
        r"cmake",
    ],
    "Test": [
        r"test",
        r"fixture",
        r"mock",
        r"pytest",
        r"unittest",
        r"spec",
    ],
    "Verification": [
        r"verify",
        r"gate",
        r"ledger",
        r"validate",
        r"check",
        r"guard",
    ],
    "Quality": [
        r"lint",
        r"format",
        r"style",
        r"ruff",
        r"black",
        r"isort",
        r"mypy",
        r"pylint",
    ],
    "Context": [
        r"context",
        r"memory",
        r"ai",
        r"prompt",
        r"openai",
        r"openmemory",
    ],
    "Scaffolds": [
        r"generator",
        r"scaffold",
        r"template",
        r"create",
        r"init",
        r"new",
    ],
    "Debug": [
        r"debug",
        r"diagnose",
        r"profile",
        r"trace",
        r"log",
        r"troubleshoot",
    ],
    "Release": [
        r"release",
        r"deploy",
        r"version",
        r"changelog",
        r"publish",
        r"tag",
    ],
    "Data": [
        r"database",
        r"db",
        r"migrate",
        r"seed",
        r"schema",
        r"sql",
    ],
    "Audio": [
        r"audio",
        r"wav",
        r"mp3",
        r"voice",
        r"speech",
        r"tts",
        r"synthesis",
    ],
    "Utility": [
        r"util",
        r"helper",
        r"common",
        r"misc",
    ],
    "Documentation": [
        r"doc",
        r"readme",
        r"markdown",
        r"registry",
    ],
    "Onboarding": [
        r"onboard",
        r"packet",
        r"role",
        r"wizard",
    ],
    "Development": [
        r"dev",
        r"setup",
        r"install",
        r"env",
        r"requirements",
    ],
}


def categorize_tool(filepath: Path, content: str = "") -> str:
    """Determine tool category based on path and content."""
    path_str = str(filepath).lower()
    filename = filepath.stem.lower()
    
    # Check path patterns first
    for category, patterns in CATEGORY_PATTERNS.items():
        for pattern in patterns:
            if re.search(pattern, path_str):
                return category
            if re.search(pattern, filename):
                return category
    
    # Check content patterns
    content_lower = content.lower()
    for category, patterns in CATEGORY_PATTERNS.items():
        for pattern in patterns:
            if re.search(pattern, content_lower[:500]):  # First 500 chars
                return category
    
    return "Utility"


def extract_first_line(docstring: str) -> str:
    """Extract the first meaningful line from a docstring."""
    if not docstring:
        return "No description available"
    
    lines = docstring.strip().split("\n")
    for line in lines:
        line = line.strip()
        if line and not line.startswith(("Args:", "Returns:", "Usage:", "Example:")):
            # Clean up common prefixes
            line = re.sub(r"^(Module|Script|Tool)\s*[:.-]\s*", "", line, flags=re.IGNORECASE)
            return line[:80]  # Truncate long descriptions
    
    return "No description available"


def extract_usage(docstring: str) -> Optional[str]:
    """Extract usage example from docstring."""
    if not docstring:
        return None
    
    # Look for Usage: section
    match = re.search(r"Usage:\s*\n((?:[ \t]+.+\n?)+)", docstring, re.IGNORECASE)
    if match:
        usage_lines = match.group(1).strip().split("\n")
        return usage_lines[0].strip() if usage_lines else None
    
    return None


def extract_python_metadata(filepath: Path) -> dict[str, Any]:
    """Extract metadata from Python script."""
    try:
        content = filepath.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        content = ""
    
    docstring = ""
    try:
        tree = ast.parse(content)
        docstring = ast.get_docstring(tree) or ""
    except SyntaxError:
        # Try to extract from comment headers
        lines = content.split("\n")[:20]
        comment_lines = []
        for line in lines:
            if line.startswith("#") and not line.startswith("#!"):
                comment_lines.append(line[1:].strip())
        docstring = " ".join(comment_lines)
    
    return {
        "docstring": docstring,
        "content": content,
    }


def extract_powershell_metadata(filepath: Path) -> dict[str, Any]:
    """Extract metadata from PowerShell script."""
    try:
        content = filepath.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        content = ""
    
    docstring = ""
    
    # Look for <# ... #> block comment
    match = re.search(r"<#(.*?)#>", content, re.DOTALL)
    if match:
        docstring = match.group(1).strip()
    else:
        # Try single-line comments at the top
        lines = content.split("\n")[:20]
        comment_lines = []
        for line in lines:
            if line.strip().startswith("#"):
                comment_lines.append(line.strip()[1:].strip())
        docstring = " ".join(comment_lines)
    
    return {
        "docstring": docstring,
        "content": content,
    }


def extract_tool_metadata(filepath: Path) -> dict[str, Any]:
    """Extract metadata from script docstring and comments."""
    if filepath.suffix == ".py":
        metadata = extract_python_metadata(filepath)
    elif filepath.suffix == ".ps1":
        metadata = extract_powershell_metadata(filepath)
    else:
        metadata = {"docstring": "", "content": ""}
    
    docstring = metadata["docstring"]
    content = metadata["content"]
    
    # Determine relative path from repo root
    try:
        repo_root = Path(__file__).parent.parent
        rel_path = filepath.relative_to(repo_root)
    except ValueError:
        rel_path = filepath
    
    return {
        "path": str(rel_path).replace("\\", "/"),
        "name": filepath.stem,
        "suffix": filepath.suffix,
        "category": categorize_tool(filepath, content),
        "description": extract_first_line(docstring),
        "usage": extract_usage(docstring),
    }


def scan_directories(repo_root: Path) -> list[dict[str, Any]]:
    """Scan directories for tools and scripts."""
    tools = []
    patterns = [
        "scripts/**/*.py",
        "scripts/**/*.ps1",
        "tools/**/*.py",
        "tools/**/*.ps1",
    ]
    
    for pattern in patterns:
        for filepath in repo_root.glob(pattern):
            # Skip common non-tool files
            if any(skip in str(filepath) for skip in [
                "__pycache__",
                "__init__.py",
                ".pyc",
                "conftest.py",
                ".pytest",
            ]):
                continue
            
            tools.append(extract_tool_metadata(filepath))
    
    return tools


def generate_registry_content(tools: list[dict[str, Any]]) -> str:
    """Generate markdown content for the registry."""
    output = []
    
    # Header
    output.append("# VoiceStudio Tools Registry\n")
    output.append("\n")
    output.append("> **Auto-generated**: Do not edit manually.\n")
    output.append(f"> **Generated**: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
    output.append(f"> **Total tools**: {len(tools)}\n")
    output.append("\n")
    output.append("This registry is generated by `scripts/generate_tools_registry.py`.\n")
    output.append("Run the script to update after adding or modifying tools.\n")
    output.append("\n")
    
    # Table of contents
    output.append("## Categories\n")
    output.append("\n")
    output.append("| Category | Description | Count |\n")
    output.append("|----------|-------------|-------|\n")
    
    category_descriptions = {
        "Audio": "Audio processing, analysis, synthesis",
        "Build": "Build automation, compilation, packaging",
        "Context": "AI context management, memory",
        "Data": "Database, migrations, seeding",
        "Debug": "Diagnostics, profiling, troubleshooting",
        "Development": "Development setup, environment",
        "Documentation": "Documentation generation",
        "Onboarding": "Role onboarding, packet generation",
        "Quality": "Linting, formatting, analysis",
        "Release": "Versioning, changelog, deployment",
        "Scaffolds": "Code generators, templates",
        "Test": "Test runners, fixtures, mocking",
        "Utility": "General utilities and helpers",
        "Verification": "Gate checks, ledger validation",
    }
    
    # Group by category
    by_category: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for tool in tools:
        by_category[tool["category"]].append(tool)
    
    for category in sorted(by_category.keys()):
        desc = category_descriptions.get(category, "")
        count = len(by_category[category])
        anchor = category.lower().replace(" ", "-")
        output.append(f"| [{category}](#{anchor}) | {desc} | {count} |\n")
    
    output.append("\n")
    output.append("---\n")
    output.append("\n")
    
    # Tool listings by category
    for category in sorted(by_category.keys()):
        output.append(f"## {category}\n")
        output.append("\n")
        output.append("| Tool | Description | Path |\n")
        output.append("|------|-------------|------|\n")
        
        for tool in sorted(by_category[category], key=lambda t: t["name"]):
            name = tool["name"]
            desc = tool["description"][:60]
            if len(tool["description"]) > 60:
                desc += "..."
            path = tool["path"]
            output.append(f"| `{name}` | {desc} | `{path}` |\n")
        
        output.append("\n")
    
    # Footer
    output.append("---\n")
    output.append("\n")
    output.append("## Updating This Registry\n")
    output.append("\n")
    output.append("To regenerate this file after adding or modifying tools:\n")
    output.append("\n")
    output.append("```bash\n")
    output.append("python scripts/generate_tools_registry.py\n")
    output.append("```\n")
    output.append("\n")
    output.append("To verify the registry is up-to-date (CI mode):\n")
    output.append("\n")
    output.append("```bash\n")
    output.append("python scripts/generate_tools_registry.py --check\n")
    output.append("```\n")
    
    return "".join(output)


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Generate TOOLS_REGISTRY.md from script metadata"
    )
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        default=Path("docs/developer/TOOLS_REGISTRY.md"),
        help="Output file path (default: docs/developer/TOOLS_REGISTRY.md)",
    )
    parser.add_argument(
        "--check",
        "-c",
        action="store_true",
        help="CI mode: check if registry is up-to-date (exits 1 if not)",
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="store_true",
        help="Verbose output",
    )
    args = parser.parse_args()
    
    # Determine repo root
    repo_root = Path(__file__).parent.parent
    
    # Scan for tools
    print(f"Scanning {repo_root} for tools...")
    tools = scan_directories(repo_root)
    print(f"Found {len(tools)} tools")
    
    if args.verbose:
        for tool in sorted(tools, key=lambda t: t["path"]):
            print(f"  - {tool['path']}: {tool['category']}")
    
    # Generate content
    content = generate_registry_content(tools)
    
    # Output path
    output_path = repo_root / args.output
    
    if args.check:
        # CI mode: check if current content matches
        if output_path.exists():
            existing = output_path.read_text(encoding="utf-8")
            # Compare ignoring timestamp line
            def normalize(text: str) -> str:
                return re.sub(r"> \*\*Generated\*\*:.*\n", "", text)
            
            if normalize(existing) == normalize(content):
                print("✓ Registry is up-to-date")
                sys.exit(0)
            else:
                print("✗ Registry is out-of-date. Run 'python scripts/generate_tools_registry.py' to update.")
                sys.exit(1)
        else:
            print(f"✗ Registry does not exist at {output_path}")
            sys.exit(1)
    
    # Write output
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(content, encoding="utf-8")
    print(f"Generated {output_path} ({len(tools)} tools)")


if __name__ == "__main__":
    main()
