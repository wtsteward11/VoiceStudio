"""
Database Integration Tests.

Tests for database operations with transaction rollback,
data integrity, and schema validation.
"""

import logging
import os
import sqlite3
import tempfile
import uuid
from datetime import datetime, timedelta
from pathlib import Path

import pytest

from .base import IntegrationTestBase, integration

logger = logging.getLogger(__name__)


# =============================================================================
# Quality Metrics Database Tests
# =============================================================================


class TestQualityMetricsDatabase(IntegrationTestBase):
    """Tests for the QualityMetricsDatabase class."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        # Cleanup
        try:
            os.unlink(db_path)
        # ALLOWED: bare except - Best effort cleanup, failure is acceptable
        except OSError:
            pass

    @pytest.fixture
    def quality_db(self, temp_db_path):
        """Create a QualityMetricsDatabase instance."""
        from backend.services.quality_metrics_db import QualityMetricsDatabase

        return QualityMetricsDatabase(db_path=temp_db_path)

    @integration
    def test_database_initialization(self, quality_db, temp_db_path):
        """Test that database is properly initialized."""
        # Verify database file exists
        assert Path(temp_db_path).exists()

        # Verify table and indexes exist
        conn = sqlite3.connect(temp_db_path)
        try:
            cursor = conn.execute("SELECT name FROM sqlite_master WHERE type='table'")
            tables = [row[0] for row in cursor.fetchall()]
            assert "quality_history" in tables

            # Verify indexes
            cursor = conn.execute("SELECT name FROM sqlite_master WHERE type='index'")
            indexes = [row[0] for row in cursor.fetchall()]
            assert any("profile_id" in idx for idx in indexes)
            assert any("engine" in idx for idx in indexes)
            assert any("timestamp" in idx for idx in indexes)
        finally:
            conn.close()

    @integration
    def test_insert_entry(self, quality_db):
        """Test inserting a quality history entry."""
        entry = {
            "id": str(uuid.uuid4()),
            "profile_id": "profile-001",
            "timestamp": datetime.utcnow().isoformat(),
            "engine": "xtts_v2",
            "metrics": {"mos_score": 4.2, "similarity": 0.85},
            "quality_score": 0.88,
            "synthesis_text": "Test synthesis",
        }

        entry_id = quality_db.insert(entry)
        assert entry_id == entry["id"]

    @integration
    def test_get_entries_by_profile(self, quality_db):
        """Test retrieving entries by profile ID."""
        profile_id = "profile-002"

        # Insert multiple entries
        for i in range(5):
            entry = {
                "id": str(uuid.uuid4()),
                "profile_id": profile_id,
                "timestamp": datetime.utcnow().isoformat(),
                "engine": "chatterbox",
                "metrics": {"mos_score": 4.0 + i * 0.1},
                "quality_score": 0.8 + i * 0.02,
            }
            quality_db.insert(entry)

        # Retrieve entries
        entries = quality_db.get_entries_by_profile(profile_id)
        assert len(entries) == 5
        assert all(e["profile_id"] == profile_id for e in entries)

    @integration
    def test_get_entries_by_profile_with_limit(self, quality_db):
        """Test retrieving entries with limit."""
        profile_id = "profile-003"

        # Insert 10 entries
        for _i in range(10):
            entry = {
                "id": str(uuid.uuid4()),
                "profile_id": profile_id,
                "timestamp": datetime.utcnow().isoformat(),
                "engine": "xtts_v2",
                "metrics": {},
                "quality_score": 0.9,
            }
            quality_db.insert(entry)

        # Retrieve with limit
        entries = quality_db.get_entries_by_profile(profile_id, limit=5)
        assert len(entries) == 5

    @integration
    def test_get_entries_by_profile_with_date_range(self, quality_db):
        """Test retrieving entries within date range."""
        profile_id = "profile-004"
        now = datetime.utcnow()

        # Insert entries with different timestamps
        for i in range(5):
            ts = now - timedelta(hours=i)
            entry = {
                "id": str(uuid.uuid4()),
                "profile_id": profile_id,
                "timestamp": ts.isoformat(),
                "engine": "xtts_v2",
                "metrics": {},
                "quality_score": 0.9,
            }
            quality_db.insert(entry)

        # Query with since filter
        since = (now - timedelta(hours=2)).isoformat()
        entries = quality_db.get_entries_by_profile(profile_id, since=since)
        assert len(entries) == 3  # Entries from last 2 hours

    @integration
    def test_query_by_engine(self, quality_db):
        """Test querying entries by engine ID."""
        engine_id = "chatterbox"

        # Insert entries for multiple engines
        for engine in ["xtts_v2", "chatterbox", "kokoro"]:
            for _i in range(3):
                entry = {
                    "id": str(uuid.uuid4()),
                    "profile_id": f"profile-{engine}",
                    "timestamp": datetime.utcnow().isoformat(),
                    "engine": engine,
                    "metrics": {},
                    "quality_score": 0.9,
                }
                quality_db.insert(entry)

        # Query by engine
        entries = quality_db.query(engine_id)
        assert len(entries) == 3
        assert all(e["engine"] == engine_id for e in entries)

    @integration
    def test_get_engine_metrics(self, quality_db):
        """Test aggregated engine metrics."""
        engine_id = "test_engine"

        # Insert entries with various metrics
        for i in range(10):
            entry = {
                "id": str(uuid.uuid4()),
                "profile_id": "profile-metrics",
                "timestamp": datetime.utcnow().isoformat(),
                "engine": engine_id,
                "metrics": {
                    "mos_score": 4.0 + i * 0.1,
                    "similarity": 0.8 + i * 0.01,
                    "latency_ms": 100 + i * 10,
                },
                "quality_score": 0.85 + i * 0.01,
            }
            quality_db.insert(entry)

        # Get aggregated metrics
        metrics = quality_db.get_engine_metrics(engine_id, timedelta(hours=1))

        assert metrics["engine_id"] == engine_id
        assert metrics["synthesis_count"] == 10
        assert metrics["avg_quality_score"] is not None
        assert metrics["avg_mos_score"] is not None
        assert metrics["p50_latency_ms"] is not None

    @integration
    def test_transaction_isolation(self, temp_db_path):
        """Test that database operations are isolated."""
        from backend.services.quality_metrics_db import QualityMetricsDatabase

        # Create two database instances
        db1 = QualityMetricsDatabase(db_path=temp_db_path)
        db2 = QualityMetricsDatabase(db_path=temp_db_path)

        # Insert from db1
        entry1 = {
            "id": "entry-1",
            "profile_id": "profile-isolation",
            "timestamp": datetime.utcnow().isoformat(),
            "engine": "xtts_v2",
            "metrics": {},
            "quality_score": 0.9,
        }
        db1.insert(entry1)

        # Read from db2
        entries = db2.get_entries_by_profile("profile-isolation")
        assert len(entries) == 1
        assert entries[0]["id"] == "entry-1"


# =============================================================================
# Database Transaction Rollback Tests
# =============================================================================


class TestDatabaseTransactionRollback(IntegrationTestBase):
    """Tests for database transaction rollback."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        try:
            os.unlink(db_path)
        # ALLOWED: bare except - Best effort cleanup, failure is acceptable
        except OSError:
            pass

    @integration
    def test_rollback_on_error(self, temp_db_path):
        """Test that rollback works on error."""
        conn = sqlite3.connect(temp_db_path)
        conn.execute("CREATE TABLE test_table (id TEXT PRIMARY KEY, value TEXT)")
        conn.commit()

        # Start transaction
        conn.execute("INSERT INTO test_table VALUES ('id1', 'value1')")

        # Simulate error and rollback
        conn.rollback()

        # Verify data was not inserted
        cursor = conn.execute("SELECT COUNT(*) FROM test_table")
        count = cursor.fetchone()[0]
        assert count == 0

        conn.close()

    @integration
    def test_commit_persists_data(self, temp_db_path):
        """Test that commit persists data."""
        conn = sqlite3.connect(temp_db_path)
        conn.execute("CREATE TABLE test_table (id TEXT PRIMARY KEY, value TEXT)")
        conn.execute("INSERT INTO test_table VALUES ('id1', 'value1')")
        conn.commit()
        conn.close()

        # Verify data persists after connection closed
        conn2 = sqlite3.connect(temp_db_path)
        cursor = conn2.execute("SELECT COUNT(*) FROM test_table")
        count = cursor.fetchone()[0]
        assert count == 1
        conn2.close()

    @integration
    def test_context_manager_rollback(self, temp_db_path):
        """Test transaction rollback with context manager."""
        conn = sqlite3.connect(temp_db_path)
        conn.execute("CREATE TABLE test_table (id TEXT PRIMARY KEY, value TEXT)")
        conn.commit()

        try:
            with conn:
                conn.execute("INSERT INTO test_table VALUES ('id1', 'value1')")
                raise ValueError("Simulated error")
        # ALLOWED: bare except - Expected for negative test case
        except ValueError:
            pass

        # Verify data was rolled back
        cursor = conn.execute("SELECT COUNT(*) FROM test_table")
        count = cursor.fetchone()[0]
        assert count == 0

        conn.close()


# =============================================================================
# Database Context Manager Tests
# =============================================================================


class TestDatabaseContextManager(IntegrationTestBase):
    """Tests for DatabaseTestContext from fixtures."""

    @integration
    def test_database_context_creates_tables(self):
        """Test DatabaseTestContext can create tables."""
        from .fixtures import create_test_database

        with create_test_database(in_memory=True) as ctx:
            ctx.execute("CREATE TABLE test_table (id TEXT PRIMARY KEY, value TEXT)")
            ctx.execute("INSERT INTO test_table VALUES ('id1', 'value1')")

            result = ctx.query("SELECT * FROM test_table")
            assert len(result) == 1
            assert result[0]["id"] == "id1"

    @integration
    def test_database_context_seed_data(self):
        """Test DatabaseTestContext seed_data method."""
        from .fixtures import create_test_database

        with create_test_database(in_memory=True) as ctx:
            ctx.execute("CREATE TABLE test_profiles (id TEXT, name TEXT, active INTEGER)")
            ctx.seed_data(
                "test_profiles",
                [
                    {"id": "p1", "name": "Profile 1", "active": 1},
                    {"id": "p2", "name": "Profile 2", "active": 0},
                ],
            )

            result = ctx.query("SELECT * FROM test_profiles")
            assert len(result) == 2

    @integration
    def test_database_context_automatic_cleanup(self):
        """Test that in-memory database is cleaned up."""
        from .fixtures import create_test_database

        # Create table in context
        with create_test_database(in_memory=True) as ctx:
            ctx.execute("CREATE TABLE temp_table (id TEXT)")
            ctx.execute("INSERT INTO temp_table VALUES ('test')")
            result = ctx.query("SELECT * FROM temp_table")
            assert len(result) == 1

        # Context closed, new context should be clean
        with create_test_database(in_memory=True) as ctx2:
            result = ctx2.query(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='temp_table'"
            )
            assert len(result) == 0  # Table should not exist


# =============================================================================
# Database Schema Validation Tests
# =============================================================================


class TestDatabaseSchemaValidation(IntegrationTestBase):
    """Tests for database schema validation."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        try:
            os.unlink(db_path)
        # ALLOWED: bare except - Best effort cleanup, failure is acceptable
        except OSError:
            pass

    @integration
    def test_quality_metrics_schema(self, temp_db_path):
        """Verify quality_history table schema."""
        from backend.services.quality_metrics_db import QualityMetricsDatabase

        QualityMetricsDatabase(db_path=temp_db_path)

        conn = sqlite3.connect(temp_db_path)
        try:
            cursor = conn.execute("PRAGMA table_info(quality_history)")
            columns = {row[1]: row[2] for row in cursor.fetchall()}

            # Verify required columns
            assert "id" in columns
            assert "profile_id" in columns
            assert "timestamp" in columns
            assert "engine" in columns
            assert "metrics" in columns
            assert "quality_score" in columns
        finally:
            conn.close()

    @integration
    def test_data_integrity_constraints(self, temp_db_path):
        """Test data integrity constraints."""
        from backend.services.quality_metrics_db import QualityMetricsDatabase

        db = QualityMetricsDatabase(db_path=temp_db_path)

        # Insert first entry
        entry1 = {
            "id": "unique-id-001",
            "profile_id": "profile-001",
            "timestamp": datetime.utcnow().isoformat(),
            "engine": "xtts_v2",
            "metrics": {},
            "quality_score": 0.9,
        }
        db.insert(entry1)

        # Try to insert duplicate ID (should fail)
        entry2 = {
            "id": "unique-id-001",  # Same ID
            "profile_id": "profile-002",
            "timestamp": datetime.utcnow().isoformat(),
            "engine": "chatterbox",
            "metrics": {},
            "quality_score": 0.8,
        }

        with pytest.raises(sqlite3.IntegrityError):
            db.insert(entry2)


# =============================================================================
# Database Cleanup and Limits Tests
# =============================================================================


class TestDatabaseCleanup(IntegrationTestBase):
    """Tests for database cleanup and entry limits."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        try:
            os.unlink(db_path)
        # ALLOWED: bare except - Best effort cleanup, failure is acceptable
        except OSError:
            pass

    @integration
    def test_per_profile_limit_enforced(self, temp_db_path):
        """Test per-profile entry limit is enforced."""
        from backend.services.quality_metrics_db import (
            _MAX_ENTRIES_PER_PROFILE,
            QualityMetricsDatabase,
        )

        db = QualityMetricsDatabase(db_path=temp_db_path)
        profile_id = "profile-limit-test"

        # Insert more than limit
        entries_to_insert = _MAX_ENTRIES_PER_PROFILE + 10
        for i in range(entries_to_insert):
            entry = {
                "id": f"entry-{i}",
                "profile_id": profile_id,
                "timestamp": datetime.utcnow().isoformat(),
                "engine": "xtts_v2",
                "metrics": {},
                "quality_score": 0.9,
            }
            db.insert(entry)

        # Verify limit is enforced
        entries = db.get_entries_by_profile(profile_id)
        assert len(entries) <= _MAX_ENTRIES_PER_PROFILE


# =============================================================================
# Integration with Service Layer Tests
# =============================================================================


class TestDatabaseServiceIntegration(IntegrationTestBase):
    """Tests for database integration with service layer."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        try:
            os.unlink(db_path)
        # ALLOWED: bare except - Best effort cleanup, failure is acceptable
        except OSError:
            pass

    @integration
    def test_get_quality_metrics_db_singleton(self):
        """Test singleton pattern for quality metrics db."""
        # Reset singleton
        import backend.services.quality_metrics_db as qm_module
        from backend.services.quality_metrics_db import get_quality_metrics_db

        qm_module._quality_db = None

        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            temp_path = f.name

        try:
            db1 = get_quality_metrics_db(temp_path)
            db2 = get_quality_metrics_db()  # Should return same instance

            assert db1 is db2
        finally:
            # Reset singleton
            qm_module._quality_db = None
            try:
                os.unlink(temp_path)
            # ALLOWED: bare except - Best effort cleanup, failure is acceptable
            except OSError:
                pass

    @integration
    def test_concurrent_database_access(self, temp_db_path):
        """Test database handles concurrent access."""
        import threading

        from backend.services.quality_metrics_db import QualityMetricsDatabase

        db = QualityMetricsDatabase(db_path=temp_db_path)
        errors = []

        def insert_entries(thread_id):
            try:
                for i in range(10):
                    entry = {
                        "id": f"thread-{thread_id}-entry-{i}",
                        "profile_id": f"profile-concurrent-{thread_id}",
                        "timestamp": datetime.utcnow().isoformat(),
                        "engine": "xtts_v2",
                        "metrics": {},
                        "quality_score": 0.9,
                    }
                    db.insert(entry)
            except Exception as e:
                errors.append(e)

        # Run concurrent inserts
        threads = [threading.Thread(target=insert_entries, args=(i,)) for i in range(5)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        # Verify no errors
        assert len(errors) == 0

        # Verify all entries inserted
        all_entries = db.get_all_entries_for_aggregation()
        assert len(all_entries) == 50  # 5 threads * 10 entries
