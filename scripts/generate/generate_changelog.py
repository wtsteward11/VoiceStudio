#!/usr/bin/env python3
"""
Changelog Generator

Generates changelog entries from git commits following Conventional Commits.

Features:
- Parse Conventional Commits (feat:, fix:, docs:, etc.)
- Group by category (Added, Changed, Fixed, etc.)
- Extract PR links and issue references
- Generate Keep-a-Changelog format
- Detect unreleased changes for next version

Usage:
    python scripts/generate_changelog.py
    python scripts/generate_changelog.py --since v1.0.0
    python scripts/generate_changelog.py --version 1.0.2
    python scripts/generate_changelog.py --dry-run
    python scripts/generate_changelog.py --update

Exit Codes:
    0: Success
    1: No changes to generate
    2: Error occurred
"""

import argparse
import re
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from _env_setup import PROJECT_ROOT

# Changelog path
CHANGELOG_PATH = PROJECT_ROOT / "CHANGELOG.md"


@dataclass
class CommitInfo:
    """Parsed commit information."""
    hash: str
    type: str
    scope: str | None
    description: str
    body: str
    breaking: bool
    pr_number: int | None
    issue_refs: list[int]
    author: str
    date: str


@dataclass
class ChangelogEntry:
    """A changelog entry for a version."""
    version: str
    date: str
    added: list[str]
    changed: list[str]
    deprecated: list[str]
    removed: list[str]
    fixed: list[str]
    security: list[str]
    breaking: list[str]

    @property
    def is_empty(self) -> bool:
        return not any([
            self.added, self.changed, self.deprecated,
            self.removed, self.fixed, self.security, self.breaking
        ])


class ConventionalCommitParser:
    """Parse conventional commit messages."""

    # Commit type mapping to changelog category
    TYPE_MAPPING = {
        "feat": "added",
        "feature": "added",
        "add": "added",
        "fix": "fixed",
        "bugfix": "fixed",
        "docs": "changed",
        "doc": "changed",
        "style": "changed",
        "refactor": "changed",
        "perf": "changed",
        "test": "changed",
        "tests": "changed",
        "build": "changed",
        "ci": "changed",
        "chore": "changed",
        "revert": "removed",
        "deprecate": "deprecated",
        "security": "security",
        "sec": "security",
    }

    # Regex patterns
    COMMIT_PATTERN = re.compile(
        r"^(?P<type>\w+)(?:\((?P<scope>[^)]+)\))?(?P<breaking>!)?:\s*(?P<description>.+)$",
        re.MULTILINE
    )
    PR_PATTERN = re.compile(r"#(\d+)")
    ISSUE_PATTERN = re.compile(r"(?:closes?|fixes?|resolves?)\s+#(\d+)", re.IGNORECASE)
    BREAKING_PATTERN = re.compile(r"BREAKING\s*CHANGE[S]?:", re.IGNORECASE)

    def parse(self, hash: str, message: str, author: str, date: str) -> CommitInfo | None:
        """Parse a single commit message."""
        lines = message.strip().split("\n")
        if not lines:
            return None

        first_line = lines[0].strip()
        body = "\n".join(lines[1:]).strip() if len(lines) > 1 else ""

        # Try to match conventional commit format
        match = self.COMMIT_PATTERN.match(first_line)

        if match:
            commit_type = match.group("type").lower()
            scope = match.group("scope")
            breaking = match.group("breaking") is not None
            description = match.group("description")
        else:
            # Non-conventional commit - try to infer type
            commit_type = "chore"
            scope = None
            breaking = False
            description = first_line

            # Infer type from keywords
            lower_desc = first_line.lower()
            if any(kw in lower_desc for kw in ["add", "feat", "new", "implement"]):
                commit_type = "feat"
            elif any(kw in lower_desc for kw in ["fix", "bug", "patch", "repair"]):
                commit_type = "fix"
            elif any(kw in lower_desc for kw in ["doc", "readme", "comment"]):
                commit_type = "docs"
            elif any(kw in lower_desc for kw in ["refactor", "clean", "improve"]):
                commit_type = "refactor"

        # Check for breaking changes in body
        if self.BREAKING_PATTERN.search(body):
            breaking = True

        # Extract PR number
        pr_match = self.PR_PATTERN.search(first_line)
        pr_number = int(pr_match.group(1)) if pr_match else None

        # Extract issue references
        issue_refs = [int(m.group(1)) for m in self.ISSUE_PATTERN.finditer(message)]

        return CommitInfo(
            hash=hash[:8],
            type=commit_type,
            scope=scope,
            description=description,
            body=body,
            breaking=breaking,
            pr_number=pr_number,
            issue_refs=issue_refs,
            author=author,
            date=date,
        )

    def get_category(self, commit_type: str) -> str:
        """Get changelog category for commit type."""
        return self.TYPE_MAPPING.get(commit_type.lower(), "changed")


class GitLogReader:
    """Read git log and parse commits."""

    def __init__(self, repo_path: Path = PROJECT_ROOT):
        self.repo_path = repo_path
        self.parser = ConventionalCommitParser()

    def get_commits_since(self, since: str | None = None) -> list[CommitInfo]:
        """Get commits since a tag or ref."""
        cmd = ["git", "log", "--pretty=format:%H%x00%an%x00%ad%x00%B%x00%x00", "--date=short"]

        if since:
            cmd.append(f"{since}..HEAD")
        else:
            # Get commits since last tag
            last_tag = self._get_last_tag()
            if last_tag:
                cmd.append(f"{last_tag}..HEAD")

        try:
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                cwd=str(self.repo_path),
            )

            if result.returncode != 0:
                return []

            commits = []
            raw_commits = result.stdout.split("\x00\x00")

            for raw in raw_commits:
                raw = raw.strip()
                if not raw:
                    continue

                parts = raw.split("\x00")
                if len(parts) >= 4:
                    hash = parts[0]
                    author = parts[1]
                    date = parts[2]
                    message = parts[3]

                    commit = self.parser.parse(hash, message, author, date)
                    if commit:
                        commits.append(commit)

            return commits

        except Exception as e:
            print(f"Error reading git log: {e}")
            return []

    def _get_last_tag(self) -> str | None:
        """Get the most recent tag."""
        try:
            result = subprocess.run(
                ["git", "describe", "--tags", "--abbrev=0"],
                capture_output=True,
                text=True,
                cwd=str(self.repo_path),
            )

            if result.returncode == 0:
                return result.stdout.strip()

            return None

        except Exception:
            return None

    def get_current_version(self) -> str:
        """Get current version from tags."""
        tag = self._get_last_tag()
        if tag:
            # Remove 'v' prefix if present
            return tag.lstrip("v")
        return "0.0.0"


class ChangelogGenerator:
    """Generate changelog from commits."""

    def __init__(self):
        self.git_reader = GitLogReader()
        self.parser = ConventionalCommitParser()

    def generate_entry(
        self,
        version: str = "Unreleased",
        since: str | None = None,
    ) -> ChangelogEntry:
        """Generate a changelog entry for a version."""
        commits = self.git_reader.get_commits_since(since)

        entry = ChangelogEntry(
            version=version,
            date=datetime.now().strftime("%Y-%m-%d"),
            added=[],
            changed=[],
            deprecated=[],
            removed=[],
            fixed=[],
            security=[],
            breaking=[],
        )

        for commit in commits:
            category = self.parser.get_category(commit.type)

            # Format entry line
            scope_prefix = f"**{commit.scope}**: " if commit.scope else ""
            pr_suffix = f" (#{commit.pr_number})" if commit.pr_number else ""

            line = f"- {scope_prefix}{commit.description}{pr_suffix}"

            # Add to appropriate category
            if commit.breaking:
                entry.breaking.append(line)

            if category == "added":
                entry.added.append(line)
            elif category == "fixed":
                entry.fixed.append(line)
            elif category == "deprecated":
                entry.deprecated.append(line)
            elif category == "removed":
                entry.removed.append(line)
            elif category == "security":
                entry.security.append(line)
            else:
                entry.changed.append(line)

        return entry

    def format_entry(self, entry: ChangelogEntry) -> str:
        """Format a changelog entry as Markdown."""
        lines = [
            f"## [{entry.version}] - {entry.date}",
            "",
        ]

        sections = [
            ("BREAKING CHANGES", entry.breaking),
            ("Added", entry.added),
            ("Changed", entry.changed),
            ("Deprecated", entry.deprecated),
            ("Removed", entry.removed),
            ("Fixed", entry.fixed),
            ("Security", entry.security),
        ]

        for title, items in sections:
            if items:
                lines.append(f"### {title}")
                lines.append("")
                for item in items:
                    lines.append(item)
                lines.append("")

        return "\n".join(lines)

    def update_changelog(self, entry: ChangelogEntry) -> bool:
        """Update the CHANGELOG.md file with new entry."""
        if not CHANGELOG_PATH.exists():
            # Create new changelog
            content = self._create_new_changelog(entry)
            CHANGELOG_PATH.write_text(content)
            return True

        content = CHANGELOG_PATH.read_text()

        # Find insertion point (after header, before first version)
        lines = content.split("\n")
        insert_index = 0

        for i, line in enumerate(lines):
            if line.startswith("## ["):
                insert_index = i
                break
            if line.startswith("## ") and "Unreleased" not in line:
                insert_index = i
                break

        if insert_index == 0:
            # No existing versions, append after header
            for i, line in enumerate(lines):
                if line.strip() == "" and i > 0:
                    insert_index = i + 1
                    break

        # Insert new entry
        entry_text = self.format_entry(entry)
        new_lines = lines[:insert_index] + entry_text.split("\n") + lines[insert_index:]

        CHANGELOG_PATH.write_text("\n".join(new_lines))
        return True

    def _create_new_changelog(self, entry: ChangelogEntry) -> str:
        """Create a new changelog file."""
        return f"""# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

{self.format_entry(entry)}
"""


def main():
    parser = argparse.ArgumentParser(description="Generate changelog from git history")
    parser.add_argument(
        "--since",
        type=str,
        help="Generate changes since this tag/ref"
    )
    parser.add_argument(
        "--version",
        type=str,
        default="Unreleased",
        help="Version name for the changelog entry"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print changelog without writing to file"
    )
    parser.add_argument(
        "--update",
        action="store_true",
        help="Update CHANGELOG.md with new entry"
    )

    args = parser.parse_args()

    print("=" * 60)
    print("Changelog Generator")
    print("=" * 60)
    print()

    generator = ChangelogGenerator()

    # Generate entry
    entry = generator.generate_entry(
        version=args.version,
        since=args.since,
    )

    if entry.is_empty:
        print("No changes found to generate changelog.")
        sys.exit(1)

    # Count changes
    total_changes = (
        len(entry.added) + len(entry.changed) + len(entry.fixed) +
        len(entry.deprecated) + len(entry.removed) + len(entry.security)
    )

    print(f"Found {total_changes} changes:")
    print(f"  Added:      {len(entry.added)}")
    print(f"  Changed:    {len(entry.changed)}")
    print(f"  Fixed:      {len(entry.fixed)}")
    print(f"  Deprecated: {len(entry.deprecated)}")
    print(f"  Removed:    {len(entry.removed)}")
    print(f"  Security:   {len(entry.security)}")
    print(f"  Breaking:   {len(entry.breaking)}")
    print()

    formatted = generator.format_entry(entry)

    if args.dry_run:
        print("-" * 60)
        print("Generated changelog entry:")
        print("-" * 60)
        print()
        print(formatted)
    elif args.update:
        if generator.update_changelog(entry):
            print(f"Updated: {CHANGELOG_PATH}")
        else:
            print("Failed to update changelog")
            sys.exit(2)
    else:
        # Print to stdout
        print(formatted)

    sys.exit(0)


if __name__ == "__main__":
    main()
