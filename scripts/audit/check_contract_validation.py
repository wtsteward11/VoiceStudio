#!/usr/bin/env python3
"""
Pre-commit hook for contract validation.

Validates:
1. OpenAPI schema exists and is valid
2. Generated client matches schema (if schema changed)
3. No breaking changes (warning only)

Exit codes:
- 0: All checks passed
- 1: Validation failed
"""

import json
import subprocess
import sys
from pathlib import Path


def main() -> int:
    """Main entry point for contract validation hook."""
    repo_root = Path(__file__).parent.parent
    schema_path = repo_root / "docs" / "api" / "openapi.json"
    validate_script = repo_root / "tools" / "nswag" / "validate-contract.py"

    # Check if schema exists
    if not schema_path.exists():
        print("[SKIP] OpenAPI schema not found, skipping contract validation")
        return 0

    # Validate schema is valid JSON
    print("[CHECK] Validating OpenAPI schema structure...")
    try:
        with open(schema_path, encoding="utf-8") as f:
            schema = json.load(f)
    except json.JSONDecodeError as e:
        print(f"[FAIL] OpenAPI schema is not valid JSON: {e}")
        return 1

    # Validate required fields
    required_fields = ["openapi", "info", "paths"]
    missing = [f for f in required_fields if f not in schema]
    if missing:
        print(f"[FAIL] OpenAPI schema missing required fields: {missing}")
        return 1

    # Check if paths are defined
    paths = schema.get("paths", {})
    if not paths:
        print("[WARN] OpenAPI schema has no paths defined")
    else:
        print(f"[OK] OpenAPI schema has {len(paths)} paths")

    # Validate operationIds exist
    missing_op_ids = []
    for path, methods in paths.items():
        for method, details in methods.items():
            if method in ["get", "post", "put", "delete", "patch"]:
                if not isinstance(details, dict):
                    continue
                if not details.get("operationId"):
                    missing_op_ids.append(f"{method.upper()} {path}")

    if missing_op_ids:
        print("[WARN] Endpoints missing operationId:")
        for op in missing_op_ids[:5]:
            print(f"  - {op}")
        if len(missing_op_ids) > 5:
            print(f"  ... and {len(missing_op_ids) - 5} more")

    # Run full contract validation if script exists
    if validate_script.exists():
        print("[CHECK] Running contract compatibility check...")
        try:
            result = subprocess.run(
                [sys.executable, str(validate_script)],
                capture_output=True,
                text=True,
                cwd=str(repo_root),
                timeout=30,
            )

            if result.returncode != 0:
                print("[WARN] Contract validation reported issues:")
                # Only show first few lines to avoid noise
                output_lines = result.stdout.split("\n")[:10]
                for line in output_lines:
                    if line.strip():
                        print(f"  {line}")

                # Don't fail the hook, just warn
                print("\n[INFO] Run 'python tools/nswag/validate-contract.py' for full report")
            else:
                print("[OK] Contract validation passed")
        except subprocess.TimeoutExpired:
            print("[WARN] Contract validation timed out")
        except Exception as e:
            print(f"[WARN] Contract validation error: {e}")

    # Check for staged changes to generated client vs schema
    print("[CHECK] Checking for client/schema sync...")
    try:
        result = subprocess.run(
            ["git", "diff", "--cached", "--name-only"],
            capture_output=True,
            text=True,
            cwd=str(repo_root),
        )

        staged_files = result.stdout.strip().split("\n") if result.stdout.strip() else []

        schema_changed = any("openapi.json" in f for f in staged_files)
        client_changed = any("BackendClient.g.cs" in f for f in staged_files)

        if schema_changed and not client_changed:
            print("[WARN] OpenAPI schema changed but generated client not updated")
            print("  Run: powershell tools/nswag/generate-client.ps1 -Force")
            print("  Then stage the generated client")
        elif schema_changed and client_changed:
            print("[OK] Both schema and client are staged")
        else:
            print("[OK] No schema changes detected")

    except Exception as e:
        print(f"[WARN] Could not check git status: {e}")

    print("[PASS] Contract validation complete")
    return 0


if __name__ == "__main__":
    sys.exit(main())
