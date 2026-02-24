"""
Unit tests for ArtifactRefCounter (Phase 21.3)
"""

import shutil
import tempfile

from backend.services.artifact_ref_counter import ArtifactRefCounter


class TestArtifactRefCounter:
    """Tests for ArtifactRefCounter class."""

    def setup_method(self):
        """Set up test fixtures with isolated temp directory."""
        self._temp_dir = tempfile.mkdtemp(prefix="artifact_ref_test_")
        self.counter = ArtifactRefCounter(data_dir=self._temp_dir)

    def teardown_method(self):
        """Clean up temp directory."""
        shutil.rmtree(self._temp_dir, ignore_errors=True)

    def test_initialization(self):
        """Test counter initializes correctly."""
        # Use the instance created in setup_method (which is isolated)
        assert self.counter is not None
        assert len(self.counter._refs) == 0

    def test_increment_new_artifact(self):
        """Test incrementing a new artifact."""
        self.counter.increment("audio_1", "ref_1")

        count = self.counter.get_count("audio_1")
        assert count == 1

    def test_increment_existing_artifact(self):
        """Test incrementing an existing artifact."""
        self.counter.increment("audio_1", "ref_1")
        self.counter.increment("audio_1", "ref_2")
        self.counter.increment("audio_1", "ref_3")

        count = self.counter.get_count("audio_1")
        assert count == 3

    def test_increment_duplicate_reference(self):
        """Test duplicate reference is not counted twice."""
        self.counter.increment("audio_1", "ref_1")
        self.counter.increment("audio_1", "ref_1")  # Same reference

        count = self.counter.get_count("audio_1")
        assert count == 1

    def test_decrement_removes_reference(self):
        """Test decrement removes a reference."""
        self.counter.increment("audio_1", "ref_1")
        self.counter.increment("audio_1", "ref_2")

        self.counter.decrement("audio_1", "ref_1")

        count = self.counter.get_count("audio_1")
        assert count == 1

    def test_decrement_nonexistent_reference(self):
        """Test decrement on non-existent reference is safe."""
        self.counter.decrement("audio_1", "nonexistent")

        count = self.counter.get_count("audio_1")
        assert count == 0

    def test_get_count_nonexistent(self):
        """Test get_count on non-existent artifact returns 0."""
        count = self.counter.get_count("nonexistent")

        assert count == 0

    def test_get_zero_ref_artifacts(self):
        """Test getting artifacts with zero references.

        Note: In current implementation, artifacts are deleted when count
        reaches zero, so get_zero_ref_artifacts returns empty unless there's
        a race condition or the artifact was added with zero refs initially.
        """
        self.counter.increment("audio_1", "ref_1")
        self.counter.increment("audio_2", "ref_2")
        self.counter.increment("audio_3", "ref_3")

        # Decrement all references from audio_2
        self.counter.decrement("audio_2", "ref_2")

        # After decrement to zero, artifact is deleted from tracking
        assert self.counter.get_count("audio_2") == 0
        assert self.counter.get_count("audio_1") == 1
        assert self.counter.get_count("audio_3") == 1

    def test_get_zero_ref_artifacts_empty(self):
        """Test get_zero_ref_artifacts with no zero-ref artifacts."""
        self.counter.increment("audio_1", "ref_1")
        self.counter.increment("audio_2", "ref_2")

        candidates = self.counter.get_zero_ref_artifacts()

        assert len(candidates) == 0

    def test_multiple_artifacts_independent(self):
        """Test multiple artifacts are tracked independently."""
        self.counter.increment("audio_1", "clip_1")
        self.counter.increment("audio_1", "clip_2")
        self.counter.increment("audio_2", "clip_3")

        assert self.counter.get_count("audio_1") == 2
        assert self.counter.get_count("audio_2") == 1

    def test_decrement_to_zero_removes_artifact(self):
        """Test decrement to zero removes artifact from tracking."""
        self.counter.increment("audio_1", "ref_1")
        self.counter.decrement("audio_1", "ref_1")

        # Artifact should be fully removed when count reaches zero
        assert self.counter.get_count("audio_1") == 0
        assert "audio_1" not in self.counter.get_all_tracked()

    def test_re_increment_after_zero(self):
        """Test re-incrementing after zero restores tracking."""
        self.counter.increment("audio_1", "ref_1")
        self.counter.decrement("audio_1", "ref_1")
        self.counter.increment("audio_1", "ref_2")

        # Artifact should be tracked again with count 1
        assert self.counter.get_count("audio_1") == 1
        assert "audio_1" in self.counter.get_all_tracked()

    def test_get_references(self):
        """Test getting all references for an artifact."""
        self.counter.increment("audio_1", "clip_1")
        self.counter.increment("audio_1", "clip_2")
        self.counter.increment("audio_1", "clip_3")

        refs = self.counter.get_references("audio_1")

        assert "clip_1" in refs
        assert "clip_2" in refs
        assert "clip_3" in refs
        assert len(refs) == 3

    def test_get_references_nonexistent(self):
        """Test get_references on non-existent artifact."""
        refs = self.counter.get_references("nonexistent")

        assert refs == set() or refs == []

    def test_clear_artifact(self):
        """Test clearing all references for an artifact."""
        self.counter.increment("audio_1", "ref_1")
        self.counter.increment("audio_1", "ref_2")

        self.counter.clear_artifact("audio_1")

        assert self.counter.get_count("audio_1") == 0

    def test_clear_artifact_nonexistent(self):
        """Test clearing non-existent artifact is safe."""
        # Should not raise
        self.counter.clear_artifact("nonexistent")

        assert self.counter.get_count("nonexistent") == 0
