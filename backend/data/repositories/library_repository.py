"""
Library Repository.

Panel Workflow Integration - Library Persistence.
Replaces in-memory _assets and _asset_folders dicts in backend/api/routes/library.py.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

from backend.data.repository_base import (
    BaseEntity,
    BaseRepository,
    ConnectionConfig,
    QueryOptions,
)

logger = logging.getLogger(__name__)


class AssetType(str, Enum):
    """Library asset type enumeration."""

    AUDIO = "audio"
    VOICE_PROFILE = "voice_profile"
    VIDEO = "video"
    IMAGE = "image"
    OTHER = "other"


@dataclass
class LibraryFolderEntity(BaseEntity):
    """
    Library folder entity for persistent storage.

    Maps to library_folders table.
    """

    name: str = ""
    parent_id: str | None = None
    path: str = ""
    modified_at: datetime | None = field(default_factory=datetime.now)


@dataclass
class LibraryAssetEntity(BaseEntity):
    """
    Library asset entity for persistent storage.

    Maps to library_assets table.
    """

    name: str = ""
    type: str = AssetType.AUDIO.value
    path: str = ""
    folder_id: str | None = None
    tags: str = "[]"  # JSON array string
    metadata: str = "{}"  # JSON string
    size: int = 0
    duration: float | None = None
    thumbnail_url: str | None = None
    modified_at: datetime | None = field(default_factory=datetime.now)

    def get_tags(self) -> list[str]:
        """Parse tags JSON."""
        try:
            return json.loads(self.tags) if self.tags else []
        except json.JSONDecodeError:
            return []

    def set_tags(self, tags: list[str]) -> None:
        """Set tags as JSON string."""
        self.tags = json.dumps(tags)

    def get_metadata(self) -> dict[str, Any]:
        """Parse metadata JSON."""
        try:
            return json.loads(self.metadata) if self.metadata else {}
        except json.JSONDecodeError:
            return {}

    def set_metadata(self, data: dict[str, Any]) -> None:
        """Set metadata as JSON string."""
        self.metadata = json.dumps(data)


class LibraryFolderRepository(BaseRepository[LibraryFolderEntity]):
    """
    Repository for library folder persistence.

    Replaces the in-memory _asset_folders dict with database-backed storage.
    """

    def __init__(self, config: ConnectionConfig | None = None):
        super().__init__(
            entity_type=LibraryFolderEntity,
            table_name="library_folders",
            config=config,
        )

    def _entity_to_dict(self, entity: LibraryFolderEntity) -> dict[str, Any]:
        """Convert LibraryFolderEntity to database row dict."""
        return {
            "id": entity.id,
            "name": entity.name,
            "parent_id": entity.parent_id,
            "path": entity.path,
            "created_at": (
                entity.created_at.isoformat()
                if isinstance(entity.created_at, datetime)
                else entity.created_at
            ),
            "modified_at": (
                entity.modified_at.isoformat()
                if isinstance(entity.modified_at, datetime)
                else entity.modified_at
            ),
            "deleted_at": entity.deleted_at.isoformat() if entity.deleted_at else None,
        }

    def _row_to_entity(self, row: dict[str, Any]) -> LibraryFolderEntity:
        """Convert database row to LibraryFolderEntity."""
        return LibraryFolderEntity(
            id=row["id"],
            name=row.get("name", ""),
            parent_id=row.get("parent_id"),
            path=row.get("path", ""),
            created_at=(
                datetime.fromisoformat(row["created_at"])
                if row.get("created_at")
                else datetime.now()
            ),
            updated_at=(
                datetime.fromisoformat(row.get("modified_at", row.get("updated_at", "")))
                if row.get("modified_at") or row.get("updated_at")
                else datetime.now()
            ),
            modified_at=(
                datetime.fromisoformat(row["modified_at"])
                if row.get("modified_at")
                else datetime.now()
            ),
            deleted_at=datetime.fromisoformat(row["deleted_at"]) if row.get("deleted_at") else None,
        )

    async def get_root_folders(self) -> list[LibraryFolderEntity]:
        """Get all root-level folders (no parent)."""
        return await self.find({"parent_id": None})

    async def get_children(self, parent_id: str) -> list[LibraryFolderEntity]:
        """Get child folders of a parent folder."""
        return await self.find({"parent_id": parent_id})

    async def get_by_path(self, path: str) -> LibraryFolderEntity | None:
        """Get folder by path."""
        return await self.find_one({"path": path})


class LibraryAssetRepository(BaseRepository[LibraryAssetEntity]):
    """
    Repository for library asset persistence.

    Replaces the in-memory _assets dict with database-backed storage.
    """

    def __init__(self, config: ConnectionConfig | None = None):
        super().__init__(
            entity_type=LibraryAssetEntity,
            table_name="library_assets",
            config=config,
        )

    def _entity_to_dict(self, entity: LibraryAssetEntity) -> dict[str, Any]:
        """Convert LibraryAssetEntity to database row dict."""
        return {
            "id": entity.id,
            "name": entity.name,
            "type": entity.type,
            "path": entity.path,
            "folder_id": entity.folder_id,
            "tags": entity.tags,
            "metadata": entity.metadata,
            "size": entity.size,
            "duration": entity.duration,
            "thumbnail_url": entity.thumbnail_url,
            "created_at": (
                entity.created_at.isoformat()
                if isinstance(entity.created_at, datetime)
                else entity.created_at
            ),
            "modified_at": (
                entity.modified_at.isoformat()
                if isinstance(entity.modified_at, datetime)
                else entity.modified_at
            ),
            "deleted_at": entity.deleted_at.isoformat() if entity.deleted_at else None,
        }

    def _row_to_entity(self, row: dict[str, Any]) -> LibraryAssetEntity:
        """Convert database row to LibraryAssetEntity."""
        return LibraryAssetEntity(
            id=row["id"],
            name=row.get("name", ""),
            type=row.get("type", AssetType.AUDIO.value),
            path=row.get("path", ""),
            folder_id=row.get("folder_id"),
            tags=row.get("tags", "[]"),
            metadata=row.get("metadata", "{}"),
            size=row.get("size", 0),
            duration=row.get("duration"),
            thumbnail_url=row.get("thumbnail_url"),
            created_at=(
                datetime.fromisoformat(row["created_at"])
                if row.get("created_at")
                else datetime.now()
            ),
            updated_at=(
                datetime.fromisoformat(row.get("modified_at", row.get("updated_at", "")))
                if row.get("modified_at") or row.get("updated_at")
                else datetime.now()
            ),
            modified_at=(
                datetime.fromisoformat(row["modified_at"])
                if row.get("modified_at")
                else datetime.now()
            ),
            deleted_at=datetime.fromisoformat(row["deleted_at"]) if row.get("deleted_at") else None,
        )

    async def get_by_type(
        self,
        asset_type: AssetType,
        options: QueryOptions | None = None,
    ) -> list[LibraryAssetEntity]:
        """Get assets by type."""
        return await self.find({"type": asset_type.value}, options)

    async def get_by_folder(
        self,
        folder_id: str | None,
        options: QueryOptions | None = None,
    ) -> list[LibraryAssetEntity]:
        """Get assets in a specific folder."""
        if folder_id is None:
            # Root-level assets (no folder)
            await self.connect()
            query = f"""
                SELECT * FROM {self.table_name}
                WHERE folder_id IS NULL
                AND deleted_at IS NULL
                ORDER BY modified_at DESC
            """
            async with self._connection.execute(query) as cursor:
                rows = await cursor.fetchall()
                return [self._row_to_entity(dict(row)) for row in rows]
        return await self.find({"folder_id": folder_id}, options)

    async def get_by_path(self, path: str) -> LibraryAssetEntity | None:
        """Get asset by file path."""
        return await self.find_one({"path": path})

    async def search_by_name(
        self,
        query: str,
        limit: int = 50,
    ) -> list[LibraryAssetEntity]:
        """Search assets by name (case-insensitive partial match)."""
        await self.connect()

        sql_query = f"""
            SELECT * FROM {self.table_name}
            WHERE name LIKE ?
            AND deleted_at IS NULL
            ORDER BY modified_at DESC
            LIMIT ?
        """

        async with self._connection.execute(sql_query, (f"%{query}%", limit)) as cursor:
            rows = await cursor.fetchall()
            return [self._row_to_entity(dict(row)) for row in rows]

    async def search_by_tags(
        self,
        tags: list[str],
        limit: int = 50,
    ) -> list[LibraryAssetEntity]:
        """Search assets by tags (any match)."""
        await self.connect()

        # Build OR conditions for each tag
        conditions = " OR ".join(["tags LIKE ?" for _ in tags])
        params = [f'%"{tag}"%' for tag in tags] + [limit]

        query = f"""
            SELECT * FROM {self.table_name}
            WHERE ({conditions})
            AND deleted_at IS NULL
            ORDER BY modified_at DESC
            LIMIT ?
        """

        async with self._connection.execute(query, params) as cursor:
            rows = await cursor.fetchall()
            return [self._row_to_entity(dict(row)) for row in rows]

    async def get_recent(self, limit: int = 20) -> list[LibraryAssetEntity]:
        """Get most recently modified assets."""
        return await self.get_all(
            QueryOptions(limit=limit, order_by="modified_at", order_desc=True)
        )

    async def get_audio_assets(
        self,
        options: QueryOptions | None = None,
    ) -> list[LibraryAssetEntity]:
        """Get all audio assets."""
        return await self.get_by_type(AssetType.AUDIO, options)

    async def get_voice_profiles(
        self,
        options: QueryOptions | None = None,
    ) -> list[LibraryAssetEntity]:
        """Get all voice profile assets."""
        return await self.get_by_type(AssetType.VOICE_PROFILE, options)

    async def update_tags(
        self,
        asset_id: str,
        tags: list[str],
    ) -> LibraryAssetEntity | None:
        """Update asset tags."""
        return await self.update(asset_id, {"tags": json.dumps(tags)})

    async def move_to_folder(
        self,
        asset_id: str,
        folder_id: str | None,
    ) -> LibraryAssetEntity | None:
        """Move asset to a different folder."""
        return await self.update(asset_id, {"folder_id": folder_id})

    async def get_summary(self) -> dict[str, Any]:
        """Get library summary statistics."""
        await self.connect()

        query = f"""
            SELECT
                COUNT(*) as total,
                SUM(CASE WHEN type = 'audio' THEN 1 ELSE 0 END) as audio,
                SUM(CASE WHEN type = 'voice_profile' THEN 1 ELSE 0 END) as voice_profiles,
                SUM(CASE WHEN type = 'video' THEN 1 ELSE 0 END) as video,
                SUM(CASE WHEN type = 'image' THEN 1 ELSE 0 END) as images,
                SUM(size) as total_size
            FROM {self.table_name}
            WHERE deleted_at IS NULL
        """

        async with self._connection.execute(query) as cursor:
            row = await cursor.fetchone()
            if row:
                return {
                    "total": row[0] or 0,
                    "audio": row[1] or 0,
                    "voice_profiles": row[2] or 0,
                    "video": row[3] or 0,
                    "images": row[4] or 0,
                    "total_size": row[5] or 0,
                }

        return {
            "total": 0,
            "audio": 0,
            "voice_profiles": 0,
            "video": 0,
            "images": 0,
            "total_size": 0,
        }


# In-memory repositories for unit tests (explicit injection only).
class InMemoryLibraryAssetRepository:
    """
    In-memory library asset repository for tests.

    This is **not** an automatic production substitute for SQLite.
    """

    def __init__(self):
        self._assets: dict[str, LibraryAssetEntity] = {}
        self._is_fallback = True
        logger.info("Using InMemoryLibraryAssetRepository (test/diagnostic mode)")

    async def get_all(self, options: QueryOptions | None = None) -> list[LibraryAssetEntity]:
        """Get all assets."""
        assets = [a for a in self._assets.values() if a.deleted_at is None]
        if options:
            if options.order_by == "modified_at":
                assets.sort(key=lambda a: a.modified_at or datetime.min, reverse=options.order_desc)
            if options.limit:
                assets = assets[: options.limit]
        return assets

    async def find(
        self, filters: dict[str, Any], options: QueryOptions | None = None
    ) -> list[LibraryAssetEntity]:
        """Find assets matching filters."""
        assets = [
            asset
            for asset in self._assets.values()
            if all(getattr(asset, k, None) == v for k, v in filters.items())
            and asset.deleted_at is None
        ]
        if options and options.limit:
            assets = assets[: options.limit]
        return assets

    async def get_by_id(self, asset_id: str) -> LibraryAssetEntity | None:
        """Get asset by ID."""
        asset = self._assets.get(asset_id)
        return asset if asset and asset.deleted_at is None else None

    async def create(self, entity: LibraryAssetEntity) -> LibraryAssetEntity:
        """Create a new asset."""
        self._assets[entity.id] = entity
        return entity

    async def update(self, asset_id: str, data: dict[str, Any]) -> LibraryAssetEntity | None:
        """Update an asset."""
        if asset_id not in self._assets:
            return None
        asset = self._assets[asset_id]
        for key, value in data.items():
            if hasattr(asset, key):
                setattr(asset, key, value)
        asset.updated_at = datetime.now()
        asset.modified_at = datetime.now()
        return asset

    async def delete(self, asset_id: str, soft: bool = True) -> bool:
        """Delete an asset."""
        if asset_id not in self._assets:
            return False
        if soft:
            self._assets[asset_id].deleted_at = datetime.now()
        else:
            del self._assets[asset_id]
        return True

    async def count(self, filters: dict[str, Any] | None = None) -> int:
        """Count assets."""
        assets = [a for a in self._assets.values() if a.deleted_at is None]
        if filters:
            assets = [
                a for a in assets if all(getattr(a, k, None) == v for k, v in filters.items())
            ]
        return len(assets)

    async def get_by_type(
        self, asset_type: AssetType, options: QueryOptions | None = None
    ) -> list[LibraryAssetEntity]:
        """Get assets by type."""
        return await self.find({"type": asset_type.value}, options)

    async def get_by_folder(
        self, folder_id: str | None, options: QueryOptions | None = None
    ) -> list[LibraryAssetEntity]:
        """Get assets in a folder."""
        return await self.find({"folder_id": folder_id}, options)

    async def get_recent(self, limit: int = 20) -> list[LibraryAssetEntity]:
        """Get recent assets."""
        return await self.get_all(
            QueryOptions(limit=limit, order_by="modified_at", order_desc=True)
        )

    async def get_summary(self) -> dict[str, Any]:
        """Get library summary."""
        assets = [a for a in self._assets.values() if a.deleted_at is None]
        return {
            "total": len(assets),
            "audio": sum(1 for a in assets if a.type == AssetType.AUDIO.value),
            "voice_profiles": sum(1 for a in assets if a.type == AssetType.VOICE_PROFILE.value),
            "video": sum(1 for a in assets if a.type == AssetType.VIDEO.value),
            "images": sum(1 for a in assets if a.type == AssetType.IMAGE.value),
            "total_size": sum(a.size for a in assets),
        }

    async def connect(self) -> None:
        """No-op for in-memory repository."""
        pass

    async def disconnect(self) -> None:
        """No-op for in-memory repository."""
        pass


class InMemoryLibraryFolderRepository:
    """
    In-memory library folder repository for tests.

    This is **not** an automatic production substitute for SQLite.
    """

    def __init__(self):
        self._folders: dict[str, LibraryFolderEntity] = {}
        self._is_fallback = True
        logger.info("Using InMemoryLibraryFolderRepository (test/diagnostic mode)")

    async def get_all(self, options: QueryOptions | None = None) -> list[LibraryFolderEntity]:
        """Get all folders."""
        folders = [f for f in self._folders.values() if f.deleted_at is None]
        if options and options.limit:
            folders = folders[: options.limit]
        return folders

    async def find(
        self, filters: dict[str, Any], options: QueryOptions | None = None
    ) -> list[LibraryFolderEntity]:
        """Find folders matching filters."""
        folders = [
            folder
            for folder in self._folders.values()
            if all(getattr(folder, k, None) == v for k, v in filters.items())
            and folder.deleted_at is None
        ]
        if options and options.limit:
            folders = folders[: options.limit]
        return folders

    async def get_by_id(self, folder_id: str) -> LibraryFolderEntity | None:
        """Get folder by ID."""
        folder = self._folders.get(folder_id)
        return folder if folder and folder.deleted_at is None else None

    async def create(self, entity: LibraryFolderEntity) -> LibraryFolderEntity:
        """Create a new folder."""
        self._folders[entity.id] = entity
        return entity

    async def update(self, folder_id: str, data: dict[str, Any]) -> LibraryFolderEntity | None:
        """Update a folder."""
        if folder_id not in self._folders:
            return None
        folder = self._folders[folder_id]
        for key, value in data.items():
            if hasattr(folder, key):
                setattr(folder, key, value)
        folder.updated_at = datetime.now()
        folder.modified_at = datetime.now()
        return folder

    async def delete(self, folder_id: str, soft: bool = True) -> bool:
        """Delete a folder."""
        if folder_id not in self._folders:
            return False
        if soft:
            self._folders[folder_id].deleted_at = datetime.now()
        else:
            del self._folders[folder_id]
        return True

    async def get_root_folders(self) -> list[LibraryFolderEntity]:
        """Get root folders."""
        return await self.find({"parent_id": None})

    async def get_children(self, parent_id: str) -> list[LibraryFolderEntity]:
        """Get child folders."""
        return await self.find({"parent_id": parent_id})

    async def connect(self) -> None:
        """No-op for in-memory repository."""
        pass

    async def disconnect(self) -> None:
        """No-op for in-memory repository."""
        pass


# Singleton instances for dependency injection
_asset_repository: LibraryAssetRepository | None = None
_folder_repository: LibraryFolderRepository | None = None


def get_library_asset_repository() -> LibraryAssetRepository:
    """
    Get or create LibraryAssetRepository singleton.

    Returns the database-backed repository. Connection is lazy (happens on first query).
    No in-memory fallback - database persistence is REQUIRED for library functionality.
    """
    global _asset_repository

    if _asset_repository is not None:
        return _asset_repository

    _asset_repository = LibraryAssetRepository()
    logger.info("LibraryAssetRepository created (connection will be established on first query)")
    return _asset_repository


def get_library_folder_repository() -> LibraryFolderRepository:
    """
    Get or create LibraryFolderRepository singleton.

    Returns the database-backed repository. Connection is lazy (happens on first query).
    No in-memory fallback - database persistence is REQUIRED for library functionality.
    """
    global _folder_repository

    if _folder_repository is not None:
        return _folder_repository

    _folder_repository = LibraryFolderRepository()
    logger.info("LibraryFolderRepository created (connection will be established on first query)")
    return _folder_repository


def reset_library_repositories() -> None:
    """Reset the repository singletons (for testing)."""
    global _asset_repository, _folder_repository
    _asset_repository = None
    _folder_repository = None
