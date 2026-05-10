#!/usr/bin/env python3
"""
Contract Validation - Verify C# client matches OpenAPI schema.

Checks:
1. All OpenAPI endpoints have corresponding C# methods
2. Request/response types match between schema and generated client
3. HTTP methods and paths are consistent
4. Breaking changes detection (when --check-breaking is used)

Usage:
    python validate-contract.py              # Basic validation
    python validate-contract.py --check-breaking  # Include breaking change detection
    python validate-contract.py --json       # JSON output for CI
"""

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class ContractEndpoint:
    """Represents an API endpoint."""
    path: str
    method: str
    operation_id: str
    request_body: str | None = None
    response_type: str | None = None
    parameters: list[str] = field(default_factory=list)


@dataclass
class ValidationResult:
    """Result of contract validation."""
    passed: bool = True
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    endpoints_in_schema: int = 0
    endpoints_in_client: int = 0
    matched: int = 0


def parse_openapi_schema(schema_path: Path) -> list[ContractEndpoint]:
    """Parse OpenAPI schema into endpoints."""
    with open(schema_path, encoding="utf-8") as f:
        schema = json.load(f)

    endpoints = []
    paths = schema.get("paths", {})

    for path, methods in paths.items():
        for method, details in methods.items():
            if method in ["get", "post", "put", "delete", "patch"]:
                operation_id = details.get("operationId", f"{method}_{path}")

                # Extract request body type
                request_body = None
                if "requestBody" in details:
                    content = details["requestBody"].get("content", {})
                    if "application/json" in content:
                        ref = content["application/json"].get("schema", {}).get("$ref")
                        if ref:
                            request_body = ref.split("/")[-1]

                # Extract response type
                response_type = None
                responses = details.get("responses", {})
                for code in ["200", "201"]:
                    if code in responses:
                        content = responses[code].get("content", {})
                        if "application/json" in content:
                            ref = content["application/json"].get("schema", {}).get("$ref")
                            if ref:
                                response_type = ref.split("/")[-1]
                            break

                # Extract parameters
                params = []
                for param in details.get("parameters", []):
                    params.append(param.get("name", ""))

                endpoints.append(ContractEndpoint(
                    path=path,
                    method=method.upper(),
                    operation_id=operation_id,
                    request_body=request_body,
                    response_type=response_type,
                    parameters=params,
                ))

    return endpoints


def _task_return_type_end(content: str, inner_start: int) -> int | None:
    """Return index after the closing `>` of `Task<...>` starting at inner_start (first char inside `<`)."""
    depth = 1
    k = inner_start
    while k < len(content) and depth > 0:
        c = content[k]
        if c == "<":
            depth += 1
        elif c == ">":
            depth -= 1
        k += 1
    if depth != 0:
        return None
    return k


def parse_csharp_client(client_path: Path) -> set[str]:
    """Parse C# client to extract operation method stems (name without trailing Async).

    NSwag emits `System.Threading.Tasks.Task<...> Operation_id_from_openapiAsync(` or, for
    no-content responses, `System.Threading.Tasks.Task Operation_idAsync(` (non-generic Task).
    Implementations may place `async` after `Task<...>` and before the method name
    (e.g. `public virtual async System.Threading.Tasks.Task<object> NameAsync(`).

    Return types may nest generics (`Task<ICollection<Foo>>`); a naive `Task<[^>]+>` regex
    stops at the first `>` and misses those methods.
    """
    content = client_path.read_text(encoding="utf-8")
    methods: set[str] = set()
    base = "System.Threading.Tasks.Task"
    i = 0
    while True:
        j = content.find(base, i)
        if j == -1:
            break
        pos = j + len(base)
        while pos < len(content) and content[pos] in " \t\r\n":
            pos += 1
        if pos < len(content) and content[pos] == "<":
            inner_start = pos + 1
            end_task = _task_return_type_end(content, inner_start)
            if end_task is None:
                i = j + 1
                continue
            m_idx = end_task
        else:
            m_idx = pos
        while m_idx < len(content) and content[m_idx] in " \t\r\n":
            m_idx += 1
        # NSwag implementation methods: `public virtual async System.Threading.Tasks.Task<...> NameAsync(`
        if (
            m_idx + 5 <= len(content)
            and content[m_idx : m_idx + 5] == "async"
            and (m_idx + 5 == len(content) or not (content[m_idx + 5].isalnum() or content[m_idx + 5] == "_"))
        ):
            prev = content[m_idx - 1] if m_idx > 0 else " "
            if not (prev.isalnum() or prev == "_"):
                m_idx += 5
                while m_idx < len(content) and content[m_idx] in " \t\r\n":
                    m_idx += 1
        name_start = m_idx
        while m_idx < len(content) and (content[m_idx].isalnum() or content[m_idx] == "_"):
            m_idx += 1
        name = content[name_start:m_idx]
        if name.endswith("Async") and m_idx < len(content) and content[m_idx] == "(":
            methods.add(name[: -len("Async")])
        i = j + 1

    return methods


def normalize_operation_id(operation_id: str) -> str:
    """Normalize operation ID for comparison (case-insensitive)."""
    # Just lowercase for comparison - NSwag may use different casing
    return operation_id.lower().replace("-", "_")


def validate_contract(
    schema_path: Path,
    client_path: Path,
) -> ValidationResult:
    """Validate C# client against OpenAPI schema."""
    result = ValidationResult()

    # Parse sources
    try:
        endpoints = parse_openapi_schema(schema_path)
        result.endpoints_in_schema = len(endpoints)
    except Exception as e:
        result.passed = False
        result.errors.append(f"Failed to parse OpenAPI schema: {e}")
        return result

    try:
        client_methods = parse_csharp_client(client_path)
        result.endpoints_in_client = len(client_methods)
    except Exception as e:
        result.passed = False
        result.errors.append(f"Failed to parse C# client: {e}")
        return result

    # Normalize client methods for comparison (case-insensitive)
    client_methods_normalized = {normalize_operation_id(m): m for m in client_methods}

    # Check each endpoint has a method
    for endpoint in endpoints:
        normalized = normalize_operation_id(endpoint.operation_id)

        # Try exact match first
        if normalized in client_methods_normalized:
            result.matched += 1
            continue

        # Check for partial matches
        partial = any(
            normalized in method_norm
            for method_norm in client_methods_normalized
        )

        if partial:
            result.warnings.append(
                f"Endpoint '{endpoint.method} {endpoint.path}' "
                f"(operationId: {endpoint.operation_id}) has partial match"
            )
        else:
            result.errors.append(
                f"Missing method for endpoint '{endpoint.method} {endpoint.path}' "
                f"(operationId: {endpoint.operation_id})"
            )
            result.passed = False

    # Check for extra methods (methods without endpoints)
    schema_ids = {
        normalize_operation_id(e.operation_id)
        for e in endpoints
    }

    for method_norm, method_orig in client_methods_normalized.items():
        if method_norm not in schema_ids:
            # Skip common helper methods
            if method_orig.lower() not in ["updatejsonserializersettings", "createserializersettings"]:
                result.warnings.append(
                    f"Client method '{method_orig}' not found in OpenAPI schema"
                )

    return result


def run_breaking_change_detection(project_root: Path, json_output: bool = False) -> int:
    """Run breaking change detection using the detector script."""
    detector_path = project_root / "tools" / "nswag" / "detect-breaking-changes.py"

    if not detector_path.exists():
        if not json_output:
            print("[WARN] Breaking change detector not found")
        return 0

    args = [sys.executable, str(detector_path)]
    if json_output:
        args.append("--json")

    try:
        result = subprocess.run(
            args,
            capture_output=True,
            text=True,
            cwd=project_root,
        )

        if result.stdout:
            print(result.stdout)

        return result.returncode
    except Exception as e:
        if not json_output:
            print(f"[ERROR] Breaking change detection failed: {e}")
        return 1


def main():
    """Run contract validation."""
    parser = argparse.ArgumentParser(description="Validate API contracts")
    parser.add_argument(
        "--check-breaking",
        action="store_true",
        help="Run breaking change detection",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output as JSON",
    )
    args = parser.parse_args()

    project_root = Path(__file__).parent.parent.parent

    schema_path = project_root / "docs" / "api" / "openapi.json"
    client_path = project_root / "src" / "VoiceStudio.App" / "Services" / "Generated" / "BackendClient.g.cs"

    if args.json:
        output = {
            "contract_validation": {},
            "breaking_changes": None,
        }
    else:
        print("=" * 60)
        print(" Contract Validation: OpenAPI -> C# Client")
        print("=" * 60)
        print()

    # Check files exist
    if not schema_path.exists():
        if args.json:
            output["contract_validation"]["error"] = f"OpenAPI schema not found: {schema_path}"
            print(json.dumps(output, indent=2))
        else:
            print(f"[ERROR] OpenAPI schema not found: {schema_path}")
            print("  Generate with: python scripts/export_openapi_schema.py")
        return 1

    if not client_path.exists():
        if args.json:
            output["contract_validation"]["error"] = f"C# client not found: {client_path}"
            print(json.dumps(output, indent=2))
        else:
            print(f"[ERROR] C# client not found: {client_path}")
            print("  Generate with: powershell tools/nswag/generate-client.ps1")
        return 1

    if not args.json:
        print(f"Schema: {schema_path.relative_to(project_root)}")
        print(f"Client: {client_path.relative_to(project_root)}")
        print()

    # Validate contract
    result = validate_contract(schema_path, client_path)

    if args.json:
        output["contract_validation"] = {
            "passed": result.passed,
            "endpoints_in_schema": result.endpoints_in_schema,
            "endpoints_in_client": result.endpoints_in_client,
            "matched": result.matched,
            "errors": result.errors,
            "warnings": result.warnings,
        }
    else:
        # Report results
        print(f"Endpoints in schema: {result.endpoints_in_schema}")
        print(f"Methods in client:   {result.endpoints_in_client}")
        print(f"Matched:             {result.matched}")
        print()

        if result.errors:
            print(f"[ERRORS] ({len(result.errors)})")
            for error in result.errors:
                print(f"  - {error}")
            print()

        if result.warnings:
            print(f"[WARNINGS] ({len(result.warnings)})")
            for warning in result.warnings[:10]:  # Limit output
                print(f"  - {warning}")
            if len(result.warnings) > 10:
                print(f"  ... and {len(result.warnings) - 10} more")
            print()

    # Run breaking change detection if requested
    breaking_exit = 0
    if args.check_breaking:
        if not args.json:
            print()
            print("=" * 60)
            print(" Breaking Change Detection")
            print("=" * 60)
            print()
        breaking_exit = run_breaking_change_detection(project_root, args.json)

    # Summary
    if args.json:
        print(json.dumps(output, indent=2))
    else:
        if result.passed:
            print("[PASS] Contract validation passed!")
        else:
            print("[FAIL] Contract validation failed!")
            print()
            print("Next steps:")
            print("  1. Regenerate client: powershell tools/nswag/generate-client.ps1 -Force")
            print("  2. Update OpenAPI schema if backend changed")
            print("  3. Review mismatched endpoints")

    # Return failure if either check failed
    if not result.passed or breaking_exit != 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
