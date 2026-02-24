"""
Unit tests for EditHistory (Phase 21.3)
"""

from backend.services.edit_history import EditCommand, EditHistory


class MockCommand(EditCommand):
    """Mock command for testing."""

    def __init__(self, name: str):
        self.name = name
        self.executed = False
        self.undone = False

    def execute(self):
        self.executed = True
        return {"action": "executed", "name": self.name}

    def undo(self):
        self.undone = True
        return {"action": "undone", "name": self.name}

    @property
    def description(self) -> str:
        return f"MockCommand: {self.name}"


class TestEditHistory:
    """Tests for EditHistory class."""

    def setup_method(self):
        """Set up test fixtures."""
        self.history = EditHistory()

    def test_initialization(self):
        """Test history initializes empty."""
        history = EditHistory()
        assert len(history._undo_stack) == 0
        assert len(history._redo_stack) == 0

    def test_execute_adds_to_undo_stack(self):
        """Test executing a command adds to undo stack."""
        cmd = MockCommand("test")

        self.history.execute(cmd)

        assert cmd.executed is True
        assert len(self.history._undo_stack) == 1

    def test_execute_clears_redo_stack(self):
        """Test executing clears redo stack."""
        cmd1 = MockCommand("first")
        cmd2 = MockCommand("second")
        cmd3 = MockCommand("third")

        self.history.execute(cmd1)
        self.history.execute(cmd2)
        self.history.undo()  # Adds to redo stack

        self.history.execute(cmd3)  # Should clear redo

        assert len(self.history._redo_stack) == 0

    def test_undo_removes_from_undo_stack(self):
        """Test undo removes from undo stack."""
        cmd = MockCommand("test")
        self.history.execute(cmd)

        self.history.undo()

        assert cmd.undone is True
        assert len(self.history._undo_stack) == 0

    def test_undo_adds_to_redo_stack(self):
        """Test undo adds command to redo stack."""
        cmd = MockCommand("test")
        self.history.execute(cmd)

        self.history.undo()

        assert len(self.history._redo_stack) == 1

    def test_redo_moves_to_undo_stack(self):
        """Test redo moves command back to undo stack."""
        cmd = MockCommand("test")
        self.history.execute(cmd)
        self.history.undo()

        # Reset executed flag
        cmd.executed = False

        self.history.redo()

        assert cmd.executed is True
        assert len(self.history._undo_stack) == 1
        assert len(self.history._redo_stack) == 0

    def test_undo_empty_stack(self):
        """Test undo on empty stack returns None."""
        result = self.history.undo()

        assert result is None

    def test_redo_empty_stack(self):
        """Test redo on empty stack returns None."""
        result = self.history.redo()

        assert result is None

    def test_can_undo(self):
        """Test can_undo property."""
        assert self.history.can_undo() is False

        cmd = MockCommand("test")
        self.history.execute(cmd)

        assert self.history.can_undo() is True

    def test_can_redo(self):
        """Test can_redo property."""
        assert self.history.can_redo() is False

        cmd = MockCommand("test")
        self.history.execute(cmd)
        self.history.undo()

        assert self.history.can_redo() is True

    def test_multiple_undo(self):
        """Test multiple undo operations."""
        cmd1 = MockCommand("first")
        cmd2 = MockCommand("second")
        cmd3 = MockCommand("third")

        self.history.execute(cmd1)
        self.history.execute(cmd2)
        self.history.execute(cmd3)

        self.history.undo()
        self.history.undo()
        self.history.undo()

        assert len(self.history._undo_stack) == 0
        assert len(self.history._redo_stack) == 3

    def test_multiple_redo(self):
        """Test multiple redo operations."""
        cmd1 = MockCommand("first")
        cmd2 = MockCommand("second")

        self.history.execute(cmd1)
        self.history.execute(cmd2)
        self.history.undo()
        self.history.undo()

        self.history.redo()
        self.history.redo()

        assert len(self.history._undo_stack) == 2
        assert len(self.history._redo_stack) == 0

    def test_clear(self):
        """Test clearing history."""
        cmd = MockCommand("test")
        self.history.execute(cmd)
        self.history.undo()

        self.history.clear()

        assert len(self.history._undo_stack) == 0
        assert len(self.history._redo_stack) == 0

    def test_max_history_size(self):
        """Test history respects max size limit."""
        history = EditHistory(max_history=5)

        for i in range(10):
            history.execute(MockCommand(f"cmd_{i}"))

        # Should be limited
        assert len(history._undo_stack) <= 5

    def test_execute_returns_result(self):
        """Test execute returns command result."""
        cmd = MockCommand("test")

        result = self.history.execute(cmd)

        assert result == {"action": "executed", "name": "test"}

    def test_undo_returns_result(self):
        """Test undo returns command description."""
        cmd = MockCommand("test")
        self.history.execute(cmd)

        result = self.history.undo()

        # Implementation returns description, not undo() result
        assert result == "MockCommand: test"

    def test_redo_returns_result(self):
        """Test redo returns command description."""
        cmd = MockCommand("test")
        self.history.execute(cmd)
        self.history.undo()
        cmd.executed = False

        result = self.history.redo()

        # Implementation returns description, not execute() result
        assert result == "MockCommand: test"


class TestEditCommand:
    """Tests for EditCommand base class."""

    def test_command_interface(self):
        """Test command interface is defined."""
        # EditCommand should define execute and undo
        assert hasattr(EditCommand, "execute")
        assert hasattr(EditCommand, "undo")
