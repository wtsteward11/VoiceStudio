"""
Database Migrations.

Task 2.3: Idempotent migrations for repository tables.
"""

from backend.infrastructure.migrations.initial_schema import run_migrations

__all__ = ["run_migrations"]
