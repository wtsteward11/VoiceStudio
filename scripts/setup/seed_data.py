#!/usr/bin/env python3
"""
Seed Data Script
Populates the backend with initial test data for development and testing.
"""

import sys
from datetime import datetime


def seed_profiles():
    """Seed voice profiles."""
    try:
        from backend.api.routes.profiles import _profiles

        if not _profiles:
            profiles = [
                {
                    "profile_id": "profile-test-1",
                    "name": "Test Voice 1",
                    "language": "en",
                    "description": "Test voice profile for development",
                    "tags": ["test", "development"],
                    "created": datetime.utcnow().isoformat(),
                    "modified": datetime.utcnow().isoformat(),
                },
                {
                    "profile_id": "profile-test-2",
                    "name": "Test Voice 2",
                    "language": "en",
                    "description": "Another test voice profile",
                    "tags": ["test"],
                    "created": datetime.utcnow().isoformat(),
                    "modified": datetime.utcnow().isoformat(),
                },
            ]
            for profile in profiles:
                _profiles[profile["profile_id"]] = profile
            print(f"[OK] Seeded {len(profiles)} voice profiles")
            return len(profiles)
    except Exception as e:
        print(f"[WARN] Failed to seed profiles: {e}")
        return 0


def seed_projects():
    """Seed projects."""
    try:
        from backend.api.routes.projects import _projects

        if not _projects:
            projects = [
                {
                    "project_id": "project-test-1",
                    "name": "Test Project 1",
                    "description": "Test project for development",
                    "created": datetime.utcnow().isoformat(),
                    "modified": datetime.utcnow().isoformat(),
                }
            ]
            for project in projects:
                _projects[project["project_id"]] = project
            print(f"[OK] Seeded {len(projects)} projects")
            return len(projects)
    except Exception as e:
        print(f"[WARN] Failed to seed projects: {e}")
        return 0


def main():
    """Main seed data function."""
    print("Seeding backend data...")

    total = 0
    total += seed_profiles()
    total += seed_projects()

    print(f"[OK] Seeded {total} total items")
    return 0


if __name__ == "__main__":
    sys.exit(main())
