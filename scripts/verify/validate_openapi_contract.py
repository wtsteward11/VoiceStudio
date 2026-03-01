#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
OpenAPI Contract Validation Script.

Validates that the OpenAPI specification matches the actual backend routes.
This prevents spec drift where documentation becomes out of sync with implementation.

Usage:
    python scripts/validate_openapi_contract.py
    python scripts/validate_openapi_contract.py --update  # Update spec from routes
"""

import argparse
import json
import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT


def get_openapi_spec_paths() -> tuple[dict[str, set[str]], Path]:
    """
    Load the OpenAPI spec and extract all paths with their methods.

    Returns:
        Tuple of (dict mapping path to set of methods, spec file path)
    """
    spec_paths = [
        PROJECT_ROOT / "docs" / "api" / "openapi.json",
        PROJECT_ROOT / "docs" / "api" / "openapi.yaml",
        PROJECT_ROOT / "openapi.json",
    ]

    spec_file = None
    spec_data = None

    for path in spec_paths:
        if path.exists():
            spec_file = path
            if path.suffix == ".json":
                spec_data = json.loads(path.read_text(encoding="utf-8"))
            else:
                try:
                    import yaml
                    spec_data = yaml.safe_load(path.read_text(encoding="utf-8"))
                except ImportError:
                    print("Warning: PyYAML not installed, cannot parse YAML spec")
                    continue
            break

    if spec_data is None:
        return {}, spec_file

    paths = {}
    for path, methods in spec_data.get("paths", {}).items():
        paths[path] = set()
        for method in methods:
            if method.lower() in ("get", "post", "put", "delete", "patch", "options", "head"):
                paths[path].add(method.upper())

    return paths, spec_file


def get_fastapi_routes() -> dict[str, set[str]]:
    """
    Get all routes registered in the FastAPI application.

    Returns:
        Dict mapping path to set of methods
    """
    try:
        # Import the app to get its routes
        from backend.api.main import app

        routes = {}
        for route in app.routes:
            if hasattr(route, "path") and hasattr(route, "methods"):
                path = route.path
                methods = set(route.methods) if route.methods else set()

                # Skip internal routes
                if path.startswith("/docs") or path.startswith("/openapi") or path.startswith("/redoc"):
                    continue

                # Normalize path (remove trailing slashes)
                path = path.rstrip("/") or "/"

                if path not in routes:
                    routes[path] = set()
                routes[path].update(methods)

        return routes

    except ImportError as e:
        print(f"Error: Could not import FastAPI app: {e}")
        print("Make sure you're running from the project root with dependencies installed.")
        return {}


def compare_routes(spec_paths: dict[str, set[str]], app_routes: dict[str, set[str]]) -> tuple[list[str], list[str], list[str]]:
    """
    Compare spec paths with actual app routes.

    Returns:
        Tuple of (missing_from_spec, missing_from_app, method_mismatches)
    """
    missing_from_spec = []
    missing_from_app = []
    method_mismatches = []

    # Find routes in app but not in spec
    for path, methods in app_routes.items():
        if path not in spec_paths:
            missing_from_spec.append(f"{path} [{', '.join(sorted(methods))}]")
        else:
            spec_methods = spec_paths[path]
            extra_methods = methods - spec_methods
            if extra_methods:
                method_mismatches.append(f"{path}: app has {extra_methods} not in spec")

    # Find routes in spec but not in app
    for path, methods in spec_paths.items():
        if path not in app_routes:
            missing_from_app.append(f"{path} [{', '.join(sorted(methods))}]")
        else:
            app_methods = app_routes[path]
            missing_methods = methods - app_methods
            if missing_methods:
                method_mismatches.append(f"{path}: spec has {missing_methods} not in app")

    return missing_from_spec, missing_from_app, method_mismatches


def main():
    parser = argparse.ArgumentParser(description="Validate OpenAPI spec against FastAPI routes")
    parser.add_argument("--update", action="store_true", help="Update spec from current routes")
    parser.add_argument("--strict", action="store_true", help="Exit with error on any mismatch")
    args = parser.parse_args()

    print("=" * 70)
    print("OpenAPI Contract Validation")
    print("=" * 70)
    print()

    # Get spec paths
    spec_paths, spec_file = get_openapi_spec_paths()
    if spec_file:
        print(f"Spec file: {spec_file}")
    else:
        print("Warning: No OpenAPI spec file found")
        spec_paths = {}

    print(f"Spec routes: {len(spec_paths)}")

    # Get app routes
    app_routes = get_fastapi_routes()
    print(f"App routes: {len(app_routes)}")
    print()

    if not app_routes:
        print("Error: Could not load app routes")
        return 1

    # Compare
    missing_from_spec, missing_from_app, method_mismatches = compare_routes(spec_paths, app_routes)

    has_issues = False

    if missing_from_spec:
        print("Routes in app but MISSING from spec:")
        for route in sorted(missing_from_spec):
            print(f"  + {route}")
        print()
        has_issues = True

    if missing_from_app:
        print("Routes in spec but MISSING from app:")
        for route in sorted(missing_from_app):
            print(f"  - {route}")
        print()
        has_issues = True

    if method_mismatches:
        print("Method mismatches:")
        for mismatch in sorted(method_mismatches):
            print(f"  ! {mismatch}")
        print()
        has_issues = True

    if not has_issues:
        print("OpenAPI contract validation: PASS")
        print("All routes match between spec and implementation.")
        return 0

    print("=" * 70)

    if args.update:
        print("\nTo update the spec, use: GET /openapi.json from running app")
        print("and save to docs/api/openapi.json")

    if args.strict:
        print("\nOpenAPI contract validation: FAIL (strict mode)")
        return 1
    else:
        print("\nOpenAPI contract validation: WARN (non-strict mode)")
        return 0


if __name__ == "__main__":
    sys.exit(main())
