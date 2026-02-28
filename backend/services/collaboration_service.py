"""
Collaboration Service

Phase 14: Collaboration Features
Real-time collaboration and project sharing capabilities.

Features:
- Real-time project sync (14.1)
- Project export/import (14.2)
- Sharing and permissions (14.3)
"""

from __future__ import annotations

import hashlib
import json
import logging
import uuid
import zipfile
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class SyncEventType(Enum):
    """Real-time sync event types."""

    PROJECT_UPDATED = "project_updated"
    TRACK_ADDED = "track_added"
    TRACK_UPDATED = "track_updated"
    TRACK_DELETED = "track_deleted"
    VOICE_CHANGED = "voice_changed"
    TEXT_CHANGED = "text_changed"
    SETTINGS_CHANGED = "settings_changed"
    USER_JOINED = "user_joined"
    USER_LEFT = "user_left"
    CURSOR_MOVED = "cursor_moved"


class PermissionLevel(Enum):
    """Permission levels for shared projects."""

    VIEW = "view"  # Read-only access
    COMMENT = "comment"  # Can add comments
    EDIT = "edit"  # Can edit project
    ADMIN = "admin"  # Full control


class ExportFormat(Enum):
    """Project export formats."""

    VOICESTUDIO = "vstudio"  # Native format
    ZIP = "zip"  # ZIP archive with assets
    JSON = "json"  # JSON-only (no audio)
    AUDACITY = "aup"  # Audacity project


@dataclass
class SyncEvent:
    """Real-time synchronization event."""

    event_id: str
    event_type: SyncEventType
    project_id: str
    user_id: str
    timestamp: datetime
    data: dict[str, Any]
    version: int

    def to_dict(self) -> dict[str, Any]:
        return {
            "event_id": self.event_id,
            "event_type": self.event_type.value,
            "project_id": self.project_id,
            "user_id": self.user_id,
            "timestamp": self.timestamp.isoformat(),
            "data": self.data,
            "version": self.version,
        }


@dataclass
class Collaborator:
    """Project collaborator."""

    user_id: str
    display_name: str
    permission: PermissionLevel
    joined_at: datetime
    is_online: bool = False
    cursor_position: dict[str, Any] | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "user_id": self.user_id,
            "display_name": self.display_name,
            "permission": self.permission.value,
            "joined_at": self.joined_at.isoformat(),
            "is_online": self.is_online,
            "cursor_position": self.cursor_position,
        }


@dataclass
class SharedProject:
    """Shared project with collaboration state."""

    project_id: str
    name: str
    owner_id: str
    collaborators: dict[str, Collaborator]
    version: int
    created_at: datetime
    last_sync_at: datetime
    is_public: bool = False
    share_link: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "project_id": self.project_id,
            "name": self.name,
            "owner_id": self.owner_id,
            "collaborators": {k: v.to_dict() for k, v in self.collaborators.items()},
            "version": self.version,
            "created_at": self.created_at.isoformat(),
            "last_sync_at": self.last_sync_at.isoformat(),
            "is_public": self.is_public,
            "share_link": self.share_link,
        }


@dataclass
class ExportResult:
    """Result of project export."""

    export_id: str
    project_id: str
    format: ExportFormat
    file_path: str
    file_size_bytes: int
    created_at: datetime
    expires_at: datetime | None
    download_url: str | None

    def to_dict(self) -> dict[str, Any]:
        return {
            "export_id": self.export_id,
            "project_id": self.project_id,
            "format": self.format.value,
            "file_path": self.file_path,
            "file_size_bytes": self.file_size_bytes,
            "created_at": self.created_at.isoformat(),
            "expires_at": self.expires_at.isoformat() if self.expires_at else None,
            "download_url": self.download_url,
        }


# Event callback type
EventCallback = Callable[[SyncEvent], None]


class CollaborationService:
    """
    Collaboration service for real-time sync and sharing.

    Phase 14: Collaboration Features

    Features:
    - Real-time project synchronization
    - Multi-user editing support
    - Project export/import
    - Sharing with permissions
    """

    def __init__(self, exports_dir: Path | None = None):
        self._exports_dir = exports_dir or Path("exports")
        self._shared_projects: dict[str, SharedProject] = {}
        self._event_history: dict[str, list[SyncEvent]] = {}
        self._subscribers: dict[str, set[EventCallback]] = {}
        self._active_connections: dict[str, set[str]] = {}  # project_id -> user_ids
        self._initialized = False

        logger.info("CollaborationService created")

    async def initialize(self) -> bool:
        """Initialize the collaboration service."""
        if self._initialized:
            return True

        try:
            self._exports_dir.mkdir(parents=True, exist_ok=True)
            self._initialized = True
            logger.info("CollaborationService initialized")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize CollaborationService: {e}")
            return False

    # ===== Phase 14.1: Real-time Sync =====

    async def create_shared_project(
        self,
        project_id: str,
        name: str,
        owner_id: str,
        owner_name: str,
    ) -> SharedProject:
        """Create a new shared project."""
        now = datetime.now()

        project = SharedProject(
            project_id=project_id,
            name=name,
            owner_id=owner_id,
            collaborators={
                owner_id: Collaborator(
                    user_id=owner_id,
                    display_name=owner_name,
                    permission=PermissionLevel.ADMIN,
                    joined_at=now,
                    is_online=True,
                )
            },
            version=1,
            created_at=now,
            last_sync_at=now,
        )

        self._shared_projects[project_id] = project
        self._event_history[project_id] = []
        self._active_connections[project_id] = {owner_id}

        logger.info(f"Created shared project: {project_id}")
        return project

    async def join_project(
        self,
        project_id: str,
        user_id: str,
        display_name: str,
    ) -> SharedProject | None:
        """Join a shared project."""
        project = self._shared_projects.get(project_id)
        if not project:
            return None

        # Add or update collaborator
        if user_id not in project.collaborators:
            project.collaborators[user_id] = Collaborator(
                user_id=user_id,
                display_name=display_name,
                permission=PermissionLevel.VIEW,
                joined_at=datetime.now(),
            )

        project.collaborators[user_id].is_online = True
        self._active_connections.setdefault(project_id, set()).add(user_id)

        # Broadcast user joined event
        await self._broadcast_event(
            project_id,
            SyncEventType.USER_JOINED,
            user_id,
            {"user_id": user_id, "display_name": display_name},
        )

        logger.info(f"User {user_id} joined project {project_id}")
        return project

    async def leave_project(self, project_id: str, user_id: str):
        """Leave a shared project."""
        project = self._shared_projects.get(project_id)
        if not project:
            return

        if user_id in project.collaborators:
            project.collaborators[user_id].is_online = False

        if project_id in self._active_connections:
            self._active_connections[project_id].discard(user_id)

        # Broadcast user left event
        await self._broadcast_event(
            project_id,
            SyncEventType.USER_LEFT,
            user_id,
            {"user_id": user_id},
        )

        logger.info(f"User {user_id} left project {project_id}")

    async def sync_change(
        self,
        project_id: str,
        user_id: str,
        event_type: SyncEventType,
        data: dict[str, Any],
    ) -> SyncEvent | None:
        """Sync a change to all collaborators."""
        project = self._shared_projects.get(project_id)
        if not project:
            return None

        # Check permissions
        collaborator = project.collaborators.get(user_id)
        if not collaborator or collaborator.permission == PermissionLevel.VIEW:
            logger.warning(f"User {user_id} lacks permission to sync changes")
            return None

        # Create event
        project.version += 1
        event = await self._broadcast_event(project_id, event_type, user_id, data)

        project.last_sync_at = datetime.now()

        return event

    async def update_cursor(
        self,
        project_id: str,
        user_id: str,
        cursor_position: dict[str, Any],
    ):
        """Update user cursor position for collaborative editing."""
        project = self._shared_projects.get(project_id)
        if not project or user_id not in project.collaborators:
            return

        project.collaborators[user_id].cursor_position = cursor_position

        # Broadcast cursor update
        await self._broadcast_event(
            project_id,
            SyncEventType.CURSOR_MOVED,
            user_id,
            {"user_id": user_id, "cursor": cursor_position},
        )

    def subscribe(self, project_id: str, callback: EventCallback):
        """Subscribe to project events."""
        self._subscribers.setdefault(project_id, set()).add(callback)

    def unsubscribe(self, project_id: str, callback: EventCallback):
        """Unsubscribe from project events."""
        if project_id in self._subscribers:
            self._subscribers[project_id].discard(callback)

    async def _broadcast_event(
        self,
        project_id: str,
        event_type: SyncEventType,
        user_id: str,
        data: dict[str, Any],
    ) -> SyncEvent:
        """Broadcast an event to all subscribers."""
        project = self._shared_projects.get(project_id)
        version = project.version if project else 1

        event = SyncEvent(
            event_id=f"evt_{uuid.uuid4().hex[:8]}",
            event_type=event_type,
            project_id=project_id,
            user_id=user_id,
            timestamp=datetime.now(),
            data=data,
            version=version,
        )

        # Store in history
        self._event_history.setdefault(project_id, []).append(event)

        # Notify subscribers
        callbacks = self._subscribers.get(project_id, set())
        for callback in callbacks:
            try:
                callback(event)
            except Exception as e:
                logger.warning(f"Event callback error: {e}")

        return event

    def get_event_history(
        self,
        project_id: str,
        since_version: int = 0,
    ) -> list[SyncEvent]:
        """Get event history since a version."""
        events = self._event_history.get(project_id, [])
        return [e for e in events if e.version > since_version]

    # ===== Phase 14.2: Export/Import =====

    async def export_project(
        self,
        project_id: str,
        project_data: dict[str, Any],
        audio_files: list[Path],
        format: ExportFormat = ExportFormat.VOICESTUDIO,
    ) -> ExportResult:
        """
        Export a project to a file.

        Args:
            project_id: Project ID
            project_data: Project JSON data
            audio_files: List of audio file paths to include
            format: Export format

        Returns:
            ExportResult with file path
        """
        export_id = f"exp_{uuid.uuid4().hex[:8]}"
        now = datetime.now()
        timestamp = now.strftime("%Y%m%d_%H%M%S")

        project_name = project_data.get("name", "project")
        safe_name = "".join(c if c.isalnum() else "_" for c in project_name)

        if format == ExportFormat.VOICESTUDIO:
            output_path = self._exports_dir / f"{safe_name}_{timestamp}.vstudio"
            await self._export_vstudio(output_path, project_data, audio_files)

        elif format == ExportFormat.ZIP:
            output_path = self._exports_dir / f"{safe_name}_{timestamp}.zip"
            await self._export_zip(output_path, project_data, audio_files)

        elif format == ExportFormat.JSON:
            output_path = self._exports_dir / f"{safe_name}_{timestamp}.json"
            await self._export_json(output_path, project_data)

        else:
            raise ValueError(f"Unsupported export format: {format}")

        file_size = output_path.stat().st_size

        result = ExportResult(
            export_id=export_id,
            project_id=project_id,
            format=format,
            file_path=str(output_path),
            file_size_bytes=file_size,
            created_at=now,
            expires_at=None,
            download_url=None,
        )

        logger.info(f"Exported project {project_id} to {output_path}")
        return result

    async def _export_vstudio(
        self,
        output_path: Path,
        project_data: dict[str, Any],
        audio_files: list[Path],
    ):
        """Export to VoiceStudio format (ZIP with manifest)."""
        with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as zf:
            # Add manifest
            manifest = {
                "version": "1.0",
                "format": "voicestudio",
                "created_at": datetime.now().isoformat(),
            }
            zf.writestr("manifest.json", json.dumps(manifest, indent=2))

            # Add project data
            zf.writestr("project.json", json.dumps(project_data, indent=2))

            # Add audio files
            for audio_path in audio_files:
                if audio_path.exists():
                    zf.write(audio_path, f"audio/{audio_path.name}")

    async def _export_zip(
        self,
        output_path: Path,
        project_data: dict[str, Any],
        audio_files: list[Path],
    ):
        """Export to standard ZIP archive."""
        with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as zf:
            # Add project data
            zf.writestr("project.json", json.dumps(project_data, indent=2))

            # Add audio files
            for audio_path in audio_files:
                if audio_path.exists():
                    zf.write(audio_path, audio_path.name)

    async def _export_json(
        self,
        output_path: Path,
        project_data: dict[str, Any],
    ):
        """Export to JSON only."""
        with open(output_path, "w") as f:
            json.dump(project_data, f, indent=2)

    async def import_project(
        self,
        file_path: Path,
    ) -> dict[str, Any]:
        """
        Import a project from a file.

        Args:
            file_path: Path to import file

        Returns:
            Project data dictionary
        """
        suffix = file_path.suffix.lower()

        if suffix == ".vstudio" or suffix == ".zip":
            return await self._import_archive(file_path)
        elif suffix == ".json":
            return await self._import_json(file_path)
        else:
            raise ValueError(f"Unsupported import format: {suffix}")

    async def _import_archive(self, file_path: Path) -> dict[str, Any]:
        """Import from archive format."""
        with zipfile.ZipFile(file_path, "r") as zf:
            # Read project data
            project_json = zf.read("project.json")
            project_data = json.loads(project_json)

            # Extract audio files to temp directory
            # (In production, extract to proper location)

            return project_data

    async def _import_json(self, file_path: Path) -> dict[str, Any]:
        """Import from JSON."""
        with open(file_path) as f:
            return json.load(f)

    # ===== Phase 14.3: Sharing =====

    async def create_share_link(
        self,
        project_id: str,
        permission: PermissionLevel = PermissionLevel.VIEW,
        expires_hours: int | None = None,
    ) -> str:
        """Create a shareable link for a project."""
        project = self._shared_projects.get(project_id)
        if not project:
            raise ValueError(f"Project not found: {project_id}")

        # Generate unique share token
        token = hashlib.sha256(f"{project_id}:{uuid.uuid4()}".encode()).hexdigest()[:16]

        share_link = f"voicestudio://share/{token}"
        project.share_link = share_link

        logger.info(f"Created share link for project {project_id}")
        return share_link

    async def set_permission(
        self,
        project_id: str,
        user_id: str,
        permission: PermissionLevel,
        granter_id: str,
    ) -> bool:
        """Set permission level for a collaborator."""
        project = self._shared_projects.get(project_id)
        if not project:
            return False

        # Check granter is admin
        granter = project.collaborators.get(granter_id)
        if not granter or granter.permission != PermissionLevel.ADMIN:
            return False

        # Update permission
        if user_id in project.collaborators:
            project.collaborators[user_id].permission = permission
            logger.info(f"Set {user_id} permission to {permission.value} on {project_id}")
            return True

        return False

    async def remove_collaborator(
        self,
        project_id: str,
        user_id: str,
        remover_id: str,
    ) -> bool:
        """Remove a collaborator from a project."""
        project = self._shared_projects.get(project_id)
        if not project:
            return False

        # Check remover is admin
        remover = project.collaborators.get(remover_id)
        if not remover or remover.permission != PermissionLevel.ADMIN:
            return False

        # Cannot remove owner
        if user_id == project.owner_id:
            return False

        if user_id in project.collaborators:
            del project.collaborators[user_id]
            logger.info(f"Removed {user_id} from project {project_id}")
            return True

        return False

    def get_project(self, project_id: str) -> SharedProject | None:
        """Get shared project by ID."""
        return self._shared_projects.get(project_id)

    def get_online_collaborators(self, project_id: str) -> list[Collaborator]:
        """Get online collaborators for a project."""
        project = self._shared_projects.get(project_id)
        if not project:
            return []

        return [c for c in project.collaborators.values() if c.is_online]


# Singleton instance
_collaboration_service: CollaborationService | None = None


def get_collaboration_service() -> CollaborationService:
    """Get or create the collaboration service singleton."""
    global _collaboration_service
    if _collaboration_service is None:
        _collaboration_service = CollaborationService()
    return _collaboration_service
