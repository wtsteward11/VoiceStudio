"""
Tests for file boundary enforcement.
"""

import os


class TestFileBoundaries:
    """Test that file operations respect policy boundaries."""

    def test_read_allowed_project_file(self, tool_gateway, agent_identity, temp_dir):
        """Test reading a file within allowed paths."""
        # Create a test file in a mock project directory
        test_file = temp_dir / "src" / "test.py"
        test_file.parent.mkdir(parents=True, exist_ok=True)
        test_file.write_text("print('hello')")

        # Set PROJECT_ROOT to temp_dir
        os.environ["PROJECT_ROOT"] = str(temp_dir)

        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="ReadFile",
            parameters={"path": str(test_file)},
        )

        assert result.success
        assert result.tool_result.output == "print('hello')"

    def test_read_denied_env_file(self, tool_gateway, agent_identity, temp_dir):
        """Test that .env files are denied."""
        # Create a .env file
        env_file = temp_dir / ".env"
        env_file.write_text("SECRET=value")

        os.environ["PROJECT_ROOT"] = str(temp_dir)

        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="ReadFile",
            parameters={"path": str(env_file)},
        )

        assert result.denied
        violations_text = " ".join(result.policy_result.violations or []).lower()
        assert "denied" in violations_text

    def test_read_denied_secrets_directory(self, tool_gateway, agent_identity, temp_dir):
        """Test that secrets directory is denied."""
        secrets_file = temp_dir / "secrets" / "api_key.txt"
        secrets_file.parent.mkdir(parents=True, exist_ok=True)
        secrets_file.write_text("my_secret_key")

        os.environ["PROJECT_ROOT"] = str(temp_dir)

        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="ReadFile",
            parameters={"path": str(secrets_file)},
        )

        assert result.denied

    def test_write_allowed_src_file(self, tool_gateway, agent_identity, temp_dir):
        """Test writing to allowed src directory."""
        test_file = temp_dir / "src" / "new_file.py"
        test_file.parent.mkdir(parents=True, exist_ok=True)

        os.environ["PROJECT_ROOT"] = str(temp_dir)

        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="WriteFile",
            parameters={
                "path": str(test_file),
                "content": "# New file",
            },
        )

        assert result.success
        assert test_file.exists()
        assert test_file.read_text() == "# New file"

    def test_write_denied_git_directory(self, tool_gateway, agent_identity, temp_dir):
        """Test that .git directory writes are denied."""
        git_file = temp_dir / ".git" / "config"
        git_file.parent.mkdir(parents=True, exist_ok=True)

        os.environ["PROJECT_ROOT"] = str(temp_dir)

        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="WriteFile",
            parameters={
                "path": str(git_file),
                "content": "malicious content",
            },
        )

        assert result.denied

    def test_write_denied_executable(self, tool_gateway, agent_identity, temp_dir):
        """Test that writing executable files is denied."""
        exe_file = temp_dir / "malware.exe"

        os.environ["PROJECT_ROOT"] = str(temp_dir)

        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="WriteFile",
            parameters={
                "path": str(exe_file),
                "content": "MZ...",
            },
        )

        assert result.denied

    def test_delete_allowed_test_file(self, tool_gateway, agent_identity, temp_dir):
        """Test deleting a file in allowed paths."""
        test_file = temp_dir / "tests" / "old_test.py"
        test_file.parent.mkdir(parents=True, exist_ok=True)
        test_file.write_text("# old test")

        os.environ["PROJECT_ROOT"] = str(temp_dir)

        # Note: DeleteFile requires high risk approval normally
        # For this test we're checking the policy allows the path
        result = tool_gateway.check_policy(
            agent=agent_identity,
            tool_name="DeleteFile",
            parameters={"path": str(test_file)},
        )

        # Path should be allowed, even if approval is required
        assert result.is_allowed


class TestFileBoundaryEdgeCases:
    """Edge cases for file boundary enforcement."""

    def test_path_traversal_attempt(self, tool_gateway, agent_identity, temp_dir):
        """Test that path traversal is handled."""
        os.environ["PROJECT_ROOT"] = str(temp_dir)

        # Attempt path traversal
        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="ReadFile",
            parameters={"path": str(temp_dir / "src" / ".." / ".." / "etc" / "passwd")},
        )

        # Should either be denied or fail to find file
        # The important thing is it doesn't succeed in reading sensitive files
        if result.success:
            assert "passwd" not in str(result.tool_result.output).lower()

    def test_unicode_path(self, tool_gateway, agent_identity, temp_dir):
        """Test handling of unicode paths."""
        unicode_file = temp_dir / "src" / "тест.py"
        unicode_file.parent.mkdir(parents=True, exist_ok=True)
        unicode_file.write_text("# unicode test")

        os.environ["PROJECT_ROOT"] = str(temp_dir)

        result = tool_gateway.execute(
            agent=agent_identity,
            tool_name="ReadFile",
            parameters={"path": str(unicode_file)},
        )

        assert result.success
