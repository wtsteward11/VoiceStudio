"""CI gate: route_module_names and _include_route calls must be in parity.

Routes listed in REGISTERED_ELSEWHERE are imported for side effects but wired
by a different mechanism (e.g., observability.py).  They are excluded from the
parity check but still documented here so drift is visible.
"""
import ast
import re
from pathlib import Path

REGISTRY = Path("backend/api/route_registry.py")

REGISTERED_ELSEWHERE: set[str] = set()
# /api/metrics is registered by register_observability_routes() in main.py.
# If a route module is imported but intentionally NOT wired via _include_route
# because another mechanism registers it, add it to REGISTERED_ELSEWHERE.


def _extract_module_names(source: str) -> set[str]:
    """Extract all string literals from route_module_names list."""
    tree = ast.parse(source)
    for node in ast.walk(tree):
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name) and target.id == "route_module_names":
                    if isinstance(node.value, ast.List):
                        return {
                            elt.value
                            for elt in node.value.elts
                            if isinstance(elt, ast.Constant) and isinstance(elt.value, str)
                        }
    return set()


def _extract_include_calls(source: str) -> set[str]:
    """Extract all _include_route("...") call arguments."""
    return set(re.findall(r'_include_route\(["\'](\w+)["\']\)', source))


def test_route_module_names_equals_include_calls():
    source = REGISTRY.read_text(encoding="utf-8")
    names = _extract_module_names(source) - REGISTERED_ELSEWHERE
    calls = _extract_include_calls(source) - REGISTERED_ELSEWHERE
    in_names_not_calls = names - calls
    in_calls_not_names = calls - names
    assert not in_names_not_calls, (
        f"In route_module_names but never _include_route'd: {sorted(in_names_not_calls)}"
    )
    assert not in_calls_not_names, (
        f"_include_route'd but not in route_module_names: {sorted(in_calls_not_names)}"
    )
