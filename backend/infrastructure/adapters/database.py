"""
Database Adapter.

Task 3.2.4: Adapter for database operations.
Gap Analysis Fix: Implemented actual aiosqlite connection.
"""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

from backend.infrastructure.adapters.base import Adapter

logger = logging.getLogger(__name__)


class DatabaseAdapter(Adapter):
    """
    Adapter for database operations.

    Supports SQLite (default) and PostgreSQL.
    Provides async connection pooling.
    """

    def __init__(
        self,
        connection_string: str | None = None,
        pool_size: int = 5,
    ):
        """
        Initialize database adapter.

        Args:
            connection_string: Database connection string
            pool_size: Connection pool size
        """
        super().__init__("Database")

        self._connection_string = connection_string or "sqlite:///data/voicestudio.db"
        self._pool_size = pool_size
        self._pool: Any | None = None
        self._connection: Any | None = None  # For SQLite
        self._db_type: str = "unknown"

    async def connect(self) -> bool:
        """Connect to the database."""
        try:
            if self._connection_string.startswith("sqlite"):
                await self._connect_sqlite()
                self._db_type = "sqlite"
            else:
                await self._connect_postgres()
                self._db_type = "postgres"

            self._connected = True
            self._logger.info(f"Connected to {self._db_type} database")
            return True

        except Exception as e:
            self._logger.error(f"Database connection failed: {e}")
            return False

    async def _connect_sqlite(self) -> None:
        """Connect to SQLite database using aiosqlite."""
        # Extract path from connection string
        db_path = self._connection_string.replace("sqlite:///", "")
        Path(db_path).parent.mkdir(parents=True, exist_ok=True)

        try:
            import aiosqlite

            self._connection = await aiosqlite.connect(db_path)
            # Enable dict-like row access
            self._connection.row_factory = aiosqlite.Row
            self._pool = self._connection  # For compatibility

            self._logger.info(f"SQLite connection established: {db_path}")

        except ImportError:
            self._logger.warning(
                "aiosqlite not installed. Using placeholder mode. "
                "Install with: pip install aiosqlite"
            )
            # Fallback to placeholder
            self._pool = {"type": "sqlite", "path": db_path, "placeholder": True}

    async def _connect_postgres(self) -> None:
        """Connect to PostgreSQL database using asyncpg."""
        try:
            import asyncpg

            self._pool = await asyncpg.create_pool(
                self._connection_string,
                min_size=1,
                max_size=self._pool_size,
            )

            self._logger.info(f"PostgreSQL pool created (size: {self._pool_size})")

        except ImportError:
            self._logger.warning(
                "asyncpg not installed. Using placeholder mode. "
                "Install with: pip install asyncpg"
            )
            # Fallback to placeholder
            self._pool = {"type": "postgres", "pool_size": self._pool_size, "placeholder": True}

    async def disconnect(self) -> bool:
        """Disconnect from the database."""
        try:
            if self._connection is not None:
                # SQLite
                await self._connection.close()
                self._connection = None
            elif self._pool is not None and hasattr(self._pool, "close"):
                # PostgreSQL
                await self._pool.close()

            self._pool = None
            self._connected = False
            self._logger.info("Disconnected from database")
            return True

        except Exception as e:
            self._logger.error(f"Disconnect error: {e}")
            self._connected = False
            return False

    async def health_check(self) -> dict[str, Any]:
        """Check database health."""
        # Check if running in placeholder mode
        is_placeholder = isinstance(self._pool, dict) and self._pool.get("placeholder", False)

        status = {
            "connected": self._connected,
            "type": self._db_type,
            "pool_size": self._pool_size,
            "placeholder_mode": is_placeholder,
        }

        if is_placeholder:
            status["warning"] = (
                f"Database adapter running in placeholder mode. "
                f"Install {'aiosqlite' if self._db_type == 'sqlite' else 'asyncpg'} "
                f"for full database support."
            )
            return status

        # Try a simple query to verify connection
        if self._connected:
            try:
                if self._db_type == "sqlite" and self._connection:
                    async with self._connection.execute("SELECT 1") as cursor:
                        await cursor.fetchone()
                    status["ping"] = "ok"
                elif self._db_type == "postgres" and self._pool:
                    async with self._pool.acquire() as conn:
                        await conn.fetchval("SELECT 1")
                    status["ping"] = "ok"
            except Exception as e:
                status["ping"] = f"error: {e}"

        return status

    async def execute(
        self,
        query: str,
        params: tuple | None = None,
    ) -> int:
        """
        Execute a query.

        Args:
            query: SQL query
            params: Query parameters

        Returns:
            Number of affected rows
        """
        if not self._connected:
            raise RuntimeError("Database not connected")

        self._logger.debug(f"Execute: {query[:100]}...")

        try:
            if self._db_type == "sqlite" and self._connection:
                async with self._connection.execute(query, params or ()) as cursor:
                    await self._connection.commit()
                    return int(cursor.rowcount)

            elif self._db_type == "postgres" and self._pool:
                async with self._pool.acquire() as conn:
                    result = await conn.execute(query, *(params or ()))
                    # asyncpg returns "UPDATE 5" or similar
                    return int(result.split()[-1]) if result else 0

        except Exception as e:
            self._logger.error(f"Execute error: {e}")
            raise

        return 0

    async def fetch_one(
        self,
        query: str,
        params: tuple | None = None,
    ) -> dict[str, Any] | None:
        """
        Fetch a single row.

        Args:
            query: SQL query
            params: Query parameters

        Returns:
            Row as dict or None
        """
        if not self._connected:
            raise RuntimeError("Database not connected")

        self._logger.debug(f"Fetch one: {query[:100]}...")

        try:
            if self._db_type == "sqlite" and self._connection:
                async with self._connection.execute(query, params or ()) as cursor:
                    row = await cursor.fetchone()
                    if row:
                        return dict(row)
                    return None

            elif self._db_type == "postgres" and self._pool:
                async with self._pool.acquire() as conn:
                    row = await conn.fetchrow(query, *(params or ()))
                    if row:
                        return dict(row)
                    return None

        except Exception as e:
            self._logger.error(f"Fetch one error: {e}")
            raise

        return None

    async def fetch_all(
        self,
        query: str,
        params: tuple | None = None,
    ) -> list[dict[str, Any]]:
        """
        Fetch all rows.

        Args:
            query: SQL query
            params: Query parameters

        Returns:
            List of rows as dicts
        """
        if not self._connected:
            raise RuntimeError("Database not connected")

        self._logger.debug(f"Fetch all: {query[:100]}...")

        try:
            if self._db_type == "sqlite" and self._connection:
                async with self._connection.execute(query, params or ()) as cursor:
                    rows = await cursor.fetchall()
                    return [dict(row) for row in rows]

            elif self._db_type == "postgres" and self._pool:
                async with self._pool.acquire() as conn:
                    rows = await conn.fetch(query, *(params or ()))
                    return [dict(row) for row in rows]

        except Exception as e:
            self._logger.error(f"Fetch all error: {e}")
            raise

        return []

    @asynccontextmanager
    async def transaction(self):
        """
        Get a transaction context.

        Usage:
            async with adapter.transaction():
                await adapter.execute("INSERT ...")
                await adapter.execute("UPDATE ...")
        """
        if not self._connected:
            raise RuntimeError("Database not connected")

        if self._db_type == "sqlite" and self._connection:
            # SQLite: use BEGIN/COMMIT
            await self._connection.execute("BEGIN")
            try:
                yield self
                await self._connection.commit()
            except Exception:
                await self._connection.rollback()
                raise

        elif self._db_type == "postgres" and self._pool:
            # PostgreSQL: use connection transaction
            async with self._pool.acquire() as conn, conn.transaction():
                yield self
        else:
            # Placeholder mode
            yield self

    async def executemany(
        self,
        query: str,
        params_list: list[tuple],
    ) -> int:
        """
        Execute a query with multiple parameter sets.

        Args:
            query: SQL query
            params_list: List of parameter tuples

        Returns:
            Total number of affected rows
        """
        if not self._connected:
            raise RuntimeError("Database not connected")

        total = 0

        try:
            if self._db_type == "sqlite" and self._connection:
                await self._connection.executemany(query, params_list)
                await self._connection.commit()
                total = len(params_list)

            elif self._db_type == "postgres" and self._pool:
                async with self._pool.acquire() as conn:
                    await conn.executemany(query, params_list)
                    total = len(params_list)

        except Exception as e:
            self._logger.error(f"Execute many error: {e}")
            raise

        return total


# Singleton instance
_database_adapter: DatabaseAdapter | None = None


def get_database_adapter(
    connection_string: str | None = None,
    pool_size: int = 5,
) -> DatabaseAdapter:
    """Get or create the database adapter singleton."""
    global _database_adapter

    if _database_adapter is None:
        _database_adapter = DatabaseAdapter(
            connection_string=connection_string,
            pool_size=pool_size,
        )

    return _database_adapter


async def close_database_adapter() -> None:
    """Close the database adapter."""
    global _database_adapter

    if _database_adapter is not None:
        await _database_adapter.disconnect()
        _database_adapter = None


def reset_database_adapter_singleton() -> None:
    """
    Drop the adapter singleton without awaiting disconnect.

    Intended for tests that will replace the database path; prefer
    close_database_adapter() when an event loop is available.
    """
    global _database_adapter
    _database_adapter = None
