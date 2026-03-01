#!/usr/bin/env python3
"""
Install VoiceStudio Git Hooks.

Copies hook templates from scripts/hooks/ to .git/hooks/
and makes them executable.

Usage:
    python scripts/install_hooks.py          # Install hooks
    python scripts/install_hooks.py --remove # Remove hooks
"""

import argparse
import os
import shutil
import stat
import sys
from pathlib import Path

# Get project root
PROJECT_ROOT = Path(__file__).parent.parent


def install_hooks():
    """Install git hooks from templates."""
    git_hooks_dir = PROJECT_ROOT / ".git" / "hooks"
    templates_dir = PROJECT_ROOT / "scripts" / "hooks"

    if not git_hooks_dir.exists():
        print(f"Error: Git hooks directory not found: {git_hooks_dir}")
        print("Is this a git repository?")
        return False

    if not templates_dir.exists():
        print(f"Error: Hook templates directory not found: {templates_dir}")
        return False

    hooks = ["pre-commit", "post-commit"]
    installed = []

    for hook_name in hooks:
        template = templates_dir / hook_name
        target = git_hooks_dir / hook_name

        if not template.exists():
            print(f"Warning: Template not found: {template}")
            continue

        # Backup existing hook if it exists and isn't ours
        if target.exists():
            with open(target) as f:
                content = f.read()
            if "VoiceStudio" not in content:
                backup = target.with_suffix(".backup")
                shutil.copy(target, backup)
                print(f"Backed up existing hook to: {backup.name}")

        # Copy template
        shutil.copy(template, target)

        # Make executable (Unix)
        if os.name != "nt":
            target.chmod(target.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

        installed.append(hook_name)
        print(f"Installed: {hook_name}")

    if installed:
        print(f"\nSuccessfully installed {len(installed)} hook(s)")
        print("\nConfiguration environment variables:")
        print("  VOICESTUDIO_AUDIT_ENABLED=1  # Enable audit checking (default)")
        print("  VOICESTUDIO_AUDIT_STRICT=1   # Block commits without audit entries")
        return True
    else:
        print("No hooks installed")
        return False


def remove_hooks():
    """Remove VoiceStudio git hooks."""
    git_hooks_dir = PROJECT_ROOT / ".git" / "hooks"

    if not git_hooks_dir.exists():
        print("Git hooks directory not found")
        return False

    hooks = ["pre-commit", "post-commit"]
    removed = []

    for hook_name in hooks:
        target = git_hooks_dir / hook_name

        if target.exists():
            with open(target) as f:
                content = f.read()
            if "VoiceStudio" in content:
                target.unlink()
                removed.append(hook_name)
                print(f"Removed: {hook_name}")

                # Restore backup if exists
                backup = target.with_suffix(".backup")
                if backup.exists():
                    shutil.move(backup, target)
                    print(f"Restored backup: {hook_name}")

    if removed:
        print(f"\nRemoved {len(removed)} hook(s)")
        return True
    else:
        print("No VoiceStudio hooks found to remove")
        return True


def main():
    parser = argparse.ArgumentParser(
        description="Install VoiceStudio Git Hooks"
    )
    parser.add_argument(
        "--remove",
        action="store_true",
        help="Remove hooks instead of installing",
    )

    args = parser.parse_args()

    success = remove_hooks() if args.remove else install_hooks()

    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())
