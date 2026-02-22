"""
Database Query Optimization

Provides:
- Connection pooling
- Query caching
- Query optimization
- Index management
- Query monitoring
"""

from __future__ import annotations

import logging
import re
import sqlite3
import threading
import time
from collections import deque
from contextlib import contextmanager, suppress
from dataclasses import dataclass
from datetime import datetime
from typing import Any

logger = logging.getLogger(__name__)

# Try importing SQLAlchemy for advanced features
try:
    from sqlalchemy import create_engine
    from sqlalchemy.pool import StaticPool

    HAS_SQLALCHEMY = True
except ImportError:
    HAS_SQLALCHEMY = False
    logger.debug("SQLAlchemy not available. Advanced features will be limited.")


@dataclass
class QueryStats:
    """Statistics for a query."""

    query: str
    execution_count: int = 0
    total_time: float = 0.0
    min_time: float = float("inf")
    max_time: float = 0.0
    average_time: float = 0.0
    last_execution: datetime | None = None
    cache_hits: int = 0
    cache_misses: int = 0

    def record_execution(self, execution_time: float, cached: bool = False):
        """Record query execution."""
        self.execution_count += 1
        self.total_time += execution_time
        self.min_time = min(self.min_time, execution_time)
        self.max_time = max(self.max_time, execution_time)
        self.average_time = self.total_time / self.execution_count
        self.last_execution = datetime.now()

        if cached:
            self.cache_hits += 1
        else:
            self.cache_misses += 1


class QueryCache:
    """LRU cache for query results."""

    def __init__(self, max_size: int = 100, ttl_seconds: float = 300.0):
        """
        Initialize query cache.

        Args:
            max_size: Maximum number of cached queries
            ttl_seconds: Time-to-live for cached results (seconds)
        """
        self.max_size = max_size
        self.ttl_seconds = ttl_seconds
        self.cache: dict[str, tuple[Any, float]] = {}
        self.access_order: deque = deque()
        self.lock = threading.Lock()

    def get(self, cache_key: str) -> Any | None:
        """
        Get cached result.

        Args:
            cache_key: Cache key

        Returns:
            Cached result or None
        """
        with self.lock:
            if cache_key not in self.cache:
                return None

            result, timestamp = self.cache[cache_key]

            # Check TTL
            if time.time() - timestamp > self.ttl_seconds:
                del self.cache[cache_key]
                if cache_key in self.access_order:
                    self.access_order.remove(cache_key)
                return None

            # Update access order (move to end)
            if cache_key in self.access_order:
                self.access_order.remove(cache_key)
            self.access_order.append(cache_key)

            return result

    def set(self, cache_key: str, value: Any):
        """
        Cache a result.

        Args:
            cache_key: Cache key
            value: Value to cache
        """
        with self.lock:
            # Remove oldest if at capacity
            if len(self.cache) >= self.max_size and self.access_order:
                oldest_key = self.access_order.popleft()
                if oldest_key in self.cache:
                    del self.cache[oldest_key]

            # Add to cache
            self.cache[cache_key] = (value, time.time())
            if cache_key not in self.access_order:
                self.access_order.append(cache_key)

    def clear(self):
        """Clear cache."""
        with self.lock:
            self.cache.clear()
            self.access_order.clear()

    def invalidate(self, pattern: str | None = None):
        """
        Invalidate cache entries.

        Args:
            pattern: Optional pattern to match (if None, clears all)
        """
        with self.lock:
            if pattern is None:
                self.clear()
            else:
                keys_to_remove = [key for key in self.cache if pattern in key]
                for key in keys_to_remove:
                    del self.cache[key]
                    if key in self.access_order:
                        self.access_order.remove(key)

    def get_stats(self) -> dict[str, Any]:
        """Get cache statistics."""
        with self.lock:
            return {
                "size": len(self.cache),
                "max_size": self.max_size,
                "ttl_seconds": self.ttl_seconds,
            }


@dataclass
class ConnectionInfo:
    """Information about a pooled connection."""

    connection: sqlite3.Connection
    created_at: datetime
    last_used: datetime
    use_count: int = 0
    is_healthy: bool = True

    def update_usage(self):
        """Update usage statistics."""
        self.last_used = datetime.now()
        self.use_count += 1


class ConnectionPool:
    """
    Enhanced connection pool for database connections.

    Features:
    - Connection health checks
    - Connection reuse strategies
    - Connection timeout handling
    - Connection statistics
    """

    def __init__(
        self,
        db_path: str,
        max_connections: int = 10,
        check_same_thread: bool = False,
        connection_timeout: float = 300.0,
        health_check_interval: float = 60.0,
        max_idle_time: float = 600.0,
        min_connections: int = 2,
        connection_retry_attempts: int = 3,
    ):
        """
        Initialize connection pool (enhanced).

        Args:
            db_path: Path to database
            max_connections: Maximum number of connections
            check_same_thread: SQLite check_same_thread setting
            connection_timeout: Connection timeout in seconds
            health_check_interval: Health check interval in seconds
            max_idle_time: Maximum idle time before closing connection
            min_connections: Minimum number of connections to maintain
            connection_retry_attempts: Retry attempts for failed connections
        """
        self.db_path = db_path
        self.max_connections = max_connections
        self.min_connections = min_connections
        self.check_same_thread = check_same_thread
        self.connection_timeout = connection_timeout
        self.health_check_interval = health_check_interval
        self.max_idle_time = max_idle_time
        self.connection_retry_attempts = connection_retry_attempts

        self.pool: deque = deque()
        self.active_connections: int = 0
        self.total_connections_created: int = 0
        self.total_connections_reused: int = 0
        self.total_connections_closed: int = 0
        self.total_health_checks: int = 0
        self.total_health_check_failures: int = 0
        self.total_connection_errors: int = 0
        self.last_health_check: datetime | None = None

        self.lock = threading.Lock()

        # Initialize minimum connections
        self._initialize_min_connections()

    def _initialize_min_connections(self):
        """Initialize minimum number of connections."""
        try:
            for _ in range(self.min_connections):
                conn = self._create_connection()
                conn_info = ConnectionInfo(
                    connection=conn,
                    created_at=datetime.now(),
                    last_used=datetime.now(),
                    use_count=0,
                    is_healthy=True,
                )
                self.pool.append(conn_info)
                self.active_connections += 1
                self.total_connections_created += 1
            logger.info(f"Initialized {self.min_connections} minimum connections")
        except Exception as e:
            logger.warning(f"Failed to initialize minimum connections: {e}")

    def _create_connection(self) -> sqlite3.Connection:
        """Create a new database connection (enhanced with retry)."""
        last_error = None
        for attempt in range(self.connection_retry_attempts):
            try:
                conn = sqlite3.connect(
                    self.db_path,
                    check_same_thread=self.check_same_thread,
                    timeout=self.connection_timeout,
                )
                # Enable WAL mode for better concurrency
                conn.execute("PRAGMA journal_mode=WAL")
                # Optimize for performance
                conn.execute("PRAGMA synchronous=NORMAL")
                conn.execute("PRAGMA cache_size=10000")
                conn.execute("PRAGMA temp_store=MEMORY")
                return conn
            except Exception as e:
                last_error = e
                if attempt < self.connection_retry_attempts - 1:
                    time.sleep(0.1 * (attempt + 1))  # Exponential backoff
                else:
                    self.total_connection_errors += 1
                    logger.error(
                        f"Failed to create connection after " f"{attempt + 1} attempts: {e}"
                    )
        if last_error:
            raise last_error
        raise RuntimeError("Failed to create connection")

    def _is_connection_healthy(self, conn: sqlite3.Connection) -> bool:
        """
        Check if a connection is healthy (enhanced).

        Args:
            conn: Database connection

        Returns:
            True if connection is healthy, False otherwise
        """
        try:
            # Enhanced health check: execute multiple lightweight queries
            cursor = conn.cursor()

            # Check 1: Basic connectivity
            cursor.execute("SELECT 1")
            cursor.fetchone()

            # Check 2: Database integrity (lightweight)
            cursor.execute("PRAGMA integrity_check(1)")
            result = cursor.fetchone()
            if result and result[0] != "ok":
                logger.warning("Database integrity check failed")
                cursor.close()
                return False

            # Check 3: Connection state
            cursor.execute("PRAGMA database_list")
            databases = cursor.fetchall()
            if not databases:
                cursor.close()
                return False

            cursor.close()
            return True
        except Exception as e:
            logger.debug(f"Connection health check failed: {e}")
            return False

    def _should_check_health(self) -> bool:
        """Check if health check should be performed."""
        if self.last_health_check is None:
            return True
        elapsed = (datetime.now() - self.last_health_check).total_seconds()
        return elapsed >= self.health_check_interval

    def _cleanup_idle_connections(self):
        """Remove idle connections that exceed max_idle_time (enhanced)."""
        now = datetime.now()
        connections_to_remove = []
        target_pool_size = max(self.min_connections, len(self.pool) - 1)

        for i, conn_info in enumerate(self.pool):
            idle_time = (now - conn_info.last_used).total_seconds()
            # Only remove if exceeds max_idle_time AND above min
            if idle_time > self.max_idle_time and len(self.pool) > target_pool_size:
                connections_to_remove.append(i)

        # Remove from end to preserve indices
        for i in reversed(connections_to_remove):
            conn_info = self.pool[i]
            try:
                conn_info.connection.close()
                self.total_connections_closed += 1
            except Exception:
                ...
            del self.pool[i]
            self.active_connections -= 1

    @contextmanager
    def get_connection(self):
        """
        Get a connection from the pool (context manager).

        Yields:
            Database connection
        """
        conn_info = None
        conn = None
        try:
            with self.lock:
                # Cleanup idle connections periodically
                self._cleanup_idle_connections()

                # Try to reuse existing connection
                while self.pool:
                    conn_info = self.pool.popleft()

                    # Health check if needed
                    if self._should_check_health():
                        if not self._is_connection_healthy(conn_info.connection):
                            self.total_health_check_failures += 1
                            with suppress(Exception):
                                conn_info.connection.close()
                            conn_info = None
                            continue

                        self.last_health_check = datetime.now()
                        self.total_health_checks += 1

                    # Connection is healthy, reuse it
                    conn_info.update_usage()
                    conn_info.is_healthy = True
                    self.total_connections_reused += 1
                    conn = conn_info.connection
                    break

                # Create new connection if pool is empty
                if conn is None:
                    if self.active_connections < self.max_connections:
                        try:
                            conn = self._create_connection()
                            conn_info = ConnectionInfo(
                                connection=conn,
                                created_at=datetime.now(),
                                last_used=datetime.now(),
                                use_count=1,
                                is_healthy=True,
                            )
                            self.active_connections += 1
                            self.total_connections_created += 1
                        except Exception as e:
                            logger.error(f"Failed to create connection: {e}")
                            raise
                    else:
                        # Pool is full, create temporary connection
                        try:
                            conn = self._create_connection()
                            # Don't pool temporary connections
                            conn_info = None
                        except Exception as e:
                            logger.error(f"Failed to create temporary connection: {e}")
                            raise

            yield conn

        except Exception as e:
            logger.error(f"Connection error: {e}")
            self.total_connection_errors += 1
            # Mark connection as unhealthy
            if conn_info:
                conn_info.is_healthy = False
            raise
        finally:
            if conn and conn_info:
                # Return connection to pool if it's healthy
                with self.lock:
                    # Re-check health before returning to pool
                    if conn_info.is_healthy:
                        # Quick health check
                        if self._is_connection_healthy(conn):
                            if len(self.pool) < self.max_connections:
                                self.pool.append(conn_info)
                            else:
                                # Pool is full, close connection
                                try:
                                    conn.close()
                                    self.total_connections_closed += 1
                                except Exception:
                                    ...
                                self.active_connections -= 1
                        else:
                            # Connection failed health check, close it
                            conn_info.is_healthy = False
                            try:
                                conn.close()
                                self.total_connections_closed += 1
                            except Exception:
                                ...
                            self.active_connections -= 1
                    else:
                        # Connection is unhealthy, close it
                        try:
                            conn.close()
                            self.total_connections_closed += 1
                        except Exception:
                            ...
                        if conn_info in self.pool:
                            self.pool.remove(conn_info)
                        self.active_connections -= 1
            elif conn and not conn_info:
                # Temporary connection, close it
                try:
                    conn.close()
                    self.total_connections_closed += 1
                except Exception:
                    ...

    def get_stats(self) -> dict[str, Any]:
        """Get connection pool statistics (enhanced)."""
        with self.lock:
            total_operations = self.total_connections_created + self.total_connections_reused
            reuse_rate = (
                self.total_connections_reused / total_operations if total_operations > 0 else 0.0
            )
            health_check_success_rate = (
                (self.total_health_checks - self.total_health_check_failures)
                / self.total_health_checks
                if self.total_health_checks > 0
                else 1.0
            )

            # Calculate average connection age
            now = datetime.now()
            connection_ages = [
                (now - conn_info.created_at).total_seconds() for conn_info in self.pool
            ]
            avg_connection_age = (
                sum(connection_ages) / len(connection_ages) if connection_ages else 0.0
            )

            # Calculate average use count
            use_counts = [conn_info.use_count for conn_info in self.pool]
            avg_use_count = sum(use_counts) / len(use_counts) if use_counts else 0.0

            return {
                "pool_size": len(self.pool),
                "active_connections": self.active_connections,
                "min_connections": self.min_connections,
                "max_connections": self.max_connections,
                "total_connections_created": self.total_connections_created,
                "total_connections_reused": self.total_connections_reused,
                "total_connections_closed": (self.total_connections_closed),
                "reuse_rate": f"{reuse_rate * 100:.2f}%",
                "total_health_checks": (self.total_health_checks),
                "total_health_check_failures": (self.total_health_check_failures),
                "health_check_success_rate": (f"{health_check_success_rate * 100:.2f}%"),
                "total_connection_errors": (self.total_connection_errors),
                "avg_connection_age_seconds": (avg_connection_age),
                "avg_use_count": (avg_use_count),
                "last_health_check": (
                    self.last_health_check.isoformat() if self.last_health_check else None
                ),
            }

    def close_all(self):
        """Close all connections."""
        with self.lock:
            while self.pool:
                conn_info = self.pool.popleft()
                with suppress(Exception):
                    conn_info.connection.close()
            self.active_connections = 0


class DatabaseQueryOptimizer:
    """
    Database query optimizer with caching, pooling, and monitoring.

    Features:
    - Connection pooling
    - Query caching (LRU with TTL)
    - Query statistics
    - Index management
    - Query monitoring
    """

    def __init__(
        self,
        db_path: str,
        enable_cache: bool = True,
        cache_size: int = 100,
        cache_ttl: float = 300.0,
        max_connections: int = 10,
    ):
        """
        Initialize query optimizer.

        Args:
            db_path: Path to database
            enable_cache: Enable query caching
            cache_size: Cache size
            cache_ttl: Cache TTL (seconds)
            max_connections: Maximum connections in pool
        """
        self.db_path = db_path
        self.enable_cache = enable_cache
        self.cache = (
            QueryCache(max_size=cache_size, ttl_seconds=cache_ttl) if enable_cache else None
        )
        self.pool = ConnectionPool(db_path, max_connections=max_connections)
        self.query_stats: dict[str, QueryStats] = {}
        self.lock = threading.Lock()

        # SQLAlchemy engine if available
        self.engine = None
        if HAS_SQLALCHEMY:
            try:
                self.engine = create_engine(
                    f"sqlite:///{db_path}",
                    poolclass=StaticPool,
                    connect_args={"check_same_thread": False},
                    echo=False,
                )
            except Exception as e:
                logger.warning(f"Failed to create SQLAlchemy engine: {e}")

    def execute_query(
        self,
        query: str,
        parameters: tuple | None = None,
        use_cache: bool = True,
        fetch_all: bool = True,
    ) -> list[dict[str, Any]]:
        """
        Execute a query with optimization.

        Args:
            query: SQL query
            parameters: Query parameters
            use_cache: Whether to use cache
            fetch_all: Whether to fetch all results

        Returns:
            Query results
        """
        # Generate cache key
        cache_key = None
        if use_cache and self.cache:
            cache_key = self._generate_cache_key(query, parameters)
            cached_result = self.cache.get(cache_key)
            if cached_result is not None:
                self._record_query_stats(query, 0.0, cached=True)
                return list(cached_result)

        # Execute query
        start_time = time.time()
        try:
            with self.pool.get_connection() as conn:
                cursor = conn.cursor()
                if parameters:
                    cursor.execute(query, parameters)
                else:
                    cursor.execute(query)

                if fetch_all:
                    # Get column names
                    columns = [desc[0] for desc in cursor.description] if cursor.description else []
                    rows = cursor.fetchall()
                    results = [dict(zip(columns, row)) for row in rows]
                else:
                    results = []

                conn.commit()

                execution_time = time.time() - start_time
                self._record_query_stats(query, execution_time, cached=False)

                # Cache result
                if use_cache and self.cache and cache_key:
                    self.cache.set(cache_key, results)

                return results

        except Exception as e:
            execution_time = time.time() - start_time
            self._record_query_stats(query, execution_time, cached=False)
            logger.error(f"Query execution failed: {e}")
            raise

    def _generate_cache_key(self, query: str, parameters: tuple | None) -> str:
        """Generate cache key for query."""
        key = query
        if parameters:
            key += str(parameters)
        return key

    def _record_query_stats(self, query: str, execution_time: float, cached: bool):
        """Record query statistics."""
        with self.lock:
            if query not in self.query_stats:
                self.query_stats[query] = QueryStats(query=query)
            self.query_stats[query].record_execution(execution_time, cached=cached)

    def create_index(
        self,
        table: str,
        columns: str | list[str],
        unique: bool = False,
    ):
        """
        Create an index on a table.

        Args:
            table: Table name
            columns: Column name(s)
            unique: Whether index is unique
        """
        if isinstance(columns, str):
            columns = [columns]

        index_name = f"idx_{table}_{'_'.join(columns)}"
        columns_str = ", ".join(columns)
        unique_str = "UNIQUE" if unique else ""

        query = (
            f"CREATE {unique_str} INDEX IF NOT EXISTS " f"{index_name} ON {table} ({columns_str})"
        )

        try:
            with self.pool.get_connection() as conn:
                conn.execute(query)
                conn.commit()
                logger.info(f"Created index: {index_name}")
        except Exception as e:
            logger.error(f"Failed to create index: {e}")
            raise

    def analyze_table(self, table: str) -> dict[str, Any]:
        """
        Analyze a table for optimization opportunities.

        Args:
            table: Table name

        Returns:
            Analysis results
        """
        if not re.match(r"^[a-zA-Z_][a-zA-Z0-9_]*$", table):
            raise ValueError(f"Invalid table name: {table!r}")
        try:
            # Get table info
            with self.pool.get_connection() as conn:
                cursor = conn.cursor()

                # Whitelist: only use table names from schema (avoids SQL injection)
                cursor.execute(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name=?",
                    (table,),
                )
                row = cursor.fetchone()
                if row is None:
                    raise ValueError(f"Table not found: {table!r}")
                schema_table = row[0]

                # Get row count: schema_table validated via sqlite_master whitelist.
                # SQLite does not support ? for identifiers; validate and quote per SQLite rules.
                if not re.match(r"^[a-zA-Z_][a-zA-Z0-9_]*$", schema_table):
                    raise ValueError(f"Invalid table name from schema: {schema_table!r}")
                _parts = ("SELECT COUNT(*) FROM ", '"' + schema_table.replace('"', '""') + '"')
                cursor.execute("".join(_parts))
                row_count = cursor.fetchone()[0]

                # Get index info (tbl_name is a value; use parameterized query)
                cursor.execute(
                    "SELECT name, sql FROM sqlite_master " "WHERE type='index' AND tbl_name=?",
                    (table,),
                )
                indexes = cursor.fetchall()

                return {
                    "table": table,
                    "row_count": row_count,
                    "indexes": [{"name": idx[0], "sql": idx[1]} for idx in indexes],
                }
        except Exception as e:
            logger.error(f"Table analysis failed: {e}")
            return {"table": table, "error": str(e)}

    def get_slow_queries(self, threshold_ms: float = 100.0) -> list[dict[str, Any]]:
        """
        Get queries that exceed execution time threshold.

        Args:
            threshold_ms: Threshold in milliseconds

        Returns:
            List of slow queries
        """
        with self.lock:
            slow_queries = []
            for query, stats in self.query_stats.items():
                if stats.average_time * 1000 > threshold_ms:
                    slow_queries.append(
                        {
                            "query": query,
                            "average_time_ms": stats.average_time * 1000,
                            "execution_count": stats.execution_count,
                            "min_time_ms": stats.min_time * 1000,
                            "max_time_ms": stats.max_time * 1000,
                        }
                    )

            # Sort by average time (descending)
            slow_queries.sort(
                key=lambda x: float(str(x["average_time_ms"])),
                reverse=True,
            )
            return slow_queries

    def get_query_stats(self) -> dict[str, Any]:
        """Get query statistics."""
        with self.lock:
            total_queries = sum(s.execution_count for s in self.query_stats.values())
            total_time = sum(s.total_time for s in self.query_stats.values())
            cache_hits = sum(s.cache_hits for s in self.query_stats.values())
            cache_misses = sum(s.cache_misses for s in self.query_stats.values())

            return {
                "total_queries": total_queries,
                "total_time": total_time,
                "average_time": (total_time / total_queries if total_queries > 0 else 0.0),
                "cache_hits": cache_hits,
                "cache_misses": cache_misses,
                "cache_hit_rate": (
                    cache_hits / (cache_hits + cache_misses)
                    if (cache_hits + cache_misses) > 0
                    else 0.0
                ),
                "query_count": len(self.query_stats),
                "cache_stats": self.cache.get_stats() if self.cache else None,
                "connection_pool_stats": self.pool.get_stats(),
            }

    def clear_cache(self):
        """Clear query cache."""
        if self.cache:
            self.cache.clear()

    def close(self):
        """Close connections and cleanup."""
        self.pool.close_all()
        if self.engine:
            self.engine.dispose()


# Factory function
def create_query_optimizer(
    db_path: str,
    enable_cache: bool = True,
    cache_size: int = 100,
    cache_ttl: float = 300.0,
) -> DatabaseQueryOptimizer:
    """
    Create database query optimizer.

    Args:
        db_path: Path to database
        enable_cache: Enable query caching
        cache_size: Cache size
        cache_ttl: Cache TTL (seconds)

    Returns:
        DatabaseQueryOptimizer instance
    """
    return DatabaseQueryOptimizer(
        db_path=db_path,
        enable_cache=enable_cache,
        cache_size=cache_size,
        cache_ttl=cache_ttl,
    )


# Export
__all__ = [
    "ConnectionPool",
    "DatabaseQueryOptimizer",
    "QueryCache",
    "QueryStats",
    "create_query_optimizer",
]
