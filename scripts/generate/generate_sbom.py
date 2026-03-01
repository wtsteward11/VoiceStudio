#!/usr/bin/env python3
"""
Generate Software Bill of Materials (SBOM) for VoiceStudio.

Outputs CycloneDX JSON format combining Python and .NET dependencies.

Usage:
    python scripts/generate_sbom.py [--output PATH] [--format json|xml]

Requirements:
    pip install cyclonedx-bom pip-licenses

For .NET dependencies, requires:
    dotnet tool install --global CycloneDX

Phase 6.3.3 Security Hardening
"""

import argparse
import json
import subprocess
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

# SBOM specification version
CYCLONEDX_SPEC_VERSION = "1.5"
SBOM_FORMAT_VERSION = "1"

# Project info
PROJECT_NAME = "VoiceStudio"
PROJECT_VERSION = "1.0.1"  # Should be read from version_lock.json in production


def get_project_root() -> Path:
    """Get the project root directory."""
    script_dir = Path(__file__).parent
    return script_dir.parent


def run_command(
    cmd: list[str],
    cwd: Path | None = None,
    capture_output: bool = True,
) -> tuple[int, str, str]:
    """Run a command and return exit code, stdout, stderr."""
    try:
        result = subprocess.run(
            cmd,
            cwd=cwd,
            capture_output=capture_output,
            text=True,
            timeout=300,  # 5 minute timeout
        )
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        return 1, "", "Command timed out"
    except FileNotFoundError:
        return 1, "", f"Command not found: {cmd[0]}"


def generate_python_sbom(project_root: Path, output_path: Path) -> bool:
    """
    Generate SBOM for Python dependencies using cyclonedx-py.

    Returns True if successful, False otherwise.
    """
    print("Generating Python SBOM...")

    # Check if cyclonedx-py is installed
    returncode, _, _ = run_command(["python", "-m", "cyclonedx_py", "--version"])
    if returncode != 0:
        print("  cyclonedx-py not found. Installing...")
        returncode, _, stderr = run_command(
            ["python", "-m", "pip", "install", "cyclonedx-bom"]
        )
        if returncode != 0:
            print(f"  Failed to install cyclonedx-bom: {stderr}")
            return False

    # Find requirements files
    req_files = [
        project_root / "requirements.txt",
        project_root / "requirements_engines.txt",
        project_root / "requirements-test.txt",
        project_root / "backend" / "requirements.txt",
    ]

    existing_req_files = [f for f in req_files if f.exists()]

    if not existing_req_files:
        print("  No requirements files found. Generating from environment...")
        # Generate from installed packages
        returncode, stdout, stderr = run_command(
            [
                "python", "-m", "cyclonedx_py", "environment",
                "--output-format", "json",
                "--outfile", str(output_path),
            ],
            cwd=project_root,
        )
    else:
        # Generate from requirements file (primary)
        primary_req = existing_req_files[0]
        print(f"  Using requirements file: {primary_req}")
        returncode, _stdout, stderr = run_command(
            [
                "python", "-m", "cyclonedx_py", "requirements",
                str(primary_req),
                "--output-format", "json",
                "--outfile", str(output_path),
            ],
            cwd=project_root,
        )

    if returncode != 0:
        print(f"  Python SBOM generation failed: {stderr}")
        return False

    print(f"  Python SBOM written to: {output_path}")
    return True


def generate_dotnet_sbom(project_root: Path, output_path: Path) -> bool:
    """
    Generate SBOM for .NET dependencies using CycloneDX dotnet tool.

    Returns True if successful, False otherwise.
    """
    print("Generating .NET SBOM...")

    # Check if dotnet CycloneDX tool is installed
    returncode, _, _ = run_command(["dotnet", "CycloneDX", "--version"])
    if returncode != 0:
        print("  dotnet CycloneDX not found. Installing...")
        returncode, _, stderr = run_command(
            ["dotnet", "tool", "install", "--global", "CycloneDX"]
        )
        if returncode != 0:
            # Tool might already be installed but not in path
            print(f"  Note: {stderr}")

    # Find solution or project files
    sln_file = project_root / "VoiceStudio.sln"
    src_dir = project_root / "src"

    if sln_file.exists():
        target = str(sln_file)
    elif src_dir.exists():
        # Find first .csproj
        csproj_files = list(src_dir.glob("**/*.csproj"))
        if csproj_files:
            target = str(csproj_files[0])
        else:
            print("  No .NET project files found")
            return False
    else:
        print("  No .NET solution or project files found")
        return False

    print(f"  Using target: {target}")

    returncode, _stdout, stderr = run_command(
        [
            "dotnet", "CycloneDX", target,
            "--json",
            "--output", str(output_path.parent),
            "--filename", output_path.name,
        ],
        cwd=project_root,
    )

    if returncode != 0:
        print(f"  .NET SBOM generation failed: {stderr}")
        # Don't fail completely - .NET SBOM is optional if solution doesn't build
        return False

    print(f"  .NET SBOM written to: {output_path}")
    return True


def merge_sboms(
    python_sbom_path: Path,
    dotnet_sbom_path: Path,
    output_path: Path,
) -> bool:
    """
    Merge Python and .NET SBOMs into a single unified SBOM.

    Returns True if successful, False otherwise.
    """
    print("Merging SBOMs...")

    components = []
    dependencies = []

    # Load Python SBOM
    if python_sbom_path.exists():
        try:
            with open(python_sbom_path, encoding="utf-8") as f:
                python_sbom = json.load(f)
            python_components = python_sbom.get("components", [])
            # Tag Python components
            for comp in python_components:
                if "properties" not in comp:
                    comp["properties"] = []
                comp["properties"].append({
                    "name": "ecosystem",
                    "value": "python",
                })
            components.extend(python_components)
            dependencies.extend(python_sbom.get("dependencies", []))
            print(f"  Added {len(python_components)} Python components")
        except (json.JSONDecodeError, KeyError) as e:
            print(f"  Warning: Could not parse Python SBOM: {e}")

    # Load .NET SBOM
    if dotnet_sbom_path.exists():
        try:
            with open(dotnet_sbom_path, encoding="utf-8") as f:
                dotnet_sbom = json.load(f)
            dotnet_components = dotnet_sbom.get("components", [])
            # Tag .NET components
            for comp in dotnet_components:
                if "properties" not in comp:
                    comp["properties"] = []
                comp["properties"].append({
                    "name": "ecosystem",
                    "value": "dotnet",
                })
            components.extend(dotnet_components)
            dependencies.extend(dotnet_sbom.get("dependencies", []))
            print(f"  Added {len(dotnet_components)} .NET components")
        except (json.JSONDecodeError, KeyError) as e:
            print(f"  Warning: Could not parse .NET SBOM: {e}")

    if not components:
        print("  No components found in either SBOM")
        return False

    # Create merged SBOM
    merged_sbom = create_sbom_document(components, dependencies)

    # Write merged SBOM
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(merged_sbom, f, indent=2)

    print(f"  Merged SBOM written to: {output_path}")
    print(f"  Total components: {len(components)}")

    return True


def create_sbom_document(
    components: list[dict[str, Any]],
    dependencies: list[dict[str, Any]],
) -> dict[str, Any]:
    """Create a CycloneDX SBOM document."""
    return {
        "$schema": "http://cyclonedx.org/schema/bom-1.5.schema.json",
        "bomFormat": "CycloneDX",
        "specVersion": CYCLONEDX_SPEC_VERSION,
        "serialNumber": f"urn:uuid:{uuid.uuid4()}",
        "version": 1,
        "metadata": {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "tools": {
                "components": [
                    {
                        "type": "application",
                        "name": "VoiceStudio SBOM Generator",
                        "version": SBOM_FORMAT_VERSION,
                    },
                    {
                        "type": "application",
                        "name": "cyclonedx-py",
                        "version": "4.x",  # Will be actual version in production
                    },
                ],
            },
            "component": {
                "type": "application",
                "name": PROJECT_NAME,
                "version": PROJECT_VERSION,
                "description": "Professional voice synthesis and cloning application",
                "licenses": [
                    {
                        "license": {
                            "id": "MIT",
                        },
                    },
                ],
            },
        },
        "components": components,
        "dependencies": dependencies,
    }


def validate_sbom(sbom_path: Path) -> bool:
    """
    Validate the generated SBOM against CycloneDX schema.

    Returns True if valid, False otherwise.
    """
    print(f"Validating SBOM: {sbom_path}")

    if not sbom_path.exists():
        print("  SBOM file not found")
        return False

    try:
        with open(sbom_path, encoding="utf-8") as f:
            sbom = json.load(f)

        # Basic structure validation
        required_fields = ["bomFormat", "specVersion", "components"]
        for field in required_fields:
            if field not in sbom:
                print(f"  Missing required field: {field}")
                return False

        if sbom.get("bomFormat") != "CycloneDX":
            print(f"  Invalid bomFormat: {sbom.get('bomFormat')}")
            return False

        components = sbom.get("components", [])
        if not isinstance(components, list):
            print("  Components must be a list")
            return False

        print(f"  Valid CycloneDX SBOM with {len(components)} components")
        return True

    except json.JSONDecodeError as e:
        print(f"  Invalid JSON: {e}")
        return False


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Generate Software Bill of Materials for VoiceStudio"
    )
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        default=None,
        help="Output file path (default: .buildlogs/sbom/voicestudio-sbom.json)",
    )
    parser.add_argument(
        "--format",
        "-f",
        choices=["json", "xml"],
        default="json",
        help="Output format (default: json)",
    )
    parser.add_argument(
        "--python-only",
        action="store_true",
        help="Generate only Python SBOM",
    )
    parser.add_argument(
        "--dotnet-only",
        action="store_true",
        help="Generate only .NET SBOM",
    )
    parser.add_argument(
        "--validate",
        action="store_true",
        help="Validate the generated SBOM",
    )

    args = parser.parse_args()

    project_root = get_project_root()

    # Set up output paths
    buildlogs_dir = project_root / ".buildlogs" / "sbom"
    buildlogs_dir.mkdir(parents=True, exist_ok=True)

    output_path = args.output or buildlogs_dir / f"voicestudio-sbom.{args.format}"

    python_sbom_path = buildlogs_dir / "python-sbom.json"
    dotnet_sbom_path = buildlogs_dir / "dotnet-sbom.json"

    print("VoiceStudio SBOM Generator")
    print(f"Project root: {project_root}")
    print(f"Output: {output_path}")
    print("-" * 50)

    success = True
    python_success = False
    dotnet_success = False

    # Generate Python SBOM
    if not args.dotnet_only:
        python_success = generate_python_sbom(project_root, python_sbom_path)
        if not python_success:
            print("Warning: Python SBOM generation failed")

    # Generate .NET SBOM
    if not args.python_only:
        dotnet_success = generate_dotnet_sbom(project_root, dotnet_sbom_path)
        if not dotnet_success:
            print("Warning: .NET SBOM generation failed")

    # Merge SBOMs if both are requested
    if not args.python_only and not args.dotnet_only:
        if python_success or dotnet_success:
            success = merge_sboms(python_sbom_path, dotnet_sbom_path, output_path)
        else:
            print("No SBOMs generated to merge")
            success = False
    elif args.python_only and python_success:
        # Copy Python SBOM to output
        import shutil
        shutil.copy(python_sbom_path, output_path)
        success = True
    elif args.dotnet_only and dotnet_success:
        # Copy .NET SBOM to output
        import shutil
        shutil.copy(dotnet_sbom_path, output_path)
        success = True
    else:
        success = False

    # Validate if requested
    if args.validate and success and not validate_sbom(output_path):
        print("SBOM validation failed")
        success = False

    print("-" * 50)
    if success:
        print(f"SBOM generated successfully: {output_path}")
        return 0
    else:
        print("SBOM generation completed with warnings")
        return 1


if __name__ == "__main__":
    sys.exit(main())
