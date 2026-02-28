"""
Privacy Engine for Plugin Data Handling.

Phase 6C: GDPR-inspired privacy enforcement for plugin data.
Implements data handling, anonymization, export, and deletion.

Privacy Features:
1. Data Inventory: Track what data each plugin stores
2. Anonymization: Remove/hash PII from plugin data
3. Data Export: Export user's plugin data (portability)
4. Data Deletion: Delete user's plugin data (right to erasure)
5. Consent Management: Track user consent per plugin

Privacy Levels:
- MINIMAL: No data collection
- STANDARD: Anonymous usage stats only
- EXTENDED: Usage + preferences (with consent)
- FULL: All data (with explicit consent)

Usage:
    engine = PrivacyEngine(data_store_path)

    # Register plugin data categories
    engine.register_plugin_data(
        plugin_id="my-plugin",
        categories=[DataCategory.USAGE, DataCategory.PREFERENCES],
    )

    # Handle data export request
    data = await engine.export_user_data(user_id="user-123")

    # Handle deletion request
    await engine.delete_user_data(user_id="user-123", plugin_id="my-plugin")
"""

from __future__ import annotations

import hashlib
import json
import logging
import re
import shutil
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Set

logger = logging.getLogger(__name__)


class PrivacyLevel(Enum):
    """Privacy levels for data collection."""

    MINIMAL = "minimal"
    STANDARD = "standard"
    EXTENDED = "extended"
    FULL = "full"

    @property
    def allows_telemetry(self) -> bool:
        return self in {PrivacyLevel.STANDARD, PrivacyLevel.EXTENDED, PrivacyLevel.FULL}

    @property
    def allows_preferences(self) -> bool:
        return self in {PrivacyLevel.EXTENDED, PrivacyLevel.FULL}

    @property
    def allows_pii(self) -> bool:
        return self == PrivacyLevel.FULL


class DataCategory(Enum):
    """Categories of data that plugins can collect."""

    USAGE = "usage"
    PREFERENCES = "preferences"
    TELEMETRY = "telemetry"
    AUDIO = "audio"
    FILES = "files"
    LOGS = "logs"
    CACHE = "cache"
    CREDENTIALS = "credentials"


class RequestType(Enum):
    """Types of user data requests."""

    ACCESS = "access"
    EXPORT = "export"
    DELETE = "delete"
    RECTIFY = "rectify"
    RESTRICT = "restrict"


@dataclass
class PluginDataDeclaration:
    """Declaration of data a plugin collects."""

    plugin_id: str
    categories: List[DataCategory]
    privacy_level: PrivacyLevel
    retention_days: int = 365
    description: str = ""
    consent_required: bool = False

    def to_dict(self) -> Dict[str, Any]:
        return {
            "plugin_id": self.plugin_id,
            "categories": [c.value for c in self.categories],
            "privacy_level": self.privacy_level.value,
            "retention_days": self.retention_days,
            "description": self.description,
            "consent_required": self.consent_required,
        }


@dataclass
class UserDataRequest:
    """A user data request (GDPR-style)."""

    request_id: str
    user_id: str
    request_type: RequestType
    plugin_id: Optional[str] = None
    status: str = "pending"
    created_at: datetime = field(default_factory=datetime.utcnow)
    completed_at: Optional[datetime] = None
    result: Optional[Dict[str, Any]] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "request_id": self.request_id,
            "user_id": self.user_id,
            "request_type": self.request_type.value,
            "plugin_id": self.plugin_id,
            "status": self.status,
            "created_at": self.created_at.isoformat(),
            "completed_at": self.completed_at.isoformat() if self.completed_at else None,
        }


@dataclass
class UserConsent:
    """Record of user consent for a plugin."""

    user_id: str
    plugin_id: str
    privacy_level: PrivacyLevel
    categories: List[DataCategory]
    granted_at: datetime
    expires_at: Optional[datetime] = None
    revoked_at: Optional[datetime] = None

    @property
    def is_valid(self) -> bool:
        if self.revoked_at:
            return False
        return not (self.expires_at and datetime.utcnow() > self.expires_at)


class PrivacyEngine:
    """
    Privacy engine for plugin data handling.

    Implements GDPR-inspired privacy controls for plugin data.
    All operations are local-only with no cloud dependencies.

    Example:
        engine = PrivacyEngine(Path("data/plugins"))

        # Register plugin data declaration
        engine.register_plugin_data(PluginDataDeclaration(
            plugin_id="audio-fx",
            categories=[DataCategory.USAGE, DataCategory.PREFERENCES],
            privacy_level=PrivacyLevel.STANDARD,
        ))

        # Record user consent
        engine.record_consent(
            user_id="user-123",
            plugin_id="audio-fx",
            privacy_level=PrivacyLevel.STANDARD,
            categories=[DataCategory.USAGE],
        )

        # Export user data
        data = await engine.export_user_data(user_id="user-123")

        # Delete user data
        await engine.delete_user_data(user_id="user-123")
    """

    def __init__(
        self,
        data_store_path: Optional[Path] = None,
        default_retention_days: int = 365,
        auto_anonymize: bool = True,
    ):
        """
        Initialize privacy engine.

        Args:
            data_store_path: Path to plugin data storage
            default_retention_days: Default data retention period
            auto_anonymize: Automatically anonymize expired data
        """
        self._data_store_path = data_store_path or Path("data/plugins")
        self._default_retention_days = default_retention_days
        self._auto_anonymize = auto_anonymize

        # Plugin data declarations
        self._declarations: Dict[str, PluginDataDeclaration] = {}

        # User consents
        self._consents: Dict[str, Dict[str, UserConsent]] = {}

        # Pending requests
        self._requests: Dict[str, UserDataRequest] = {}

        # Anonymization functions by data type
        self._anonymizers: Dict[str, Callable[[str], str]] = {
            "email": self._anonymize_email,
            "ip": self._anonymize_ip,
            "name": self._anonymize_name,
            "id": self._anonymize_id,
        }

    def register_plugin_data(self, declaration: PluginDataDeclaration):
        """Register a plugin's data declaration."""
        self._declarations[declaration.plugin_id] = declaration
        logger.info(f"Registered data declaration for {declaration.plugin_id}")

    def get_plugin_declaration(
        self,
        plugin_id: str,
    ) -> Optional[PluginDataDeclaration]:
        """Get a plugin's data declaration."""
        return self._declarations.get(plugin_id)

    def record_consent(
        self,
        user_id: str,
        plugin_id: str,
        privacy_level: PrivacyLevel,
        categories: List[DataCategory],
        expires_days: Optional[int] = None,
    ) -> UserConsent:
        """
        Record user consent for a plugin.

        Args:
            user_id: User identifier
            plugin_id: Plugin identifier
            privacy_level: Consented privacy level
            categories: Consented data categories
            expires_days: Days until consent expires

        Returns:
            UserConsent record
        """
        expires_at = None
        if expires_days:
            from datetime import timedelta

            expires_at = datetime.utcnow() + timedelta(days=expires_days)

        consent = UserConsent(
            user_id=user_id,
            plugin_id=plugin_id,
            privacy_level=privacy_level,
            categories=categories,
            granted_at=datetime.utcnow(),
            expires_at=expires_at,
        )

        if user_id not in self._consents:
            self._consents[user_id] = {}
        self._consents[user_id][plugin_id] = consent

        logger.info(f"Recorded consent: user={user_id}, plugin={plugin_id}")
        return consent

    def revoke_consent(self, user_id: str, plugin_id: str) -> bool:
        """Revoke user consent for a plugin."""
        if user_id in self._consents and plugin_id in self._consents[user_id]:
            self._consents[user_id][plugin_id].revoked_at = datetime.utcnow()
            logger.info(f"Revoked consent: user={user_id}, plugin={plugin_id}")
            return True
        return False

    def check_consent(
        self,
        user_id: str,
        plugin_id: str,
        category: DataCategory,
    ) -> bool:
        """Check if user has consented to data category for plugin."""
        consent = self._consents.get(user_id, {}).get(plugin_id)
        if not consent or not consent.is_valid:
            return False
        return category in consent.categories

    async def export_user_data(
        self,
        user_id: str,
        plugin_id: Optional[str] = None,
    ) -> Dict[str, Any]:
        """
        Export all user data (data portability).

        Args:
            user_id: User identifier
            plugin_id: Optional specific plugin to export

        Returns:
            Dictionary with all user data
        """
        export_data: Dict[str, Any] = {
            "user_id": user_id,
            "exported_at": datetime.utcnow().isoformat(),
            "plugins": {},
        }

        plugins_to_export = [plugin_id] if plugin_id else list(self._declarations.keys())

        for pid in plugins_to_export:
            plugin_data = await self._collect_plugin_data(user_id, pid)
            if plugin_data:
                export_data["plugins"][pid] = plugin_data

        # Include consents
        export_data["consents"] = {
            pid: {
                "privacy_level": c.privacy_level.value,
                "categories": [cat.value for cat in c.categories],
                "granted_at": c.granted_at.isoformat(),
                "is_valid": c.is_valid,
            }
            for pid, c in self._consents.get(user_id, {}).items()
        }

        logger.info(f"Exported data for user={user_id}")
        return export_data

    async def delete_user_data(
        self,
        user_id: str,
        plugin_id: Optional[str] = None,
        keep_anonymized: bool = False,
    ) -> Dict[str, Any]:
        """
        Delete all user data (right to erasure).

        Args:
            user_id: User identifier
            plugin_id: Optional specific plugin to delete
            keep_anonymized: Keep anonymized version for analytics

        Returns:
            Summary of deleted data
        """
        summary: Dict[str, Any] = {
            "user_id": user_id,
            "deleted_at": datetime.utcnow().isoformat(),
            "plugins": {},
        }

        plugins_to_delete = [plugin_id] if plugin_id else list(self._declarations.keys())

        for pid in plugins_to_delete:
            deleted = await self._delete_plugin_data(user_id, pid, keep_anonymized)
            summary["plugins"][pid] = deleted

        # Revoke all consents for deleted plugins
        if user_id in self._consents:
            for pid in plugins_to_delete:
                if pid in self._consents[user_id]:
                    self._consents[user_id][pid].revoked_at = datetime.utcnow()

        logger.info(f"Deleted data for user={user_id}")
        return summary

    async def _collect_plugin_data(
        self,
        user_id: str,
        plugin_id: str,
    ) -> Dict[str, Any]:
        """Collect all data stored by a plugin for a user."""
        data: Dict[str, Any] = {}

        declaration = self._declarations.get(plugin_id)
        if not declaration:
            return data

        # Check each category
        for category in declaration.categories:
            category_path = self._data_store_path / plugin_id / category.value / user_id
            if category_path.exists():
                try:
                    if category_path.is_file():
                        data[category.value] = json.loads(category_path.read_text())
                    elif category_path.is_dir():
                        data[category.value] = {
                            f.name: (
                                json.loads(f.read_text()) if f.suffix == ".json" else f.read_text()
                            )
                            for f in category_path.iterdir()
                        }
                except Exception as e:
                    logger.error(f"Error collecting {category.value} data: {e}")

        return data

    async def _delete_plugin_data(
        self,
        user_id: str,
        plugin_id: str,
        keep_anonymized: bool,
    ) -> Dict[str, Any]:
        """Delete all data stored by a plugin for a user."""
        deleted: Dict[str, Any] = {"categories": [], "files_deleted": 0}

        declaration = self._declarations.get(plugin_id)
        if not declaration:
            return deleted

        for category in declaration.categories:
            category_path = self._data_store_path / plugin_id / category.value / user_id
            if category_path.exists():
                if keep_anonymized:
                    # Anonymize before deletion
                    await self._anonymize_path(category_path)

                try:
                    if category_path.is_file():
                        category_path.unlink()
                        deleted["files_deleted"] += 1
                    elif category_path.is_dir():
                        file_count = len(list(category_path.iterdir()))
                        shutil.rmtree(category_path)
                        deleted["files_deleted"] += file_count

                    deleted["categories"].append(category.value)
                except Exception as e:
                    logger.error(f"Error deleting {category.value} data: {e}")

        return deleted

    async def _anonymize_path(self, path: Path):
        """Anonymize all data at a path."""
        if path.is_file():
            await self._anonymize_file(path)
        elif path.is_dir():
            for file in path.rglob("*"):
                if file.is_file():
                    await self._anonymize_file(file)

    async def _anonymize_file(self, file_path: Path):
        """Anonymize data in a file."""
        try:
            content = file_path.read_text()

            # Try to parse as JSON
            try:
                data = json.loads(content)
                anonymized = self._anonymize_dict(data)
                file_path.write_text(json.dumps(anonymized, indent=2))
            except json.JSONDecodeError:
                # Plain text - anonymize PII patterns
                anonymized = self._anonymize_text(content)
                file_path.write_text(anonymized)

        except Exception as e:
            logger.error(f"Error anonymizing {file_path}: {e}")

    def _anonymize_dict(self, data: Dict[str, Any]) -> Dict[str, Any]:
        """Anonymize PII fields in a dictionary."""
        anonymized = {}

        pii_fields = {
            "email",
            "name",
            "username",
            "user_name",
            "user_id",
            "ip",
            "ip_address",
            "phone",
            "address",
        }

        for key, value in data.items():
            key_lower = key.lower()

            if isinstance(value, dict):
                anonymized[key] = self._anonymize_dict(value)
            elif isinstance(value, list):
                anonymized[key] = [
                    self._anonymize_dict(v) if isinstance(v, dict) else v for v in value
                ]
            elif key_lower in pii_fields or "email" in key_lower:
                anonymized[key] = self._hash_value(str(value))
            elif "name" in key_lower:
                anonymized[key] = self._anonymize_name(str(value))
            else:
                anonymized[key] = value

        return anonymized

    def _anonymize_text(self, text: str) -> str:
        """Anonymize PII patterns in text."""
        # Email pattern
        text = re.sub(
            r"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            lambda m: self._anonymize_email(m.group()),
            text,
        )

        # IP pattern
        text = re.sub(
            r"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
            lambda m: self._anonymize_ip(m.group()),
            text,
        )

        return text

    def _anonymize_email(self, email: str) -> str:
        """Anonymize email address."""
        parts = email.split("@")
        if len(parts) == 2:
            return f"{self._hash_value(parts[0])[:8]}@{parts[1]}"
        return self._hash_value(email)[:16]

    def _anonymize_ip(self, ip: str) -> str:
        """Anonymize IP address."""
        parts = ip.split(".")
        if len(parts) == 4:
            return f"{parts[0]}.{parts[1]}.xxx.xxx"
        return "0.0.0.0"

    def _anonymize_name(self, name: str) -> str:
        """Anonymize a name."""
        return f"User_{self._hash_value(name)[:6]}"

    def _anonymize_id(self, id_value: str) -> str:
        """Anonymize an identifier."""
        return self._hash_value(id_value)[:12]

    def _hash_value(self, value: str) -> str:
        """Create a one-way hash of a value."""
        return hashlib.sha256(value.encode()).hexdigest()

    def create_data_request(
        self,
        user_id: str,
        request_type: RequestType,
        plugin_id: Optional[str] = None,
    ) -> UserDataRequest:
        """Create a new data request."""
        import uuid

        request = UserDataRequest(
            request_id=str(uuid.uuid4()),
            user_id=user_id,
            request_type=request_type,
            plugin_id=plugin_id,
        )

        self._requests[request.request_id] = request
        logger.info(f"Created {request_type.value} request for user={user_id}")
        return request

    async def process_request(self, request_id: str) -> UserDataRequest:
        """Process a data request."""
        request = self._requests.get(request_id)
        if not request:
            raise ValueError(f"Request not found: {request_id}")

        request.status = "processing"

        try:
            if request.request_type == RequestType.EXPORT:
                result = await self.export_user_data(
                    request.user_id,
                    request.plugin_id,
                )
            elif request.request_type == RequestType.DELETE:
                result = await self.delete_user_data(
                    request.user_id,
                    request.plugin_id,
                )
            else:
                result = {"error": f"Unsupported request type: {request.request_type}"}

            request.result = result
            request.status = "completed"
            request.completed_at = datetime.utcnow()

        except Exception as e:
            request.status = "failed"
            request.result = {"error": str(e)}
            logger.error(f"Request {request_id} failed: {e}")

        return request

    def get_privacy_summary(self) -> Dict[str, Any]:
        """Get summary of privacy settings and data."""
        return {
            "registered_plugins": len(self._declarations),
            "declarations": [d.to_dict() for d in self._declarations.values()],
            "total_consents": sum(len(c) for c in self._consents.values()),
            "pending_requests": sum(1 for r in self._requests.values() if r.status == "pending"),
        }

    # =========================================================================
    # C-1: Persistence Integration
    # =========================================================================

    def save_consent_to_db(self, user_id: str, consent: UserConsent) -> bool:
        """
        Save a consent record to persistent storage.

        Args:
            user_id: User identifier.
            consent: UserConsent to persist.

        Returns:
            True if saved successfully, False otherwise.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import (
                get_phase6_persistence,
            )

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available, consent not saved")
            return False

        rows = persistence.save_consent(
            user_id=user_id,
            plugin_id=consent.plugin_id,
            privacy_level=consent.privacy_level.value,
            categories=[c.value for c in consent.categories],
        )
        return rows > 0

    def load_user_consents_from_db(self, user_id: str) -> int:
        """
        Load all consent records for a user from persistent storage.

        Args:
            user_id: User identifier.

        Returns:
            Number of consents loaded.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import (
                get_phase6_persistence,
            )

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available, consents not loaded")
            return 0

        persisted = persistence.get_user_consents(user_id)

        if not persisted:
            return 0

        if user_id not in self._consents:
            self._consents[user_id] = {}

        count = 0
        for pc in persisted:
            # Map from persistence schema to domain model
            try:
                privacy_level = PrivacyLevel(pc.privacy_level)
            except ValueError:
                privacy_level = PrivacyLevel.MINIMAL

            categories = []
            for cat in pc.categories:
                try:
                    categories.append(DataCategory(cat))
                except ValueError:
                    # GAP-PY-001: Unknown category, skip silently
                    logger.debug(f"Unknown data category '{cat}', skipping")

            self._consents[user_id][pc.plugin_id] = UserConsent(
                user_id=pc.user_id,
                plugin_id=pc.plugin_id,
                privacy_level=privacy_level,
                categories=categories,
                granted_at=pc.consented_at,
                expires_at=None,
                revoked_at=pc.revoked_at,
            )
            count += 1

        logger.info(f"Loaded {count} consents for user {user_id} from database")
        return count

    def revoke_consent_in_db(self, user_id: str, plugin_id: str) -> bool:
        """
        Revoke consent in persistent storage.

        Args:
            user_id: User identifier.
            plugin_id: Plugin identifier.

        Returns:
            True if revoked successfully, False otherwise.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import (
                get_phase6_persistence,
            )

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available")
            return False

        rows = persistence.revoke_consent(user_id, plugin_id)
        return rows > 0

    def save_declaration_to_db(self, declaration: PluginDataDeclaration) -> bool:
        """
        Save a plugin data declaration to persistent storage.

        Args:
            declaration: PluginDataDeclaration to persist.

        Returns:
            True if saved successfully, False otherwise.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import (
                get_phase6_persistence,
            )

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available, declaration not saved")
            return False

        rows = persistence.save_data_declaration(
            plugin_id=declaration.plugin_id,
            categories=[c.value for c in declaration.categories],
            retention_days=declaration.retention_days,
            required_consent_level=declaration.privacy_level.value,
        )
        return rows > 0

    def load_declaration_from_db(self, plugin_id: str) -> bool:
        """
        Load a plugin data declaration from persistent storage.

        Args:
            plugin_id: Plugin identifier.

        Returns:
            True if loaded successfully, False otherwise.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import (
                get_phase6_persistence,
            )

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available, declaration not loaded")
            return False

        pd = persistence.get_data_declaration(plugin_id)
        if not pd:
            return False

        # Map from persistence schema to domain model
        try:
            privacy_level = PrivacyLevel(pd.required_consent_level)
        except ValueError:
            privacy_level = PrivacyLevel.MINIMAL

        categories = []
        for cat in pd.categories:
            try:
                categories.append(DataCategory(cat))
            except ValueError:
                # GAP-PY-001: Unknown category in declaration, skip silently
                logger.debug(f"Unknown data category '{cat}' in plugin declaration, skipping")

        self._declarations[plugin_id] = PluginDataDeclaration(
            plugin_id=pd.plugin_id,
            categories=categories,
            privacy_level=privacy_level,
            retention_days=pd.retention_days,
        )

        logger.info(f"Loaded declaration for plugin {plugin_id} from database")
        return True
