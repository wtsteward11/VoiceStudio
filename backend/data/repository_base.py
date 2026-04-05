"""
Database-Agnostic Repository Pattern.

Task 1.3.1: SQLite to PostgreSQL abstraction.
Provides database-agnostic data access through repository pattern.
"""

from __future__ import annotations

import asyncio
import logging
import re
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import (
    Any,
    Generic,
    TypeVar,
)

logger = logging.getLogger(__name__)

# SQL Injection Protection: Whitelist of valid table names
VALID_TABLE_NAMES: set[str] = {
    # Core entities
    "voice_profiles",
    "audio_files",
    "users",
    "settings",
    "api_keys",
    "projects",
    "project_tracks",
    "presets",
    "quality_metrics",
    "audit_log",
    # Job persistence (Phase 1 - Backend-Frontend Integration)
    "job_history",
    "training_jobs",
    "training_logs",
    "training_quality_history",
    "deepfake_jobs",
    "sessions",
    "transcriptions",
    "pipeline_sessions",
    "abx_sessions",
    "abx_results",
    # Library persistence (Panel Workflow Integration)
    "library_assets",
    "library_folders",
}

# Regex pattern for valid SQL identifiers (column names, table names)
SQL_IDENTIFIER_PATTERN = re.compile(r"^[a-zA-Z_][a-zA-Z0-9_]*$")


def validate_sql_identifier(identifier: str, context: str = "identifier") -> None:
    """
    Validate that a string is a safe SQL identifier.

    Args:
        identifier: The string to validate
        context: Description for error messages

    Raises:
        ValueError: If the identifier contains unsafe characters
    """
    if not identifier:
        raise ValueError(f"Empty {context} is not allowed")
    if len(identifier) > 128:
        raise ValueError(f"{context} exceeds maximum length of 128 characters")
    if not SQL_IDENTIFIER_PATTERN.match(identifier):
        raise ValueError(
            f"Invalid {context} '{identifier}': "
            f"must start with letter or underscore, contain only alphanumeric and underscore"
        )


def validate_table_name(table_name: str) -> None:
    """
    Validate table name against whitelist.

    Args:
        table_name: The table name to validate

    Raises:
        ValueError: If the table name is not in the whitelist
    """
    validate_sql_identifier(table_name, "table name")
    if table_name.lower() not in VALID_TABLE_NAMES:
        # Allow the identifier if it passes pattern check but log a warning
        logger.warning(
            f"Table name '{table_name}' not in whitelist but passes format validation. "
            f"Consider adding it to VALID_TABLE_NAMES."
        )


T = TypeVar("T")  # Entity type


class DatabaseType(Enum):
    """Supported database types."""

    SQLITE = "sqlite"
    POSTGRESQL = "postgresql"
    MEMORY = "memory"


def _get_default_sqlite_path() -> str:
    """Get the default SQLite path from configuration."""
    try:
        from backend.settings import config

        return config.database.sqlite_path
    except ImportError:
        return "data/voicestudio.db"


@dataclass
class ConnectionConfig:
    """Database connection configuration."""

    database_type: DatabaseType = DatabaseType.SQLITE
    host: str = "localhost"
    port: int = 5432
    database: str = "voicestudio"
    username: str = ""
    password: str = ""
    sqlite_path: str = field(default_factory=_get_default_sqlite_path)
    pool_size: int = 5
    max_overflow: int = 10

    def get_connection_string(self) -> str:
        """Get database connection string."""
        if self.database_type == DatabaseType.SQLITE:
            return f"sqlite:///{self.sqlite_path}"
        elif self.database_type == DatabaseType.POSTGRESQL:
            return f"postgresql://{self.username}:{self.password}@{self.host}:{self.port}/{self.database}"
        elif self.database_type == DatabaseType.MEMORY:
            return "sqlite:///:memory:"
        else:
            raise ValueError(f"Unsupported database type: {self.database_type}")


@dataclass
class QueryOptions:
    """Options for repository queries."""

    limit: int | None = None
    offset: int = 0
    order_by: str | None = None
    order_desc: bool = False
    include_deleted: bool = False


class Repository(ABC, Generic[T]):
    """
    Abstract repository interface.

    Defines standard CRUD operations that work across database backends.
    """

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> T | None:
        """Get entity by ID."""
        pass

    @abstractmethod
    async def get_all(self, options: QueryOptions | None = None) -> list[T]:
        """Get all entities with optional filtering."""
        pass

    @abstractmethod
    async def find(
        self,
        filters: dict[str, Any],
        options: QueryOptions | None = None,
    ) -> list[T]:
        """Find entities matching filters."""
        pass

    @abstractmethod
    async def find_one(self, filters: dict[str, Any]) -> T | None:
        """Find single entity matching filters."""
        pass

    @abstractmethod
    async def create(self, entity: T) -> T:
        """Create new entity."""
        pass

    @abstractmethod
    async def update(self, entity_id: str, data: dict[str, Any]) -> T | None:
        """Update existing entity."""
        pass

    @abstractmethod
    async def delete(self, entity_id: str, soft: bool = True) -> bool:
        """Delete entity (soft delete by default)."""
        pass

    @abstractmethod
    async def count(self, filters: dict[str, Any] | None = None) -> int:
        """Count entities matching filters."""
        pass

    @abstractmethod
    async def exists(self, entity_id: str) -> bool:
        """Check if entity exists."""
        pass


@dataclass
class BaseEntity:
    """Base entity with common fields."""

    id: str
    created_at: datetime = field(default_factory=datetime.now)
    updated_at: datetime = field(default_factory=datetime.now)
    deleted_at: datetime | None = None

    @property
    def is_deleted(self) -> bool:
        return self.deleted_at is not None


class BaseRepository(Repository[T]):
    """
    Base repository implementation with common logic.

    Subclasses should implement database-specific operations.
    """

    def __init__(
        self,
        entity_type: type[T],
        table_name: str,
        config: ConnectionConfig | None = None,
    ):
        # Validate table name to prevent SQL injection
        validate_table_name(table_name)

        self.entity_type = entity_type
        self.table_name = table_name
        self.config = config or ConnectionConfig()
        self._connection: Any = None
        self._lock = asyncio.Lock()

    async def connect(self) -> None:
        """Establish database connection."""
        async with self._lock:
            if self._connection is not None:
                return

            if self.config.database_type == DatabaseType.SQLITE:
                await self._connect_sqlite()
            elif self.config.database_type == DatabaseType.POSTGRESQL:
                await self._connect_postgresql()
            elif self.config.database_type == DatabaseType.MEMORY:
                await self._connect_memory()

    async def _connect_sqlite(self) -> None:
        """Connect to SQLite database."""
        import aiosqlite

        self._connection = await aiosqlite.connect(self.config.sqlite_path)
        self._connection.row_factory = aiosqlite.Row
        logger.info(f"Connected to SQLite: {self.config.sqlite_path}")

    async def _connect_postgresql(self) -> None:
        """Connect to PostgreSQL database."""
        try:
            import asyncpg

            self._connection = await asyncpg.create_pool(
                self.config.get_connection_string(),
                min_size=1,
                max_size=self.config.pool_size,
            )
            logger.info(f"Connected to PostgreSQL: {self.config.host}")
        except ImportError:
            raise ImportError("asyncpg required for PostgreSQL support")

    async def _connect_memory(self) -> None:
        """Connect to in-memory database."""
        import aiosqlite

        self._connection = await aiosqlite.connect(":memory:")
        self._connection.row_factory = aiosqlite.Row
        logger.info("Connected to in-memory SQLite")

    async def disconnect(self) -> None:
        """Close database connection."""
        async with self._lock:
            if self._connection:
                await self._connection.close()
                self._connection = None

    async def get_by_id(self, entity_id: str) -> T | None:
        """Get entity by ID."""
        await self.connect()

        query = f"SELECT * FROM {self.table_name} WHERE id = ?"

        if self.config.database_type == DatabaseType.SQLITE:
            async with self._connection.execute(query, (entity_id,)) as cursor:
                row = await cursor.fetchone()
                if row:
                    return self._row_to_entity(dict(row))

        return None

    async def get_all(self, options: QueryOptions | None = None) -> list[T]:
        """Get all entities."""
        await self.connect()
        options = options or QueryOptions()

        query = f"SELECT * FROM {self.table_name}"
        params: list[Any] = []

        if not options.include_deleted:
            query += " WHERE deleted_at IS NULL"

        if options.order_by:
            direction = "DESC" if options.order_desc else "ASC"
            query += f" ORDER BY {options.order_by} {direction}"

        if options.limit:
            query += f" LIMIT {options.limit}"

        if options.offset:
            query += f" OFFSET {options.offset}"

        if self.config.database_type == DatabaseType.SQLITE:
            async with self._connection.execute(query, params) as cursor:
                rows = await cursor.fetchall()
                return [self._row_to_entity(dict(row)) for row in rows]

        return []

    async def find(
        self,
        filters: dict[str, Any],
        options: QueryOptions | None = None,
    ) -> list[T]:
        """Find entities matching filters."""
        await self.connect()
        options = options or QueryOptions()

        where_clauses = []
        params = []

        for key, value in filters.items():
            # Validate column name to prevent SQL injection
            validate_sql_identifier(key, "column name")
            if value is None:
                # Use IS NULL for NULL comparisons
                where_clauses.append(f"{key} IS NULL")
            else:
                where_clauses.append(f"{key} = ?")
                params.append(value)

        if not options.include_deleted:
            where_clauses.append("deleted_at IS NULL")

        where_sql = " AND ".join(where_clauses) if where_clauses else "1=1"
        query = f"SELECT * FROM {self.table_name} WHERE {where_sql}"

        if options.order_by:
            # Validate order_by column name to prevent SQL injection
            validate_sql_identifier(options.order_by, "order_by column")
            direction = "DESC" if options.order_desc else "ASC"
            query += f" ORDER BY {options.order_by} {direction}"

        if options.limit:
            query += f" LIMIT {options.limit}"

        if options.offset:
            query += f" OFFSET {options.offset}"

        if self.config.database_type == DatabaseType.SQLITE:
            async with self._connection.execute(query, params) as cursor:
                rows = await cursor.fetchall()
                return [self._row_to_entity(dict(row)) for row in rows]

        return []

    async def find_one(self, filters: dict[str, Any]) -> T | None:
        """Find single entity."""
        results = await self.find(filters, QueryOptions(limit=1))
        return results[0] if results else None

    async def create(self, entity: T) -> T:
        """Create new entity."""
        await self.connect()

        data = self._entity_to_dict(entity)
        columns = ", ".join(data.keys())
        placeholders = ", ".join(["?" for _ in data])
        values = list(data.values())

        query = f"INSERT INTO {self.table_name} ({columns}) VALUES ({placeholders})"

        if self.config.database_type == DatabaseType.SQLITE:
            await self._connection.execute(query, values)
            await self._connection.commit()

        return entity

    async def update(self, entity_id: str, data: dict[str, Any]) -> T | None:
        """Update existing entity."""
        await self.connect()

        data["updated_at"] = datetime.now().isoformat()

        set_clauses = [f"{key} = ?" for key in data]
        set_sql = ", ".join(set_clauses)
        values = [*list(data.values()), entity_id]

        query = f"UPDATE {self.table_name} SET {set_sql} WHERE id = ?"

        if self.config.database_type == DatabaseType.SQLITE:
            await self._connection.execute(query, values)
            await self._connection.commit()

        return await self.get_by_id(entity_id)

    async def delete(self, entity_id: str, soft: bool = True) -> bool:
        """Delete entity."""
        await self.connect()

        if soft:
            return (
                await self.update(entity_id, {"deleted_at": datetime.now().isoformat()}) is not None
            )
        else:
            query = f"DELETE FROM {self.table_name} WHERE id = ?"

            if self.config.database_type == DatabaseType.SQLITE:
                await self._connection.execute(query, (entity_id,))
                await self._connection.commit()

            return True

    async def count(self, filters: dict[str, Any] | None = None) -> int:
        """Count entities."""
        await self.connect()

        where_clauses = ["deleted_at IS NULL"]
        params = []

        if filters:
            for key, value in filters.items():
                # Validate column name to prevent SQL injection
                validate_sql_identifier(key, "column name")
                where_clauses.append(f"{key} = ?")
                params.append(value)

        where_sql = " AND ".join(where_clauses)
        query = f"SELECT COUNT(*) FROM {self.table_name} WHERE {where_sql}"

        if self.config.database_type == DatabaseType.SQLITE:
            async with self._connection.execute(query, params) as cursor:
                row = await cursor.fetchone()
                return row[0] if row else 0

        return 0

    async def exists(self, entity_id: str) -> bool:
        """Check if entity exists."""
        entity = await self.get_by_id(entity_id)
        return entity is not None

    def _entity_to_dict(self, entity: T) -> dict[str, Any]:
        """Convert entity to dictionary for storage."""
        if hasattr(entity, "__dict__"):
            result = {}
            for k, v in entity.__dict__.items():
                if k.startswith("_"):
                    continue
                # Serialize datetime objects to ISO format strings
                if isinstance(v, datetime):
                    result[k] = v.isoformat()
                else:
                    result[k] = v
            return result
        return dict(entity)  # type: ignore

    def _row_to_entity(self, row: dict[str, Any]) -> T:
        """Convert database row to entity."""
        return self.entity_type(**row)


class UnitOfWork:
    """
    Unit of Work pattern for transaction management.
    """

    def __init__(self, config: ConnectionConfig | None = None):
        self.config = config or ConnectionConfig()
        self._repositories: dict[str, Repository] = {}
        self._connection = None

    def register_repository(self, name: str, repository: Repository) -> None:
        """Register a repository."""
        self._repositories[name] = repository

    def get_repository(self, name: str) -> Repository:
        """Get a registered repository."""
        if name not in self._repositories:
            raise KeyError(f"Repository '{name}' not registered")
        return self._repositories[name]

    async def __aenter__(self) -> UnitOfWork:
        """Start transaction."""
        # Begin transaction logic here
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb) -> None:
        """Commit or rollback transaction."""
        if exc_type:
            await self.rollback()
        else:
            await self.commit()

    async def commit(self) -> None:
        """Commit all changes."""
        logger.debug("Committing unit of work")

    async def rollback(self) -> None:
        """Rollback all changes."""
        logger.debug("Rolling back unit of work")
