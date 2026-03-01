#!/usr/bin/env python3
"""
API Documentation Generator

Generates markdown documentation from OpenAPI schema.

Sources:
- docs/api/openapi.json - OpenAPI 3.0+ schema
- Python docstrings from backend/api/routes/*.py
- Pydantic model descriptions

Output:
- docs/api/GENERATED_API_REFERENCE.md
- Per-route documentation files (optional)

Usage:
    python scripts/generate_api_docs.py
    python scripts/generate_api_docs.py --format html
    python scripts/generate_api_docs.py --output docs/api/
    python scripts/generate_api_docs.py --per-route

Exit Codes:
    0: Documentation generated successfully
    1: OpenAPI schema not found or invalid
    2: Error occurred
"""

import argparse
import json
import re
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

from _env_setup import DOCS_DIR

# Paths
OPENAPI_PATH = DOCS_DIR / "api" / "openapi.json"
OUTPUT_DIR = DOCS_DIR / "api"


@dataclass
class Parameter:
    """API parameter."""
    name: str
    location: str  # query, path, header
    required: bool
    description: str
    schema_type: str
    default: Any | None = None


@dataclass
class RequestBody:
    """API request body."""
    content_type: str
    schema: dict[str, Any]
    required: bool
    description: str = ""


@dataclass
class Response:
    """API response."""
    status_code: str
    description: str
    schema: dict[str, Any] = field(default_factory=dict)
    examples: dict[str, Any] = field(default_factory=dict)


@dataclass
class Endpoint:
    """API endpoint."""
    path: str
    method: str
    operation_id: str
    summary: str
    description: str
    tags: list[str]
    parameters: list[Parameter]
    request_body: RequestBody | None
    responses: list[Response]
    deprecated: bool = False


class OpenAPIParser:
    """Parse OpenAPI schema into structured documentation."""

    def __init__(self, openapi_path: Path):
        self.openapi_path = openapi_path
        self.schema: dict[str, Any] = {}
        self.endpoints: list[Endpoint] = []
        self.tags: dict[str, str] = {}
        self.components: dict[str, Any] = {}

    def parse(self) -> bool:
        """Parse the OpenAPI schema."""
        try:
            self.schema = json.loads(self.openapi_path.read_text(encoding="utf-8"))

            # Parse info
            self.info = self.schema.get("info", {})

            # Parse tags
            for tag in self.schema.get("tags", []):
                self.tags[tag.get("name", "")] = tag.get("description", "")

            # Parse components
            self.components = self.schema.get("components", {}).get("schemas", {})

            # Parse paths
            for path, methods in self.schema.get("paths", {}).items():
                for method, details in methods.items():
                    if method in ("get", "post", "put", "patch", "delete"):
                        endpoint = self._parse_endpoint(path, method, details)
                        self.endpoints.append(endpoint)

            return True

        except Exception as e:
            print(f"Error parsing OpenAPI: {e}")
            return False

    def _parse_endpoint(self, path: str, method: str, details: dict[str, Any]) -> Endpoint:
        """Parse a single endpoint."""
        # Parse parameters
        parameters = []
        for param in details.get("parameters", []):
            parameters.append(Parameter(
                name=param.get("name", ""),
                location=param.get("in", "query"),
                required=param.get("required", False),
                description=param.get("description", ""),
                schema_type=self._get_schema_type(param.get("schema", {})),
                default=param.get("schema", {}).get("default"),
            ))

        # Parse request body
        request_body = None
        if "requestBody" in details:
            rb = details["requestBody"]
            content = rb.get("content", {})
            for content_type, content_schema in content.items():
                request_body = RequestBody(
                    content_type=content_type,
                    schema=content_schema.get("schema", {}),
                    required=rb.get("required", False),
                    description=rb.get("description", ""),
                )
                break  # Use first content type

        # Parse responses
        responses = []
        for status_code, response in details.get("responses", {}).items():
            content = response.get("content", {})
            schema = {}
            examples = {}

            for content_type, content_data in content.items():
                schema = content_data.get("schema", {})
                examples = content_data.get("examples", {})
                break

            responses.append(Response(
                status_code=status_code,
                description=response.get("description", ""),
                schema=schema,
                examples=examples,
            ))

        return Endpoint(
            path=path,
            method=method.upper(),
            operation_id=details.get("operationId", ""),
            summary=details.get("summary", ""),
            description=details.get("description", ""),
            tags=details.get("tags", []),
            parameters=parameters,
            request_body=request_body,
            responses=responses,
            deprecated=details.get("deprecated", False),
        )

    def _get_schema_type(self, schema: dict[str, Any]) -> str:
        """Get human-readable schema type."""
        if "$ref" in schema:
            return schema["$ref"].split("/")[-1]

        type_name = schema.get("type", "any")

        if type_name == "array":
            items_type = self._get_schema_type(schema.get("items", {}))
            return f"array[{items_type}]"

        if "enum" in schema:
            return f"enum[{', '.join(str(e) for e in schema['enum'])}]"

        return type_name


class MarkdownGenerator:
    """Generate Markdown documentation from parsed API."""

    def __init__(self, parser: OpenAPIParser):
        self.parser = parser

    def generate_full_reference(self) -> str:
        """Generate complete API reference document."""
        lines = [
            "# VoiceStudio API Reference",
            "",
            "<!-- AUTO-GENERATED: Do not edit manually -->",
            f"<!-- Generated: {datetime.now().isoformat()} -->",
            "",
        ]

        # API Info
        info = self.parser.info
        lines.extend([
            f"**Version**: {info.get('version', '1.0.0')}",
            "",
            info.get("description", "").strip(),
            "",
            "---",
            "",
            "## Table of Contents",
            "",
        ])

        # Group endpoints by tag
        endpoints_by_tag: dict[str, list[Endpoint]] = defaultdict(list)
        for endpoint in self.parser.endpoints:
            tag = endpoint.tags[0] if endpoint.tags else "General"
            endpoints_by_tag[tag].append(endpoint)

        # TOC
        for tag in sorted(endpoints_by_tag.keys()):
            lines.append(f"- [{tag}](#{self._slugify(tag)})")

        lines.extend(["", "---", ""])

        # Endpoints by tag
        for tag in sorted(endpoints_by_tag.keys()):
            lines.extend(self._generate_tag_section(tag, endpoints_by_tag[tag]))

        # Components/Schemas
        if self.parser.components:
            lines.extend([
                "---",
                "",
                "## Schemas",
                "",
            ])
            for name, schema in sorted(self.parser.components.items()):
                lines.extend(self._generate_schema_section(name, schema))

        return "\n".join(lines)

    def _generate_tag_section(self, tag: str, endpoints: list[Endpoint]) -> list[str]:
        """Generate documentation for a tag group."""
        lines = [
            f"## {tag}",
            "",
        ]

        if tag in self.parser.tags:
            lines.extend([self.parser.tags[tag], ""])

        for endpoint in endpoints:
            lines.extend(self._generate_endpoint_section(endpoint))

        return lines

    def _generate_endpoint_section(self, endpoint: Endpoint) -> list[str]:
        """Generate documentation for a single endpoint."""
        deprecated_badge = " ⚠️ DEPRECATED" if endpoint.deprecated else ""

        lines = [
            f"### `{endpoint.method}` {endpoint.path}{deprecated_badge}",
            "",
            f"**{endpoint.summary}**",
            "",
        ]

        if endpoint.description:
            lines.extend([endpoint.description.strip(), ""])

        # Parameters
        if endpoint.parameters:
            lines.extend([
                "#### Parameters",
                "",
                "| Name | Location | Type | Required | Description |",
                "|------|----------|------|----------|-------------|",
            ])

            for param in endpoint.parameters:
                required = "Yes" if param.required else "No"
                default = f" (default: `{param.default}`)" if param.default is not None else ""
                lines.append(
                    f"| `{param.name}` | {param.location} | {param.schema_type} | {required} | {param.description}{default} |"
                )

            lines.append("")

        # Request Body
        if endpoint.request_body:
            lines.extend([
                "#### Request Body",
                "",
                f"Content-Type: `{endpoint.request_body.content_type}`",
                "",
            ])

            if endpoint.request_body.description:
                lines.extend([endpoint.request_body.description, ""])

            schema = endpoint.request_body.schema
            if schema:
                lines.extend(self._format_schema(schema))

            lines.append("")

        # Responses
        lines.extend([
            "#### Responses",
            "",
        ])

        for response in endpoint.responses:
            lines.append(f"**{response.status_code}**: {response.description}")

            if response.examples:
                for name, example in response.examples.items():
                    value = example.get("value", example)
                    lines.extend([
                        "",
                        f"Example ({name}):",
                        "```json",
                        json.dumps(value, indent=2),
                        "```",
                    ])

            lines.append("")

        lines.append("---")
        lines.append("")

        return lines

    def _generate_schema_section(self, name: str, schema: dict[str, Any]) -> list[str]:
        """Generate documentation for a schema."""
        lines = [
            f"### {name}",
            "",
        ]

        if "description" in schema:
            lines.extend([schema["description"], ""])

        if schema.get("type") == "object" and "properties" in schema:
            lines.extend([
                "| Property | Type | Required | Description |",
                "|----------|------|----------|-------------|",
            ])

            required_props = set(schema.get("required", []))

            for prop_name, prop_schema in schema.get("properties", {}).items():
                prop_type = prop_schema.get("type", "any")
                required = "Yes" if prop_name in required_props else "No"
                description = prop_schema.get("description", "")
                lines.append(f"| `{prop_name}` | {prop_type} | {required} | {description} |")

            lines.append("")

        return lines

    def _format_schema(self, schema: dict[str, Any]) -> list[str]:
        """Format a schema as readable text."""
        lines = []

        if "$ref" in schema:
            ref_name = schema["$ref"].split("/")[-1]
            lines.append(f"See schema: [{ref_name}](#{self._slugify(ref_name)})")
        elif schema.get("type") == "object" and "properties" in schema:
            lines.append("")
            lines.append("| Property | Type | Description |")
            lines.append("|----------|------|-------------|")

            for prop_name, prop_schema in schema.get("properties", {}).items():
                prop_type = prop_schema.get("type", "any")
                description = prop_schema.get("description", "")
                lines.append(f"| `{prop_name}` | {prop_type} | {description} |")

        return lines

    def _slugify(self, text: str) -> str:
        """Convert text to URL-friendly slug."""
        return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")

    def generate_per_route(self) -> dict[str, str]:
        """Generate separate file for each route group."""
        files = {}

        # Group by tag
        endpoints_by_tag: dict[str, list[Endpoint]] = defaultdict(list)
        for endpoint in self.parser.endpoints:
            tag = endpoint.tags[0] if endpoint.tags else "general"
            endpoints_by_tag[tag].append(endpoint)

        for tag, endpoints in endpoints_by_tag.items():
            filename = f"{self._slugify(tag)}.md"
            content = self._generate_route_file(tag, endpoints)
            files[filename] = content

        return files

    def _generate_route_file(self, tag: str, endpoints: list[Endpoint]) -> str:
        """Generate a single route file."""
        lines = [
            f"# {tag} API",
            "",
            "<!-- AUTO-GENERATED -->",
            "",
        ]

        for endpoint in endpoints:
            lines.extend(self._generate_endpoint_section(endpoint))

        return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Generate API documentation")
    parser.add_argument(
        "--format",
        choices=["markdown", "html"],
        default="markdown",
        help="Output format (default: markdown)"
    )
    parser.add_argument(
        "--output",
        type=str,
        default=None,
        help="Output directory"
    )
    parser.add_argument(
        "--per-route",
        action="store_true",
        help="Generate separate file per route group"
    )
    parser.add_argument(
        "--openapi",
        type=str,
        default=None,
        help="Path to OpenAPI schema"
    )

    args = parser.parse_args()

    print("=" * 60)
    print("API Documentation Generator")
    print("=" * 60)
    print()

    openapi_path = Path(args.openapi) if args.openapi else OPENAPI_PATH
    output_dir = Path(args.output) if args.output else OUTPUT_DIR

    if not openapi_path.exists():
        print(f"Error: OpenAPI schema not found: {openapi_path}")
        sys.exit(1)

    # Parse OpenAPI
    print(f"Parsing: {openapi_path}")
    api_parser = OpenAPIParser(openapi_path)

    if not api_parser.parse():
        print("Error: Failed to parse OpenAPI schema")
        sys.exit(1)

    print(f"Found {len(api_parser.endpoints)} endpoints")
    print()

    # Generate documentation
    generator = MarkdownGenerator(api_parser)

    output_dir.mkdir(parents=True, exist_ok=True)

    if args.per_route:
        # Generate per-route files
        route_files = generator.generate_per_route()
        routes_dir = output_dir / "routes"
        routes_dir.mkdir(exist_ok=True)

        for filename, content in route_files.items():
            file_path = routes_dir / filename
            file_path.write_text(content)
            print(f"Generated: {file_path}")
    else:
        # Generate single reference file
        reference = generator.generate_full_reference()
        output_path = output_dir / "GENERATED_API_REFERENCE.md"
        output_path.write_text(reference)
        print(f"Generated: {output_path}")

    print()
    print("API documentation generated successfully!")
    sys.exit(0)


if __name__ == "__main__":
    main()
