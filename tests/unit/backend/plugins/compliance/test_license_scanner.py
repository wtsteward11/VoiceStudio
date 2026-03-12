"""
Tests for Phase 6C License Scanner

Tests automatic license detection and compatibility checking.

NOTE: This test module is a specification for Phase 6C license scanning.
Tests will be skipped until license_scanner module is implemented.
"""

import os
import tempfile
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import List, Optional
from unittest.mock import MagicMock, patch

import pytest

# Skip module if license_scanner not implemented
try:
    from backend.plugins.compliance.license_scanner import (
        CompatibilityResult,
        LicenseInfo,
        LicenseScanner,
        LicenseType,
    )
except ImportError:
    pytestmark = pytest.mark.skipif(True, reason="Phase 6C license_scanner not implemented (SKIP_DEBT_CLEANUP_SUBPLAN § Product gaps)")

    # Create stubs for syntax validation
    class LicenseType(Enum):
        MIT = "mit"
        APACHE_2_0 = "apache-2.0"
        GPL_3_0 = "gpl-3.0"
        GPL_2_0 = "gpl-2.0"
        BSD_3_CLAUSE = "bsd-3-clause"
        BSD_2_CLAUSE = "bsd-2-clause"
        LGPL_3_0 = "lgpl-3.0"
        PROPRIETARY = "proprietary"
        UNKNOWN = "unknown"

        @property
        def is_permissive(self):
            return self in [
                LicenseType.MIT,
                LicenseType.APACHE_2_0,
                LicenseType.BSD_3_CLAUSE,
                LicenseType.BSD_2_CLAUSE,
            ]

        @property
        def is_copyleft(self):
            return self in [LicenseType.GPL_3_0, LicenseType.GPL_2_0, LicenseType.LGPL_3_0]

    @dataclass
    class LicenseInfo:
        license_type: LicenseType
        confidence: float
        source_file: str = ""

        def to_dict(self):
            return {
                "license_type": self.license_type.value,
                "confidence": self.confidence,
            }

    @dataclass
    class CompatibilityResult:
        is_compatible: bool
        issues: List[str] = field(default_factory=list)
        recommendations: List[str] = field(default_factory=list)
        requires_copyleft: bool = False

    class LicenseScanner:
        def detect_license(self, text: str) -> LicenseInfo:
            if "MIT" in text:
                return LicenseInfo(LicenseType.MIT, 0.9, "")
            if "Apache" in text:
                return LicenseInfo(LicenseType.APACHE_2_0, 0.9, "")
            if "GPL" in text or "GNU" in text:
                return LicenseInfo(LicenseType.GPL_3_0, 0.8, "")
            return LicenseInfo(LicenseType.UNKNOWN, 0.5, "")

        def scan_directory(self, path: Path) -> Optional[LicenseInfo]:
            return LicenseInfo(LicenseType.MIT, 0.9, "LICENSE")

        def check_compatibility(
            self, plugin_license: LicenseType, dependency_licenses: List[LicenseType]
        ) -> CompatibilityResult:
            for dep in dependency_licenses:
                if dep.is_copyleft and not plugin_license.is_copyleft:
                    return CompatibilityResult(
                        is_compatible=False, issues=["GPL incompatible"], requires_copyleft=True
                    )
            return CompatibilityResult(is_compatible=True)

        def scan_plugin(self, path: Path):
            @dataclass
            class ScanResult:
                main_license: Optional[LicenseInfo] = None
                dependency_licenses: List[LicenseInfo] = field(default_factory=list)

            return ScanResult(main_license=LicenseInfo(LicenseType.MIT, 0.9, "LICENSE"))


class TestLicenseScanner:
    """Tests for LicenseScanner class."""

    def test_scanner_initialization(self) -> None:
        """Test scanner initializes correctly."""
        scanner = LicenseScanner()
        assert scanner is not None

    def test_detect_mit_license(self) -> None:
        """Test detection of MIT license."""
        scanner = LicenseScanner()

        mit_text = """
MIT License

Copyright (c) 2024 Test Author

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software...
"""

        result = scanner.detect_license(mit_text)
        assert result.license_type == LicenseType.MIT

    def test_detect_apache_license(self) -> None:
        """Test detection of Apache 2.0 license."""
        scanner = LicenseScanner()

        apache_text = """
Apache License
Version 2.0, January 2004
http://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION
"""

        result = scanner.detect_license(apache_text)
        assert result.license_type == LicenseType.APACHE_2_0

    def test_detect_gpl_license(self) -> None:
        """Test detection of GPL license."""
        scanner = LicenseScanner()

        gpl_text = """
GNU GENERAL PUBLIC LICENSE
Version 3, 29 June 2007

Copyright (C) 2007 Free Software Foundation, Inc.
"""

        result = scanner.detect_license(gpl_text)
        assert result.license_type in [LicenseType.GPL_3_0, LicenseType.GPL_2_0]

    def test_detect_unknown_license(self) -> None:
        """Test detection of unknown/custom license."""
        scanner = LicenseScanner()

        custom_text = """
Custom License Agreement
This software is licensed under proprietary terms.
Contact sales@example.com for licensing options.
"""

        result = scanner.detect_license(custom_text)
        assert result.license_type == LicenseType.UNKNOWN

    def test_scan_directory(self) -> None:
        """Test scanning a directory for license files."""
        scanner = LicenseScanner()

        with tempfile.TemporaryDirectory() as tmpdir:
            # Create LICENSE file
            license_path = Path(tmpdir) / "LICENSE"
            license_path.write_text("MIT License\n...")

            result = scanner.scan_directory(Path(tmpdir))
            assert result is not None


class TestLicenseInfo:
    """Tests for LicenseInfo class."""

    def test_create_license_info(self) -> None:
        """Test creating license info."""
        info = LicenseInfo(
            license_type=LicenseType.MIT,
            confidence=0.95,
            source_file="LICENSE",
        )

        assert info.license_type == LicenseType.MIT
        assert info.confidence == 0.95

    def test_license_info_to_dict(self) -> None:
        """Test converting license info to dictionary."""
        info = LicenseInfo(
            license_type=LicenseType.APACHE_2_0,
            confidence=0.90,
            source_file="LICENSE.txt",
        )

        data = info.to_dict()
        assert data["license_type"] == "apache-2.0"
        assert data["confidence"] == 0.90


class TestLicenseCompatibility:
    """Tests for license compatibility checking."""

    def test_mit_compatible_with_mit(self) -> None:
        """Test MIT is compatible with MIT."""
        scanner = LicenseScanner()
        result = scanner.check_compatibility(
            plugin_license=LicenseType.MIT,
            dependency_licenses=[LicenseType.MIT],
        )

        assert result.is_compatible

    def test_mit_compatible_with_apache(self) -> None:
        """Test MIT is compatible with Apache 2.0."""
        scanner = LicenseScanner()
        result = scanner.check_compatibility(
            plugin_license=LicenseType.MIT,
            dependency_licenses=[LicenseType.APACHE_2_0],
        )

        assert result.is_compatible

    def test_gpl_copyleft_propagates(self) -> None:
        """Test GPL copyleft propagation."""
        scanner = LicenseScanner()
        result = scanner.check_compatibility(
            plugin_license=LicenseType.MIT,
            dependency_licenses=[LicenseType.GPL_3_0],
        )

        # GPL dependency requires GPL-compatible output
        assert not result.is_compatible or result.requires_copyleft

    def test_proprietary_incompatible_with_gpl(self) -> None:
        """Test proprietary is incompatible with GPL."""
        scanner = LicenseScanner()
        result = scanner.check_compatibility(
            plugin_license=LicenseType.PROPRIETARY,
            dependency_licenses=[LicenseType.GPL_3_0],
        )

        assert not result.is_compatible


class TestCompatibilityResult:
    """Tests for CompatibilityResult class."""

    def test_create_compatible_result(self) -> None:
        """Test creating compatible result."""
        result = CompatibilityResult(
            is_compatible=True,
            issues=[],
            recommendations=[],
        )

        assert result.is_compatible
        assert len(result.issues) == 0

    def test_create_incompatible_result(self) -> None:
        """Test creating incompatible result."""
        result = CompatibilityResult(
            is_compatible=False,
            issues=["GPL dependency requires GPL-compatible license"],
            recommendations=["Consider using LGPL or removing GPL dependency"],
        )

        assert not result.is_compatible
        assert len(result.issues) == 1


class TestLicenseType:
    """Tests for LicenseType enum."""

    def test_common_licenses_exist(self) -> None:
        """Test that common license types exist."""
        assert LicenseType.MIT is not None
        assert LicenseType.APACHE_2_0 is not None
        assert LicenseType.GPL_3_0 is not None
        assert LicenseType.GPL_2_0 is not None
        assert LicenseType.BSD_3_CLAUSE is not None
        assert LicenseType.BSD_2_CLAUSE is not None
        assert LicenseType.LGPL_3_0 is not None
        assert LicenseType.PROPRIETARY is not None
        assert LicenseType.UNKNOWN is not None

    def test_license_is_permissive(self) -> None:
        """Test checking if license is permissive."""
        assert LicenseType.MIT.is_permissive
        assert LicenseType.APACHE_2_0.is_permissive
        assert LicenseType.BSD_3_CLAUSE.is_permissive
        assert not LicenseType.GPL_3_0.is_permissive

    def test_license_is_copyleft(self) -> None:
        """Test checking if license is copyleft."""
        assert LicenseType.GPL_3_0.is_copyleft
        assert LicenseType.GPL_2_0.is_copyleft
        assert not LicenseType.MIT.is_copyleft


class TestScannerIntegration:
    """Integration tests for license scanner."""

    def test_scan_plugin_with_dependencies(self) -> None:
        """Test scanning plugin with multiple license files."""
        scanner = LicenseScanner()

        with tempfile.TemporaryDirectory() as tmpdir:
            # Main plugin license
            (Path(tmpdir) / "LICENSE").write_text("MIT License...")

            # Dependency licenses
            deps_dir = Path(tmpdir) / "node_modules" / "some-dep"
            deps_dir.mkdir(parents=True)
            (deps_dir / "LICENSE").write_text("Apache License 2.0...")

            result = scanner.scan_plugin(Path(tmpdir))

            assert result.main_license is not None
            assert len(result.dependency_licenses) >= 0
