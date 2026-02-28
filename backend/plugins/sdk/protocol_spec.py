"""
OpenAPI 3.1 Protocol Specification Module.

Phase 5D M5: Provides programmatic access to the plugin-host IPC protocol
specification and utilities for SDK generation.

This module:
- Loads and validates the OpenAPI spec
- Provides schema introspection
- Generates TypedDict stubs for type safety
- Validates messages against schemas
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any

# Optional YAML support
try:
    import yaml

    YAML_AVAILABLE = True
except ImportError:
    yaml = None  # type: ignore[assignment]
    YAML_AVAILABLE = False


class MethodDirection(str, Enum):
    """Direction of method invocation."""

    HOST_TO_PLUGIN = "host_to_plugin"
    PLUGIN_TO_HOST = "plugin_to_host"
    BIDIRECTIONAL = "bidirectional"


@dataclass
class MethodSpec:
    """Specification for a single protocol method."""

    name: str
    operation_id: str
    summary: str
    description: str
    direction: MethodDirection
    tags: list[str]
    request_schema: dict[str, Any] | None
    response_schema: dict[str, Any] | None
    is_notification: bool = False

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "name": self.name,
            "operation_id": self.operation_id,
            "summary": self.summary,
            "description": self.description,
            "direction": self.direction.value,
            "tags": self.tags,
            "request_schema": self.request_schema,
            "response_schema": self.response_schema,
            "is_notification": self.is_notification,
        }


@dataclass
class SchemaSpec:
    """Specification for a schema component."""

    name: str
    schema: dict[str, Any]
    required_properties: list[str] = field(default_factory=list)
    description: str = ""

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "name": self.name,
            "schema": self.schema,
            "required_properties": self.required_properties,
            "description": self.description,
        }


@dataclass
class ErrorCodeSpec:
    """Specification for an error code."""

    code: int
    name: str
    description: str

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "code": self.code,
            "name": self.name,
            "description": self.description,
        }


# Standard error codes from protocol
STANDARD_ERROR_CODES: list[ErrorCodeSpec] = [
    ErrorCodeSpec(-32700, "PARSE_ERROR", "Parse error"),
    ErrorCodeSpec(-32600, "INVALID_REQUEST", "Invalid request"),
    ErrorCodeSpec(-32601, "METHOD_NOT_FOUND", "Method not found"),
    ErrorCodeSpec(-32602, "INVALID_PARAMS", "Invalid params"),
    ErrorCodeSpec(-32603, "INTERNAL_ERROR", "Internal error"),
    ErrorCodeSpec(-32000, "PERMISSION_DENIED", "Permission denied"),
    ErrorCodeSpec(-32001, "TIMEOUT", "Request timeout"),
    ErrorCodeSpec(-32002, "PLUGIN_ERROR", "Plugin error"),
    ErrorCodeSpec(-32003, "RESOURCE_LIMIT", "Resource limit exceeded"),
    ErrorCodeSpec(-32004, "NOT_INITIALIZED", "Not initialized"),
    ErrorCodeSpec(-32005, "ALREADY_INITIALIZED", "Already initialized"),
    ErrorCodeSpec(-32006, "INVALID_STATE", "Invalid state"),
    ErrorCodeSpec(-32007, "SHUTDOWN_IN_PROGRESS", "Shutdown in progress"),
    ErrorCodeSpec(-32008, "HOST_UNAVAILABLE", "Host unavailable"),
    ErrorCodeSpec(-32009, "SERIALIZATION_ERROR", "Serialization error"),
]


class ProtocolSpec:
    """
    OpenAPI-based protocol specification.

    Loads and parses the OpenAPI spec for introspection
    and SDK generation.
    """

    # Default spec path relative to project root
    DEFAULT_SPEC_PATH = "shared/schemas/plugin-ipc-protocol.openapi.yaml"

    def __init__(self, spec_path: str | Path | None = None):
        """
        Initialize with optional custom spec path.

        Args:
            spec_path: Path to OpenAPI spec file. Defaults to standard location.
        """
        self._spec: dict[str, Any] = {}
        self._methods: dict[str, MethodSpec] = {}
        self._schemas: dict[str, SchemaSpec] = {}
        self._loaded = False

        if spec_path:
            self._spec_path = Path(spec_path)
        else:
            # Try to find spec relative to this file
            self._spec_path = self._find_default_spec_path()

    def _find_default_spec_path(self) -> Path:
        """Find the default spec path relative to project structure."""
        # Start from this file's location
        current = Path(__file__).resolve()

        # Walk up to find project root (contains shared/schemas)
        for _ in range(10):
            candidate = current / "shared" / "schemas" / "plugin-ipc-protocol.openapi.yaml"
            if candidate.exists():
                return candidate
            current = current.parent

        # Fallback to relative path
        return Path("shared/schemas/plugin-ipc-protocol.openapi.yaml")

    def load(self) -> None:
        """Load and parse the OpenAPI specification."""
        if not self._spec_path.exists():
            raise FileNotFoundError(f"Spec file not found: {self._spec_path}")

        content = self._spec_path.read_text(encoding="utf-8")

        if self._spec_path.suffix in (".yaml", ".yml"):
            if not YAML_AVAILABLE:
                raise ImportError(
                    "PyYAML is required to load YAML specs. " "Install with: pip install pyyaml"
                )
            self._spec = yaml.safe_load(content)
        else:
            self._spec = json.loads(content)

        self._parse_methods()
        self._parse_schemas()
        self._loaded = True

    def _ensure_loaded(self) -> None:
        """Ensure spec is loaded."""
        if not self._loaded:
            self.load()

    def _parse_methods(self) -> None:
        """Parse method specifications from paths."""
        paths = self._spec.get("paths", {})

        for path, methods in paths.items():
            method_name = path.lstrip("/")

            for http_method, spec in methods.items():
                if http_method not in ("get", "post", "put", "delete"):
                    continue

                # Determine direction based on method name prefix
                direction = self._infer_direction(method_name)

                # Extract request schema
                request_schema = None
                if "requestBody" in spec:
                    content = spec["requestBody"].get("content", {})
                    if "application/json" in content:
                        request_schema = content["application/json"].get("schema")

                # Extract response schema
                response_schema = None
                responses = spec.get("responses", {})
                if "200" in responses:
                    content = responses["200"].get("content", {})
                    if "application/json" in content:
                        response_schema = content["application/json"].get("schema")

                # Check if notification (204 response, no body)
                is_notification = "204" in responses or method_name.startswith("notify.")

                self._methods[method_name] = MethodSpec(
                    name=method_name,
                    operation_id=spec.get("operationId", method_name.replace(".", "_")),
                    summary=spec.get("summary", ""),
                    description=spec.get("description", ""),
                    direction=direction,
                    tags=spec.get("tags", []),
                    request_schema=request_schema,
                    response_schema=response_schema,
                    is_notification=is_notification,
                )

    def _infer_direction(self, method_name: str) -> MethodDirection:
        """Infer method direction from name."""
        if method_name.startswith("plugin."):
            return MethodDirection.HOST_TO_PLUGIN
        elif method_name.startswith("host."):
            return MethodDirection.PLUGIN_TO_HOST
        elif method_name.startswith("notify."):
            return MethodDirection.BIDIRECTIONAL
        else:
            return MethodDirection.BIDIRECTIONAL

    def _parse_schemas(self) -> None:
        """Parse schema specifications from components."""
        components = self._spec.get("components", {})
        schemas = components.get("schemas", {})

        for name, schema in schemas.items():
            required = schema.get("required", [])
            description = schema.get("description", "")

            self._schemas[name] = SchemaSpec(
                name=name,
                schema=schema,
                required_properties=required,
                description=description,
            )

    @property
    def version(self) -> str:
        """Get spec version."""
        self._ensure_loaded()
        return self._spec.get("info", {}).get("version", "0.0.0")

    @property
    def title(self) -> str:
        """Get spec title."""
        self._ensure_loaded()
        return self._spec.get("info", {}).get("title", "")

    @property
    def description(self) -> str:
        """Get spec description."""
        self._ensure_loaded()
        return self._spec.get("info", {}).get("description", "")

    def get_methods(self) -> dict[str, MethodSpec]:
        """Get all method specifications."""
        self._ensure_loaded()
        return self._methods.copy()

    def get_method(self, name: str) -> MethodSpec | None:
        """Get a specific method specification."""
        self._ensure_loaded()
        return self._methods.get(name)

    def get_host_methods(self) -> dict[str, MethodSpec]:
        """Get methods callable by plugins (host API)."""
        self._ensure_loaded()
        return {
            name: spec
            for name, spec in self._methods.items()
            if spec.direction == MethodDirection.PLUGIN_TO_HOST
        }

    def get_plugin_methods(self) -> dict[str, MethodSpec]:
        """Get methods callable by host (plugin API)."""
        self._ensure_loaded()
        return {
            name: spec
            for name, spec in self._methods.items()
            if spec.direction == MethodDirection.HOST_TO_PLUGIN
        }

    def get_notification_methods(self) -> dict[str, MethodSpec]:
        """Get notification methods (no response expected)."""
        self._ensure_loaded()
        return {name: spec for name, spec in self._methods.items() if spec.is_notification}

    def get_schemas(self) -> dict[str, SchemaSpec]:
        """Get all schema specifications."""
        self._ensure_loaded()
        return self._schemas.copy()

    def get_schema(self, name: str) -> SchemaSpec | None:
        """Get a specific schema specification."""
        self._ensure_loaded()
        return self._schemas.get(name)

    def resolve_ref(self, ref: str) -> dict[str, Any] | None:
        """
        Resolve a $ref pointer.

        Args:
            ref: Reference string like "#/components/schemas/InitializeParams"

        Returns:
            Resolved schema or None if not found.
        """
        self._ensure_loaded()

        if not ref.startswith("#/"):
            return None

        parts = ref[2:].split("/")
        current: Any = self._spec

        for part in parts:
            if isinstance(current, dict) and part in current:
                current = current[part]
            else:
                return None

        return current if isinstance(current, dict) else None

    def get_error_codes(self) -> list[ErrorCodeSpec]:
        """Get all error code specifications."""
        return STANDARD_ERROR_CODES.copy()

    def export_json(self) -> str:
        """Export the spec as JSON."""
        self._ensure_loaded()
        return json.dumps(self._spec, indent=2)

    def generate_method_summary(self) -> str:
        """Generate a markdown summary of all methods."""
        self._ensure_loaded()

        lines = [
            "# Plugin IPC Protocol Methods",
            "",
            f"Version: {self.version}",
            "",
        ]

        # Group by direction
        for direction in MethodDirection:
            methods = [m for m in self._methods.values() if m.direction == direction]
            if not methods:
                continue

            lines.append(f"## {direction.value.replace('_', ' ').title()}")
            lines.append("")

            for method in sorted(methods, key=lambda m: m.name):
                notification_marker = " (notification)" if method.is_notification else ""
                lines.append(f"### `{method.name}`{notification_marker}")
                lines.append(f"{method.summary}")
                if method.description:
                    lines.append(f"\n{method.description}")
                lines.append("")

        return "\n".join(lines)


class SDKGenerator:
    """
    Generate SDK code from protocol specification.

    Produces Python TypedDict definitions and method stubs.
    """

    def __init__(self, spec: ProtocolSpec):
        """
        Initialize with protocol spec.

        Args:
            spec: Loaded protocol specification.
        """
        self.spec = spec

    def generate_typed_dicts(self) -> str:
        """Generate Python TypedDict definitions for all schemas."""
        lines = [
            '"""',
            "Auto-generated TypedDict definitions from OpenAPI spec.",
            "",
            "DO NOT EDIT MANUALLY. Regenerate with:",
            "  python -m backend.plugins.sdk.protocol_spec --generate-types",
            '"""',
            "",
            "from __future__ import annotations",
            "",
            "from typing import Any, Literal, TypedDict, NotRequired",
            "",
        ]

        for name, schema_spec in sorted(self.spec.get_schemas().items()):
            schema = schema_spec.schema
            if schema.get("type") != "object":
                continue

            lines.extend(self._generate_typed_dict(name, schema))
            lines.append("")

        return "\n".join(lines)

    def _generate_typed_dict(self, name: str, schema: dict[str, Any]) -> list[str]:
        """Generate a single TypedDict class."""
        lines = [f"class {name}(TypedDict, total=False):"]

        description = schema.get("description")
        if description:
            lines.append(f'    """{description}"""')
            lines.append("")

        properties = schema.get("properties", {})
        required = set(schema.get("required", []))

        if not properties:
            lines.append("    pass")
            return lines

        for prop_name, prop_schema in properties.items():
            prop_type = self._schema_to_python_type(prop_schema)
            is_required = prop_name in required

            if is_required:
                lines.append(f"    {prop_name}: {prop_type}")
            else:
                lines.append(f"    {prop_name}: NotRequired[{prop_type}]")

        return lines

    def _schema_to_python_type(self, schema: dict[str, Any]) -> str:
        """Convert JSON Schema to Python type annotation."""
        if "$ref" in schema:
            # Extract name from reference
            ref = schema["$ref"]
            return ref.split("/")[-1]

        schema_type = schema.get("type")

        if schema_type == "string":
            if "enum" in schema:
                values = ", ".join(f'"{v}"' for v in schema["enum"])
                return f"Literal[{values}]"
            if schema.get("format") == "byte":
                return "str"  # Base64 encoded
            return "str"

        elif schema_type == "integer":
            return "int"

        elif schema_type == "number":
            return "float"

        elif schema_type == "boolean":
            return "bool"

        elif schema_type == "array":
            items = schema.get("items", {})
            item_type = self._schema_to_python_type(items)
            return f"list[{item_type}]"

        elif schema_type == "object":
            if "additionalProperties" in schema:
                value_type = schema.get("additionalProperties")
                if value_type is True:
                    return "dict[str, Any]"
                elif isinstance(value_type, dict):
                    val_type = self._schema_to_python_type(value_type)
                    return f"dict[str, {val_type}]"
            return "dict[str, Any]"

        elif "oneOf" in schema:
            types = [self._schema_to_python_type(s) for s in schema["oneOf"]]
            return " | ".join(types)

        elif "const" in schema:
            value = schema["const"]
            if isinstance(value, str):
                return f'Literal["{value}"]'
            return f"Literal[{value}]"

        return "Any"

    def generate_client_stubs(self) -> str:
        """Generate Python client method stubs for plugin SDK."""
        lines = [
            '"""',
            "Auto-generated client stubs for plugin-to-host API.",
            "",
            "DO NOT EDIT MANUALLY. Regenerate with:",
            "  python -m backend.plugins.sdk.protocol_spec --generate-client",
            '"""',
            "",
            "from __future__ import annotations",
            "",
            "from typing import TYPE_CHECKING",
            "",
            "if TYPE_CHECKING:",
            "    from .protocol_types import *",
            "",
            "",
            "class HostAPIClient:",
            '    """Client for calling host services from plugins."""',
            "",
            "    def __init__(self, send_request, send_notification):",
            '        """',
            "        Initialize with transport functions.",
            "",
            "        Args:",
            "            send_request: Callable to send request and await response.",
            "            send_notification: Callable to send notification (no response).",
            '        """',
            "        self._send_request = send_request",
            "        self._send_notification = send_notification",
            "",
        ]

        # Generate methods for host API
        for name, method in sorted(self.spec.get_host_methods().items()):
            lines.extend(self._generate_method_stub(method))
            lines.append("")

        # Generate notification methods
        for name, method in sorted(self.spec.get_notification_methods().items()):
            if method.direction != MethodDirection.PLUGIN_TO_HOST:
                continue
            lines.extend(self._generate_notification_stub(method))
            lines.append("")

        return "\n".join(lines)

    def _generate_method_stub(self, method: MethodSpec) -> list[str]:
        """Generate a single method stub."""
        method_name = method.name.replace(".", "_").replace("host_", "")

        lines = [
            f"    async def {method_name}(self, **params):",
            '        """',
            f"        {method.summary}",
        ]

        if method.description:
            lines.append("")
            lines.append(f"        {method.description}")

        lines.extend(
            [
                '        """',
                f'        return await self._send_request("{method.name}", params)',
            ]
        )

        return lines

    def _generate_notification_stub(self, method: MethodSpec) -> list[str]:
        """Generate a notification method stub."""
        method_name = method.name.replace(".", "_")

        lines = [
            f"    def {method_name}(self, **params):",
            '        """',
            f"        {method.summary}",
            "",
            "        This is a notification (no response expected).",
            '        """',
            f'        self._send_notification("{method.name}", params)',
        ]

        return lines


# Module singleton
_spec_instance: ProtocolSpec | None = None


def get_protocol_spec() -> ProtocolSpec:
    """Get the singleton protocol spec instance."""
    global _spec_instance
    if _spec_instance is None:
        _spec_instance = ProtocolSpec()
    return _spec_instance


def reset_spec() -> None:
    """Reset the singleton instance (for testing)."""
    global _spec_instance
    _spec_instance = None


# CLI support
if __name__ == "__main__":
    import sys

    spec = get_protocol_spec()

    if len(sys.argv) > 1:
        if sys.argv[1] == "--generate-types":
            gen = SDKGenerator(spec)
            print(gen.generate_typed_dicts())
        elif sys.argv[1] == "--generate-client":
            gen = SDKGenerator(spec)
            print(gen.generate_client_stubs())
        elif sys.argv[1] == "--summary":
            print(spec.generate_method_summary())
        elif sys.argv[1] == "--json":
            print(spec.export_json())
        else:
            print(f"Unknown option: {sys.argv[1]}")
            print("Usage: python -m backend.plugins.sdk.protocol_spec [option]")
            print("Options:")
            print("  --generate-types  Generate TypedDict definitions")
            print("  --generate-client Generate client stubs")
            print("  --summary         Generate markdown method summary")
            print("  --json            Export spec as JSON")
            sys.exit(1)
    else:
        # Default: print info
        print(f"Protocol: {spec.title}")
        print(f"Version: {spec.version}")
        print(f"Methods: {len(spec.get_methods())}")
        print(f"Schemas: {len(spec.get_schemas())}")
