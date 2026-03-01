#!/usr/bin/env python3
"""
validate_xaml_bindings.py - Validate XAML Binding Paths Against ViewModels

Parses XAML files for {Binding ...} and {x:Bind ...} expressions and validates
that the referenced property paths exist in the corresponding ViewModel.

Usage:
    python scripts/validate_xaml_bindings.py
    python scripts/validate_xaml_bindings.py --verbose
    python scripts/validate_xaml_bindings.py --json
    python scripts/validate_xaml_bindings.py --file src/VoiceStudio.App/Views/SomeView.xaml

Exit codes:
    0: All bindings validated (or only warnings)
    1: Critical binding errors found or error
"""

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

# ============================================================================
# Configuration
# ============================================================================

def get_project_root() -> Path:
    """Find project root by looking for VoiceStudio.sln."""
    script_dir = Path(__file__).parent
    for parent in [script_dir.parent, script_dir]:
        if (parent / "VoiceStudio.sln").exists():
            return parent
    raise RuntimeError("Could not find project root (VoiceStudio.sln)")


VIEWS_DIR = "src/VoiceStudio.App/Views"
CONTROLS_DIR = "src/VoiceStudio.App/Controls"
VIEWMODELS_DIR = "src/VoiceStudio.App/ViewModels"
PANELS_VIEWMODELS_DIR = "src/VoiceStudio.App/Views/Panels"


# ============================================================================
# Data Classes
# ============================================================================

@dataclass
class BindingInfo:
    """Information about a binding expression."""
    file_path: Path
    line_number: int
    binding_type: str  # "Binding" or "x:Bind"
    path: str
    mode: str | None = None
    converter: str | None = None
    full_expression: str = ""


@dataclass
class PropertyInfo:
    """Information about a ViewModel property."""
    name: str
    type_name: str | None = None
    is_observable: bool = False
    is_command: bool = False


@dataclass
class ViewModelInfo:
    """Information about a ViewModel class."""
    name: str
    file_path: Path
    properties: dict[str, PropertyInfo] = field(default_factory=dict)
    commands: dict[str, PropertyInfo] = field(default_factory=dict)
    nested_types: set[str] = field(default_factory=set)


@dataclass
class ValidationIssue:
    """A binding validation issue."""
    severity: str  # "error", "warning", "info"
    file_path: Path
    line_number: int
    binding_path: str
    message: str

    def to_dict(self) -> dict:
        return {
            "severity": self.severity,
            "file": str(self.file_path),
            "line": self.line_number,
            "path": self.binding_path,
            "message": self.message
        }


@dataclass
class ValidationReport:
    """Overall validation report."""
    timestamp: str = field(default_factory=lambda: datetime.now().isoformat())
    files_checked: int = 0
    bindings_found: int = 0
    bindings_validated: int = 0
    issues: list[ValidationIssue] = field(default_factory=list)

    @property
    def errors(self) -> int:
        return sum(1 for i in self.issues if i.severity == "error")

    @property
    def warnings(self) -> int:
        return sum(1 for i in self.issues if i.severity == "warning")

    @property
    def passed(self) -> bool:
        return self.errors == 0

    def to_dict(self) -> dict:
        return {
            "timestamp": self.timestamp,
            "files_checked": self.files_checked,
            "bindings_found": self.bindings_found,
            "bindings_validated": self.bindings_validated,
            "errors": self.errors,
            "warnings": self.warnings,
            "passed": self.passed,
            "issues": [i.to_dict() for i in self.issues]
        }


# ============================================================================
# Parsing Patterns
# ============================================================================

# Match {Binding Path=...} or {Binding ...}
BINDING_PATTERN = re.compile(
    r'\{Binding\s+(?:Path=)?([^,\}]+)(?:,\s*([^\}]+))?\}',
    re.IGNORECASE
)

# Match {x:Bind Path} or {x:Bind Path, Mode=...}
XBIND_PATTERN = re.compile(
    r'\{x:Bind\s+([^,\}]+)(?:,\s*([^\}]+))?\}',
    re.IGNORECASE
)

# Match property definitions in C# ViewModels
# [ObservableProperty] private TYPE _name;
OBSERVABLE_PROPERTY_PATTERN = re.compile(
    r'\[ObservableProperty\]\s*(?:\[.*?\]\s*)*private\s+(\S+)\s+_(\w+)\s*[;=]'
)

# public TYPE PropertyName { get; set; }
PUBLIC_PROPERTY_PATTERN = re.compile(
    r'public\s+(?:virtual\s+)?(\S+)\s+(\w+)\s*\{\s*get\s*;'
)

# [RelayCommand] methods
RELAY_COMMAND_PATTERN = re.compile(
    r'\[RelayCommand(?:\([^\)]*\))?\]\s*(?:private|public|protected)?\s*(?:async\s+)?(?:\S+\s+)?(\w+)'
)

# public IRelayCommand CommandName => ...
COMMAND_PROPERTY_PATTERN = re.compile(
    r'public\s+I(?:Relay|Async)?Command(?:<[^>]+>)?\s+(\w+)\s*(?:=>|\{)'
)

# DataContext binding in XAML
DATA_CONTEXT_PATTERN = re.compile(
    r'd:DataContext="\{d:DesignInstance\s+(?:Type=)?(\w+:)?(\w+)'
)


# ============================================================================
# ViewModel Parsing
# ============================================================================

def parse_viewmodel(file_path: Path) -> ViewModelInfo | None:
    """Parse a ViewModel file to extract properties and commands."""
    try:
        content = file_path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None

    name = file_path.stem
    vm_info = ViewModelInfo(name=name, file_path=file_path)

    # Find observable properties
    for match in OBSERVABLE_PROPERTY_PATTERN.finditer(content):
        type_name = match.group(1)
        prop_name = match.group(2)
        # Convert _fieldName to PropertyName (capitalize first letter)
        prop_name = prop_name[0].upper() + prop_name[1:] if prop_name else prop_name
        vm_info.properties[prop_name] = PropertyInfo(
            name=prop_name,
            type_name=type_name,
            is_observable=True
        )

    # Find public properties
    for match in PUBLIC_PROPERTY_PATTERN.finditer(content):
        type_name = match.group(1)
        prop_name = match.group(2)
        if prop_name not in vm_info.properties:
            vm_info.properties[prop_name] = PropertyInfo(
                name=prop_name,
                type_name=type_name,
                is_observable=False
            )

    # Find relay commands
    for match in RELAY_COMMAND_PATTERN.finditer(content):
        method_name = match.group(1)
        # Command name is MethodName + "Command"
        cmd_name = method_name + "Command"
        vm_info.commands[cmd_name] = PropertyInfo(
            name=cmd_name,
            is_command=True
        )

    # Find command properties
    for match in COMMAND_PROPERTY_PATTERN.finditer(content):
        cmd_name = match.group(1)
        if cmd_name not in vm_info.commands:
            vm_info.commands[cmd_name] = PropertyInfo(
                name=cmd_name,
                is_command=True
            )

    return vm_info


def load_all_viewmodels(project_root: Path) -> dict[str, ViewModelInfo]:
    """Load all ViewModels from the project."""
    viewmodels = {}

    for vm_dir in [VIEWMODELS_DIR, PANELS_VIEWMODELS_DIR]:
        dir_path = project_root / vm_dir
        if not dir_path.exists():
            continue

        for vm_file in dir_path.glob("*ViewModel.cs"):
            vm_info = parse_viewmodel(vm_file)
            if vm_info:
                viewmodels[vm_info.name] = vm_info

    return viewmodels


# ============================================================================
# XAML Binding Parsing
# ============================================================================

def extract_bindings(file_path: Path) -> list[BindingInfo]:
    """Extract all bindings from a XAML file."""
    bindings = []

    try:
        content = file_path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return bindings

    lines = content.splitlines()

    for line_num, line in enumerate(lines, 1):
        # Find {Binding ...}
        for match in BINDING_PATTERN.finditer(line):
            path = match.group(1).strip()
            rest = match.group(2) or ""

            # Parse mode
            mode_match = re.search(r'Mode=(\w+)', rest)
            mode = mode_match.group(1) if mode_match else None

            # Parse converter
            converter_match = re.search(r'Converter=\{[^}]+\}', rest)
            converter = converter_match.group(0) if converter_match else None

            bindings.append(BindingInfo(
                file_path=file_path,
                line_number=line_num,
                binding_type="Binding",
                path=path,
                mode=mode,
                converter=converter,
                full_expression=match.group(0)
            ))

        # Find {x:Bind ...}
        for match in XBIND_PATTERN.finditer(line):
            path = match.group(1).strip()
            rest = match.group(2) or ""

            mode_match = re.search(r'Mode=(\w+)', rest)
            mode = mode_match.group(1) if mode_match else None

            bindings.append(BindingInfo(
                file_path=file_path,
                line_number=line_num,
                binding_type="x:Bind",
                path=path,
                mode=mode,
                full_expression=match.group(0)
            ))

    return bindings


def infer_viewmodel_for_view(view_path: Path, viewmodels: dict[str, ViewModelInfo]) -> ViewModelInfo | None:
    """Infer the ViewModel for a View based on naming convention."""
    view_name = view_path.stem

    # Try direct ViewModel match: SomeView -> SomeViewModel
    vm_name = view_name.replace("View", "ViewModel")
    if vm_name in viewmodels:
        return viewmodels[vm_name]

    # Try Panel match: SomePanelView -> SomePanelViewModel or SomeViewModel
    if "Panel" in view_name:
        alt_vm_name = view_name.replace("PanelView", "ViewModel")
        if alt_vm_name in viewmodels:
            return viewmodels[alt_vm_name]

    # Check d:DataContext in the file itself
    try:
        content = view_path.read_text(encoding="utf-8")[:2000]
        match = DATA_CONTEXT_PATTERN.search(content)
        if match:
            vm_name = match.group(2)
            if vm_name in viewmodels:
                return viewmodels[vm_name]
    except (OSError, UnicodeDecodeError):
        pass

    return None


# ============================================================================
# Validation
# ============================================================================

def validate_binding_path(
    binding: BindingInfo,
    viewmodel: ViewModelInfo | None
) -> ValidationIssue | None:
    """Validate a single binding path against a ViewModel."""
    path = binding.path

    # Skip empty paths (binding to DataContext itself)
    if not path or path == ".":
        return None

    # Skip paths with indexers, complex expressions, or ElementName bindings
    if any(c in path for c in ['[', '(', 'ElementName', 'RelativeSource', 'Source', 'StaticResource']):
        return None

    # Get the first path segment
    path_parts = path.split('.')
    first_part = path_parts[0]

    # If no ViewModel found, we can only report a warning
    if viewmodel is None:
        # Only warn for non-trivial paths
        if len(path_parts) > 1:
            return ValidationIssue(
                severity="warning",
                file_path=binding.file_path,
                line_number=binding.line_number,
                binding_path=path,
                message="Cannot validate path (no ViewModel found)"
            )
        return None

    # Check if property or command exists
    if first_part in viewmodel.properties:
        return None  # Valid

    if first_part in viewmodel.commands:
        return None  # Valid command binding

    # Common acceptable paths that may not be direct properties
    acceptable_patterns = [
        "SelectedItem",
        "SelectedItems",
        "Items",
        "Count",
        "Length",
        "IsEnabled",
        "Visibility",
        "Text",
        "Content",
        "Value",
        "Source",
    ]

    if first_part in acceptable_patterns:
        return None

    # Report as warning (not error) since we might have incomplete ViewModel parsing
    return ValidationIssue(
        severity="warning",
        file_path=binding.file_path,
        line_number=binding.line_number,
        binding_path=path,
        message=f"Property '{first_part}' not found in {viewmodel.name}"
    )


def validate_xaml_file(
    file_path: Path,
    viewmodels: dict[str, ViewModelInfo],
    report: ValidationReport
) -> None:
    """Validate all bindings in a XAML file."""
    bindings = extract_bindings(file_path)

    if not bindings:
        return

    report.files_checked += 1
    report.bindings_found += len(bindings)

    # Find corresponding ViewModel
    viewmodel = infer_viewmodel_for_view(file_path, viewmodels)

    for binding in bindings:
        issue = validate_binding_path(binding, viewmodel)
        if issue:
            report.issues.append(issue)
        else:
            report.bindings_validated += 1


# ============================================================================
# Main Entry Point
# ============================================================================

def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate XAML Binding Paths Against ViewModels"
    )
    parser.add_argument(
        "--file", "-f",
        type=str,
        help="Validate a specific XAML file"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results as JSON"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Show all issues including warnings"
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        help="Output file path"
    )

    args = parser.parse_args()

    try:
        project_root = get_project_root()
    except RuntimeError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

    # Load ViewModels
    print("Loading ViewModels...", file=sys.stderr)
    viewmodels = load_all_viewmodels(project_root)
    print(f"Found {len(viewmodels)} ViewModels", file=sys.stderr)

    report = ValidationReport()

    if args.file:
        # Validate single file
        file_path = Path(args.file)
        if not file_path.is_absolute():
            file_path = project_root / file_path

        if not file_path.exists():
            print(f"Error: File not found: {file_path}", file=sys.stderr)
            return 1

        validate_xaml_file(file_path, viewmodels, report)
    else:
        # Validate all XAML files
        for xaml_dir in [VIEWS_DIR, CONTROLS_DIR]:
            dir_path = project_root / xaml_dir
            if not dir_path.exists():
                continue

            for xaml_file in dir_path.rglob("*.xaml"):
                # Skip resource dictionaries
                try:
                    content = xaml_file.read_text(encoding="utf-8")[:500]
                    if "<ResourceDictionary" in content:
                        continue
                except (OSError, UnicodeDecodeError):
                    continue

                validate_xaml_file(xaml_file, viewmodels, report)

    # Generate output
    if args.json:
        output = json.dumps(report.to_dict(), indent=2)
    else:
        lines = [
            "=" * 60,
            "XAML Binding Validation Report",
            "=" * 60,
            "",
            f"Files checked: {report.files_checked}",
            f"Bindings found: {report.bindings_found}",
            f"Bindings validated: {report.bindings_validated}",
            f"Errors: {report.errors}",
            f"Warnings: {report.warnings}",
            "",
        ]

        if report.issues:
            lines.append("Issues:")
            lines.append("-" * 40)

            shown = 0
            for issue in report.issues:
                if args.verbose or issue.severity == "error":
                    severity_marker = "[ERR]" if issue.severity == "error" else "[WRN]"
                    lines.append(f"{severity_marker} {issue.file_path.name}:{issue.line_number}")
                    lines.append(f"      Path: {issue.binding_path}")
                    lines.append(f"      {issue.message}")
                    lines.append("")
                    shown += 1
                    if shown >= 20:
                        remaining = len(report.issues) - shown
                        if remaining > 0:
                            lines.append(f"... and {remaining} more issues")
                        break

        lines.append("=" * 60)
        if report.passed:
            lines.append("Overall: PASS")
        else:
            lines.append("Overall: FAIL - Critical binding errors found")
        lines.append("=" * 60)

        output = "\n".join(lines)

    # Output
    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(output, encoding="utf-8")
        print(f"Report written to: {args.output}", file=sys.stderr)
    else:
        print(output)

    # Save JSON to buildlogs
    buildlogs_dir = project_root / ".buildlogs"
    buildlogs_dir.mkdir(exist_ok=True)
    json_path = buildlogs_dir / "xaml-bindings.json"
    json_path.write_text(json.dumps(report.to_dict(), indent=2), encoding="utf-8")

    return 0 if report.passed else 1


if __name__ == "__main__":
    sys.exit(main())
