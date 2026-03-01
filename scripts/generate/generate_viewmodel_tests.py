#!/usr/bin/env python3
"""
ViewModel Test Generator

Generates MSTest unit test scaffolds for ViewModels by analyzing their structure.
Parses public properties, commands, and methods to create comprehensive test stubs.

Usage:
    python scripts/generate_viewmodel_tests.py --viewmodel RealTimeVoiceConverterViewModel
    python scripts/generate_viewmodel_tests.py --all --min-lines 500
    python scripts/generate_viewmodel_tests.py --list-untested
"""

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class PropertyInfo:
    """Information about a ViewModel property."""
    name: str
    type_name: str
    is_observable: bool = False
    has_setter: bool = True


@dataclass
class CommandInfo:
    """Information about a RelayCommand."""
    name: str
    method_name: str
    is_async: bool = False
    has_can_execute: bool = False


@dataclass
class MethodInfo:
    """Information about a public method."""
    name: str
    return_type: str
    is_async: bool = False
    parameters: list[str] = field(default_factory=list)


@dataclass
class ViewModelInfo:
    """Parsed information about a ViewModel."""
    name: str
    file_path: str
    line_count: int
    namespace: str = ""
    base_class: str = "BaseViewModel"
    interfaces: list[str] = field(default_factory=list)
    constructor_params: list[str] = field(default_factory=list)
    properties: list[PropertyInfo] = field(default_factory=list)
    commands: list[CommandInfo] = field(default_factory=list)
    methods: list[MethodInfo] = field(default_factory=list)


def parse_viewmodel(file_path: Path) -> ViewModelInfo | None:
    """Parse a ViewModel file to extract structure information."""
    try:
        content = file_path.read_text(encoding='utf-8')
    except Exception as e:
        print(f"Error reading {file_path}: {e}")
        return None

    lines = content.split('\n')
    line_count = len(lines)

    # Extract class name
    class_match = re.search(
        r'public\s+(?:partial\s+)?class\s+(\w+ViewModel)\s*(?::\s*([^{]+))?',
        content
    )
    if not class_match:
        return None

    name = class_match.group(1)
    inheritance = class_match.group(2) or ""

    # Parse base class and interfaces
    base_class = "BaseViewModel"
    interfaces = []
    if inheritance:
        parts = [p.strip() for p in inheritance.split(',')]
        for part in parts:
            if 'ViewModel' in part or part in ('ObservableObject', 'ObservableRecipient'):
                base_class = part
            elif part.startswith('I'):
                interfaces.append(part)

    # Extract namespace
    ns_match = re.search(r'namespace\s+([\w.]+)', content)
    namespace = ns_match.group(1) if ns_match else "VoiceStudio.App.ViewModels"

    # Extract constructor parameters
    ctor_match = re.search(
        rf'public\s+{name}\s*\(([^)]*)\)',
        content, re.MULTILINE
    )
    constructor_params = []
    if ctor_match:
        params_str = ctor_match.group(1)
        for param in params_str.split(','):
            param = param.strip()
            if param:
                # Extract type and name
                parts = param.split()
                if len(parts) >= 2:
                    constructor_params.append(f"{parts[-2]} {parts[-1]}")

    # Extract ObservableProperty attributes
    properties = []
    observable_pattern = re.compile(
        r'\[ObservableProperty\]\s*(?:\[[\w\(\)="\.]+\]\s*)*private\s+(\w+(?:<[^>]+>)?)\s+_?(\w+)',
        re.MULTILINE
    )
    for match in observable_pattern.finditer(content):
        type_name = match.group(1)
        prop_name = match.group(2)
        # Convert camelCase to PascalCase
        prop_name = prop_name[0].upper() + prop_name[1:] if prop_name else prop_name
        properties.append(PropertyInfo(
            name=prop_name,
            type_name=type_name,
            is_observable=True
        ))

    # Extract regular public properties
    prop_pattern = re.compile(
        r'public\s+(\w+(?:<[^>]+>)?)\s+(\w+)\s*{\s*get;',
        re.MULTILINE
    )
    for match in prop_pattern.finditer(content):
        type_name = match.group(1)
        prop_name = match.group(2)
        if prop_name not in [p.name for p in properties]:
            properties.append(PropertyInfo(
                name=prop_name,
                type_name=type_name,
                is_observable=False
            ))

    # Extract RelayCommand attributes
    commands = []
    command_pattern = re.compile(
        r'\[RelayCommand(?:\([^)]*\))?\]\s*(?:private|public)\s+(?:async\s+)?(?:Task|void)\s+(\w+)',
        re.MULTILINE
    )
    for match in command_pattern.finditer(content):
        method_name = match.group(1)
        is_async = 'async' in match.group(0)
        command_name = method_name + "Command"
        commands.append(CommandInfo(
            name=command_name,
            method_name=method_name,
            is_async=is_async
        ))

    # Extract public async methods (likely to be important for testing)
    methods = []
    method_pattern = re.compile(
        r'public\s+(?:async\s+)?(?:Task(?:<(\w+)>)?|(\w+))\s+(\w+)\s*\(([^)]*)\)',
        re.MULTILINE
    )
    for match in method_pattern.finditer(content):
        task_return = match.group(1)
        direct_return = match.group(2)
        method_name = match.group(3)
        params = match.group(4)

        # Skip property getters and constructors
        if method_name.startswith('get_') or method_name == name:
            continue

        return_type = task_return or direct_return or "void"
        is_async = 'async' in match.group(0) or 'Task' in match.group(0)

        param_list = []
        if params.strip():
            for p in params.split(','):
                p = p.strip()
                if p:
                    param_list.append(p)

        methods.append(MethodInfo(
            name=method_name,
            return_type=return_type,
            is_async=is_async,
            parameters=param_list
        ))

    return ViewModelInfo(
        name=name,
        file_path=str(file_path),
        line_count=line_count,
        namespace=namespace,
        base_class=base_class,
        interfaces=interfaces,
        constructor_params=constructor_params,
        properties=properties,
        commands=commands,
        methods=methods
    )


def generate_test_file(vm_info: ViewModelInfo) -> str:
    """Generate a test file for a ViewModel."""

    # Determine required mocks based on constructor params
    required_mocks = []
    for param in vm_info.constructor_params:
        parts = param.split()
        if len(parts) >= 2:
            type_name = parts[0]
            if type_name.startswith('I'):
                required_mocks.append(type_name)

    test_class_name = f"{vm_info.name}Tests"

    lines = [
        "using Microsoft.VisualStudio.TestTools.UnitTesting;",
        "using Moq;",
        "using System;",
        "using System.Threading.Tasks;",
        "using VoiceStudio.App.ViewModels;",
        "using VoiceStudio.App.Services;",
        "using VoiceStudio.Core.Services;",
        "",
        "namespace VoiceStudio.App.Tests.ViewModels",
        "{",
        "    /// <summary>",
        f"    /// Unit tests for {vm_info.name}.",
        "    /// Auto-generated by generate_viewmodel_tests.py - review and implement.",
        f"    /// Source: {Path(vm_info.file_path).name} ({vm_info.line_count} lines)",
        "    /// </summary>",
        "    [TestClass]",
        f"    public class {test_class_name} : ViewModelTestBase",
        "    {",
    ]

    # Add mock fields - IBackendClient is always included since it's common
    for mock_type in required_mocks:
        if mock_type != 'IViewModelContext':
            mock_name = f"_mock{mock_type[1:]}"  # Remove 'I' prefix
            lines.append(f"        private Mock<{mock_type}>? {mock_name};")

    lines.append("")

    # Add test initialize
    lines.extend([
        "        [TestInitialize]",
        "        public override void TestInitialize()",
        "        {",
        "            base.TestInitialize();",
    ])

    for mock_type in required_mocks:
        if mock_type != 'IViewModelContext':
            mock_name = f"_mock{mock_type[1:]}"
            lines.append(f"            {mock_name} = new Mock<{mock_type}>();")

    lines.extend([
        "        }",
        "",
    ])

    # Add helper to create ViewModel - generate proper instantiation
    lines.extend([
        f"        private {vm_info.name} CreateViewModel()",
        "        {",
        "            // Uses MockContext from ViewModelTestBase and mock services",
    ])

    # Build constructor arguments
    ctor_args = []
    for param in vm_info.constructor_params:
        parts = param.split()
        if len(parts) >= 2:
            type_name = parts[0]
            if type_name == 'IViewModelContext':
                ctor_args.append("MockContext!")
            elif type_name == 'IBackendClient':
                ctor_args.append("_mockBackendClient!.Object")
            elif type_name.startswith('I'):
                mock_name = f"_mock{type_name[1:]}"
                ctor_args.append(f"{mock_name}!.Object")
            else:
                # Non-interface type - use default or null
                ctor_args.append("null!")

    if ctor_args:
        args_str = ", ".join(ctor_args)
        lines.append(f"            return new {vm_info.name}({args_str});")
    else:
        lines.append(f"            return new {vm_info.name}(MockContext!);")

    lines.extend([
        "        }",
        "",
    ])

    # Add region for construction tests
    lines.extend([
        "        #region Construction and Initialization Tests",
        "",
        "        [TestMethod]",
        "        public void Constructor_WithValidDependencies_CreatesInstance()",
        "        {",
        "            // TODO: Implement test",
        "            // Arrange & Act",
        "            // var viewModel = CreateViewModel();",
        "            // Assert.IsNotNull(viewModel);",
        "            Assert.Inconclusive(\"Test not implemented\");",
        "        }",
        "",
    ])

    if any(m.name.startswith('Initialize') for m in vm_info.methods):
        lines.extend([
            "        [TestMethod]",
            "        public async Task InitializeAsync_WhenCalled_LoadsData()",
            "        {",
            "            // TODO: Implement test",
            "            Assert.Inconclusive(\"Test not implemented\");",
            "        }",
            "",
        ])

    lines.extend([
        "        #endregion",
        "",
    ])

    # Add region for property tests
    if vm_info.properties:
        lines.extend([
            "        #region Property Tests",
            "",
        ])

        # Add tests for key observable properties (limit to prevent huge files)
        for prop in vm_info.properties[:10]:
            if prop.is_observable:
                lines.extend([
                    "        [TestMethod]",
                    f"        public void {prop.name}_WhenSet_RaisesPropertyChanged()",
                    "        {",
                    "            // TODO: Implement test",
                    f"            // Test that setting {prop.name} raises PropertyChanged event",
                    "            Assert.Inconclusive(\"Test not implemented\");",
                    "        }",
                    "",
                ])

        lines.extend([
            "        #endregion",
            "",
        ])

    # Add region for command tests
    if vm_info.commands:
        lines.extend([
            "        #region Command Tests",
            "",
        ])

        for cmd in vm_info.commands:
            test_method = "async Task" if cmd.is_async else "void"
            lines.extend([
                "        [TestMethod]",
                f"        public {test_method} {cmd.name}_WhenExecuted_PerformsAction()",
                "        {",
                "            // TODO: Implement test",
                f"            // Test that {cmd.name} executes correctly",
                "            Assert.Inconclusive(\"Test not implemented\");",
                "        }",
                "",
                "        [TestMethod]",
                f"        public void {cmd.name}_CanExecute_ReturnsExpectedValue()",
                "        {",
                "            // TODO: Implement test",
                f"            // Test CanExecute logic for {cmd.name}",
                "            Assert.Inconclusive(\"Test not implemented\");",
                "        }",
                "",
            ])

        lines.extend([
            "        #endregion",
            "",
        ])

    # Add region for method tests
    public_methods = [m for m in vm_info.methods if not m.name.startswith('On') and not m.name.startswith('_')]
    if public_methods:
        lines.extend([
            "        #region Method Tests",
            "",
        ])

        for method in public_methods[:8]:  # Limit to prevent huge files
            test_method = "async Task" if method.is_async else "void"
            lines.extend([
                "        [TestMethod]",
                f"        public {test_method} {method.name}_WhenCalled_ReturnsExpected()",
                "        {",
                "            // TODO: Implement test",
                f"            // Test {method.name} behavior",
                "            Assert.Inconclusive(\"Test not implemented\");",
                "        }",
                "",
            ])

        lines.extend([
            "        #endregion",
            "",
        ])

    # Add region for error handling tests
    lines.extend([
        "        #region Error Handling Tests",
        "",
        "        [TestMethod]",
        "        public void ViewModel_WhenErrorOccurs_HandlesGracefully()",
        "        {",
        "            // TODO: Implement error handling test",
        "            // Test that errors are handled without crashing",
        "            Assert.Inconclusive(\"Test not implemented\");",
        "        }",
        "",
        "        #endregion",
    ])

    lines.extend([
        "    }",
        "}",
    ])

    return '\n'.join(lines)


def get_existing_tests(test_dir: Path) -> set[str]:
    """Get set of ViewModel names that already have tests."""
    existing = set()
    for test_file in test_dir.glob("*ViewModelTests.cs"):
        # Extract ViewModel name from test file
        name = test_file.stem.replace("Tests", "")
        existing.add(name)

    # Also check for *ModelTests.cs which test ViewModel-related models
    for test_file in test_dir.glob("*ModelTests.cs"):
        name = test_file.stem.replace("ModelTests", "ViewModel")
        existing.add(name)

    return existing


def find_viewmodels(viewmodels_dir: Path, panels_dir: Path) -> list[Path]:
    """Find all ViewModel files."""
    files = []

    # Main ViewModels directory
    if viewmodels_dir.exists():
        files.extend(viewmodels_dir.glob("*ViewModel.cs"))

    # Views/Panels directory (some ViewModels are here)
    if panels_dir.exists():
        files.extend(panels_dir.glob("*ViewModel.cs"))

    return files


def main():
    parser = argparse.ArgumentParser(description="Generate ViewModel test scaffolds")
    parser.add_argument("--viewmodel", "-v", help="Generate tests for specific ViewModel")
    parser.add_argument("--all", "-a", action="store_true", help="Generate for all untested ViewModels")
    parser.add_argument("--min-lines", type=int, default=500, help="Minimum lines for --all (default: 500)")
    parser.add_argument("--list-untested", "-l", action="store_true", help="List untested ViewModels")
    parser.add_argument("--output-dir", "-o", help="Output directory for test files")
    parser.add_argument("--dry-run", "-n", action="store_true", help="Print output without writing files")

    args = parser.parse_args()

    # Determine paths
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent

    viewmodels_dir = repo_root / "src" / "VoiceStudio.App" / "ViewModels"
    panels_dir = repo_root / "src" / "VoiceStudio.App" / "Views" / "Panels"
    test_dir = repo_root / "src" / "VoiceStudio.App.Tests" / "ViewModels"

    output_dir = Path(args.output_dir) if args.output_dir else test_dir

    # Find all ViewModels
    vm_files = find_viewmodels(viewmodels_dir, panels_dir)
    existing_tests = get_existing_tests(test_dir)

    # Parse all ViewModels
    viewmodels = []
    for vm_file in vm_files:
        vm_info = parse_viewmodel(vm_file)
        if vm_info:
            vm_info.has_tests = vm_info.name in existing_tests
            viewmodels.append(vm_info)

    # Sort by line count (largest first)
    viewmodels.sort(key=lambda x: x.line_count, reverse=True)

    if args.list_untested:
        print("Untested ViewModels (sorted by line count):")
        print("-" * 60)
        print(f"{'ViewModel':<45} {'Lines':>6} {'Has Test':>10}")
        print("-" * 60)

        for vm in viewmodels:
            status = "YES" if vm.has_tests else "NO"
            print(f"{vm.name:<45} {vm.line_count:>6} {status:>10}")

        # Summary
        tested = sum(1 for vm in viewmodels if vm.has_tests)
        total = len(viewmodels)
        print("-" * 60)
        print(f"Coverage: {tested}/{total} ({100*tested/total:.1f}%)")

        untested_large = [vm for vm in viewmodels if not vm.has_tests and vm.line_count >= 500]
        print(f"Untested with 500+ lines: {len(untested_large)}")

        return 0

    if args.viewmodel:
        # Generate for specific ViewModel
        target = [vm for vm in viewmodels if vm.name == args.viewmodel]
        if not target:
            print(f"Error: ViewModel '{args.viewmodel}' not found")
            return 1
        targets = target
    elif args.all:
        # Generate for all untested ViewModels meeting threshold
        targets = [
            vm for vm in viewmodels
            if not vm.has_tests and vm.line_count >= args.min_lines
        ]
        if not targets:
            print(f"No untested ViewModels with {args.min_lines}+ lines found")
            return 0
    else:
        parser.print_help()
        return 1

    # Generate test files
    generated = 0
    for vm_info in targets:
        test_content = generate_test_file(vm_info)
        test_filename = f"{vm_info.name}Tests.cs"
        test_path = output_dir / test_filename

        if args.dry_run:
            print(f"\n{'='*60}")
            print(f"Would generate: {test_path}")
            print(f"{'='*60}")
            print(test_content[:500] + "..." if len(test_content) > 500 else test_content)
        else:
            if test_path.exists():
                print(f"Skipping {test_filename} - already exists")
                continue

            output_dir.mkdir(parents=True, exist_ok=True)
            test_path.write_text(test_content, encoding='utf-8')
            print(f"Generated: {test_path}")
            generated += 1

    if not args.dry_run:
        print(f"\nGenerated {generated} test file(s)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
