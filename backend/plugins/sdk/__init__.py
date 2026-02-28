"""
VoiceStudio Plugin SDK.

Phase 5D M5: OpenAPI-based SDK for plugin development.

This module provides:
- Protocol specification access and introspection
- TypedDict definitions for type-safe development
- Client stubs for host API access
- SDK generation utilities

Example usage:
    from backend.plugins.sdk import get_protocol_spec, SDKGenerator

    spec = get_protocol_spec()
    print(f"Protocol version: {spec.version}")
    print(f"Available methods: {len(spec.get_methods())}")

    # Generate SDK code
    gen = SDKGenerator(spec)
    typed_dicts = gen.generate_typed_dicts()
    client_stubs = gen.generate_client_stubs()
"""

from __future__ import annotations

from .protocol_spec import (
    STANDARD_ERROR_CODES,
    ErrorCodeSpec,
    MethodDirection,
    MethodSpec,
    ProtocolSpec,
    SchemaSpec,
    SDKGenerator,
    get_protocol_spec,
    reset_spec,
)

__all__ = [
    # Constants
    "STANDARD_ERROR_CODES",
    "ErrorCodeSpec",
    # Enums
    "MethodDirection",
    "MethodSpec",
    # Core spec classes
    "ProtocolSpec",
    # SDK generation
    "SDKGenerator",
    "SchemaSpec",
    # Functions
    "get_protocol_spec",
    "reset_spec",
]
