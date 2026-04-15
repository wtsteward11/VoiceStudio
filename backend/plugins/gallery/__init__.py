"""
Plugin Gallery module.

D.1 Enhancement: Plugin Gallery Infrastructure.
Phase 5B Enhancement: Adds verification integration.
Phase 5C M2: Gallery v2 with multi-catalog, search, filtering, and ratings.
Phase 5C M4: Lockfile system for version pinning and deployment consistency.
Phase 5C M6: Dependency resolver with topological resolution and conflict detection.
Phase 5C M7: Enhanced installer with .vspkg support, verification, and atomic install.

Provides catalog fetching, installation, and update management with
integrated supply chain security verification, full-text search,
local-first ratings support, lockfile-based version pinning,
dependency resolution with conflict detection, and atomic installation
with rollback capabilities.
"""

from __future__ import annotations

import importlib
from typing import Any

# Submodules that import aiohttp at module level are loaded lazily so that
# importing e.g. dependency_resolver (pure logic) does not require aiohttp
# until catalog/installer/multi-catalog symbols are accessed.
_LAZY_EXPORT_MODULE: dict[str, str] = {
    # catalog
    "PluginCatalogService": "catalog",
    "get_catalog_service": "catalog",
    # installer
    "PluginInstallService": "installer",
    "get_install_service": "installer",
    # installer_v2
    "AtomicInstallResult": "installer_v2",
    "BackupInfo": "installer_v2",
    "InstallAction": "installer_v2",
    "InstallTransaction": "installer_v2",
    "PackageVerificationResult": "installer_v2",
    "PluginInstallerV2": "installer_v2",
    "TransactionState": "installer_v2",
    "get_installer_v2": "installer_v2",
    "install_from_vspkg": "installer_v2",
    "install_plugin_atomic": "installer_v2",
    "rollback_plugin": "installer_v2",
    # multi_catalog
    "CatalogPriority": "multi_catalog",
    "CatalogSource": "multi_catalog",
    "CatalogType": "multi_catalog",
    "MultiCatalogConfig": "multi_catalog",
    "MultiCatalogService": "multi_catalog",
    "add_catalog_source": "multi_catalog",
    "get_merged_catalog": "multi_catalog",
    "get_multi_catalog_service": "multi_catalog",
    "remove_catalog_source": "multi_catalog",
}


def __getattr__(name: str) -> Any:
    sub = _LAZY_EXPORT_MODULE.get(name)
    if sub is None:
        raise AttributeError(f"module {__name__!r} has no attribute {name!r}")
    mod = importlib.import_module(f".{sub}", __name__)
    val = getattr(mod, name)
    globals()[name] = val
    return val


def __dir__() -> list[str]:
    return sorted(__all__)


# Phase 5C M6: Dependency resolver
from .dependency_resolver import (
    Conflict,
    ConflictType,
    Dependency,
    DependencyResolver,
    ResolutionResult,
    ResolvedDependency,
    Version,
    VersionConstraint,
    VersionOperator,
    VersionSpec,
    check_compatibility,
    get_dependency_resolver,
    resolve_dependencies,
)
from .gallery_v2 import (
    GalleryPlugin,
    GallerySearchResponse,
    GalleryStats,
    PluginGalleryV2,
    get_gallery_plugin,
    get_gallery_v2,
    refresh_gallery,
    search_gallery,
)

# Phase 5C M4: Lockfile system
from .lockfile import (
    LockedDependency,
    LockedPlugin,
    Lockfile,
    LockfileConflict,
    LockfileManager,
    LockfileStatus,
    LockfileValidationResult,
    ResolutionStrategy,
    generate_lockfile,
    get_lockfile_manager,
    lock_plugin,
    unlock_plugin,
    validate_lockfile,
)
from .models import (
    CatalogPlugin,
    InstalledPlugin,
    InstallPhase,
    InstallProgress,
    PluginCatalog,
    PluginVersion,
)
from .ratings import (
    PluginRating,
    PluginRatingsStore,
    PluginRatingStats,
    get_my_rating,
    get_ratings_store,
    rate_plugin,
    remove_rating,
)

# Phase 5C M2: Gallery v2 components
from .search import (
    PluginSearchEngine,
    SearchFilter,
    SearchQuery,
    SearchResponse,
    SearchResult,
    SortField,
    SortOrder,
    get_search_engine,
    search_plugins,
)
from .verification import (
    PluginVerificationService,
    VerificationCheck,
    VerificationLevel,
    VerificationPolicy,
    VerificationResult,
    VerificationStatus,
    get_verification_service,
    verify_package,
)

__all__ = [
    # Installer v2 (Phase 5C M7)
    "AtomicInstallResult",
    "BackupInfo",
    # Original models
    "CatalogPlugin",
    # Multi-Catalog (Phase 5C M2)
    "CatalogPriority",
    "CatalogSource",
    "CatalogType",
    # Dependency Resolver (Phase 5C M6)
    "Conflict",
    "ConflictType",
    "Dependency",
    "DependencyResolver",
    # Gallery v2 (Phase 5C M2)
    "GalleryPlugin",
    "GallerySearchResponse",
    "GalleryStats",
    "InstallAction",
    "InstallPhase",
    "InstallProgress",
    "InstallTransaction",
    "InstalledPlugin",
    "LockedDependency",
    "LockedPlugin",
    # Lockfile (Phase 5C M4)
    "Lockfile",
    "LockfileConflict",
    "LockfileManager",
    "LockfileStatus",
    "LockfileValidationResult",
    "MultiCatalogConfig",
    "MultiCatalogService",
    "PackageVerificationResult",
    "PluginCatalog",
    "PluginCatalogService",
    "PluginGalleryV2",
    "PluginInstallService",
    "PluginInstallerV2",
    # Ratings (Phase 5C M2)
    "PluginRating",
    "PluginRatingStats",
    "PluginRatingsStore",
    # Search (Phase 5C M2)
    "PluginSearchEngine",
    # Verification (Phase 5B)
    "PluginVerificationService",
    "PluginVersion",
    "ResolutionResult",
    "ResolutionStrategy",
    "ResolvedDependency",
    "SearchFilter",
    "SearchQuery",
    "SearchResponse",
    "SearchResult",
    "SortField",
    "SortOrder",
    "TransactionState",
    "VerificationCheck",
    "VerificationLevel",
    "VerificationPolicy",
    "VerificationResult",
    "VerificationStatus",
    "Version",
    "VersionConstraint",
    "VersionOperator",
    "VersionSpec",
    "add_catalog_source",
    "check_compatibility",
    "generate_lockfile",
    "get_catalog_service",
    "get_dependency_resolver",
    "get_gallery_plugin",
    "get_gallery_v2",
    "get_install_service",
    "get_installer_v2",
    "get_lockfile_manager",
    "get_merged_catalog",
    "get_multi_catalog_service",
    "get_my_rating",
    "get_ratings_store",
    "get_search_engine",
    "get_verification_service",
    "install_from_vspkg",
    "install_plugin_atomic",
    "lock_plugin",
    "rate_plugin",
    "refresh_gallery",
    "remove_catalog_source",
    "remove_rating",
    "resolve_dependencies",
    "rollback_plugin",
    "search_gallery",
    "search_plugins",
    "unlock_plugin",
    "validate_lockfile",
    "verify_package",
]
