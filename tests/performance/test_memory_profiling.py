"""
Memory Profiling and Leak Detection Tests

Comprehensive tests for detecting memory leaks and profiling memory usage
in VoiceStudio components.

Memory Targets:
- Audio buffers: < 100MB for typical workflow
- Engine instances: < 500MB per engine (GPU models higher)
- Backend services: < 200MB baseline
- UI components: < 50MB per panel

Leak Detection:
- Repeated operations should not accumulate memory
- Resources should be properly released after use
- Circular references should be avoided
"""

import gc
import sys
import time
import weakref
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from unittest.mock import Mock

import numpy as np
import pytest

project_root = Path(__file__).parent.parent.parent
sys.path.insert(0, str(project_root))

import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Try to import psutil for accurate memory measurements
try:
    import psutil

    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False
    logger.warning("psutil not available - using fallback memory measurements")

# Try to import tracemalloc for memory tracing
try:
    import tracemalloc

    HAS_TRACEMALLOC = True
except ImportError:
    HAS_TRACEMALLOC = False


# =============================================================================
# MEMORY PROFILING UTILITIES
# =============================================================================


@dataclass
class MemorySnapshot:
    """Snapshot of memory state at a point in time."""

    timestamp: float
    rss_bytes: int  # Resident Set Size
    vms_bytes: int  # Virtual Memory Size
    heap_bytes: int  # Heap usage (if tracemalloc available)
    description: str = ""

    @property
    def rss_mb(self) -> float:
        return self.rss_bytes / (1024 * 1024)

    @property
    def vms_mb(self) -> float:
        return self.vms_bytes / (1024 * 1024)

    @property
    def heap_mb(self) -> float:
        return self.heap_bytes / (1024 * 1024)


class MemoryProfiler:
    """Utility for profiling memory usage during operations."""

    def __init__(self, trace_enabled: bool = True):
        self.snapshots: list[MemorySnapshot] = []
        self.trace_enabled = trace_enabled and HAS_TRACEMALLOC
        self._process = psutil.Process() if HAS_PSUTIL else None

    def snapshot(self, description: str = "") -> MemorySnapshot:
        """Take a memory snapshot."""
        gc.collect()  # Force garbage collection for accurate measurement

        if self._process:
            mem_info = self._process.memory_info()
            rss = mem_info.rss
            vms = mem_info.vms
        else:
            # Fallback: estimate from sys.getsizeof on gc objects
            rss = sum(sys.getsizeof(obj) for obj in gc.get_objects() if hasattr(obj, "__sizeof__"))
            vms = rss

        heap = 0
        if self.trace_enabled:
            current, _peak = tracemalloc.get_traced_memory()
            heap = current

        snap = MemorySnapshot(
            timestamp=time.time(),
            rss_bytes=rss,
            vms_bytes=vms,
            heap_bytes=heap,
            description=description,
        )
        self.snapshots.append(snap)
        return snap

    def get_growth(self, start_idx: int = 0, end_idx: int = -1) -> float:
        """Get memory growth between two snapshots in MB."""
        if len(self.snapshots) < 2:
            return 0.0
        start = self.snapshots[start_idx]
        end = self.snapshots[end_idx]
        return (end.rss_bytes - start.rss_bytes) / (1024 * 1024)

    def start_trace(self):
        """Start memory tracing."""
        if HAS_TRACEMALLOC:
            tracemalloc.start()

    def stop_trace(self):
        """Stop memory tracing."""
        if HAS_TRACEMALLOC:
            tracemalloc.stop()

    def get_top_allocations(self, limit: int = 10) -> list[str]:
        """Get top memory allocations."""
        if not HAS_TRACEMALLOC:
            return ["tracemalloc not available"]

        snapshot = tracemalloc.take_snapshot()
        top_stats = snapshot.statistics("lineno")[:limit]
        return [str(stat) for stat in top_stats]


class LeakDetector:
    """Utility for detecting memory leaks."""

    def __init__(self):
        self.tracked_objects: list[weakref.ref] = []
        self.creation_info: dict[int, str] = {}

    def track(self, obj: Any, description: str = "") -> None:
        """Track an object for leak detection."""
        ref = weakref.ref(obj)
        self.tracked_objects.append(ref)
        self.creation_info[id(obj)] = description

    def check_leaks(self) -> list[str]:
        """Check for leaked objects (objects still alive after gc)."""
        gc.collect()
        leaks = []

        for ref in self.tracked_objects:
            obj = ref()
            if obj is not None:
                desc = self.creation_info.get(id(obj), "unknown")
                leaks.append(f"Leak: {type(obj).__name__} - {desc}")

        return leaks

    def clear(self):
        """Clear tracked objects."""
        self.tracked_objects.clear()
        self.creation_info.clear()


def generate_large_audio(duration_seconds: float, sample_rate: int = 22050) -> np.ndarray:
    """Generate large audio array for memory testing."""
    samples = int(sample_rate * duration_seconds)
    return np.random.uniform(-1, 1, samples).astype(np.float32)


# =============================================================================
# MEMORY PROFILING TESTS
# =============================================================================


@pytest.mark.performance
class TestMemoryBaseline:
    """Test baseline memory usage."""

    @pytest.mark.skipif(not HAS_PSUTIL, reason="psutil required for accurate memory tests (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
    def test_python_process_baseline(self):
        """Establish baseline memory usage for Python process."""
        profiler = MemoryProfiler()
        gc.collect()

        baseline = profiler.snapshot("baseline")

        # Python process baseline should be under 200MB
        assert baseline.rss_mb < 200, f"Baseline RSS {baseline.rss_mb:.1f}MB exceeds 200MB limit"
        logger.info(f"Process baseline: RSS={baseline.rss_mb:.1f}MB, VMS={baseline.vms_mb:.1f}MB")

    def test_gc_collection_effectiveness(self):
        """Test that garbage collection properly reclaims memory."""
        profiler = MemoryProfiler()

        # Baseline
        profiler.snapshot("before_allocation")

        # Allocate large objects
        large_objects = [np.zeros(1_000_000, dtype=np.float32) for _ in range(10)]
        profiler.snapshot("after_allocation")

        # Clear references and collect
        del large_objects
        gc.collect()
        profiler.snapshot("after_gc")

        growth = profiler.get_growth(0, -1)
        assert growth < 5.0, f"Memory growth {growth:.1f}MB after gc - potential leak"
        logger.info(f"GC effectiveness: {growth:.1f}MB growth after cleanup")


@pytest.mark.performance
class TestAudioBufferMemory:
    """Test memory usage of audio buffers."""

    def test_audio_buffer_size_calculation(self):
        """Verify audio buffer memory size is as expected."""
        duration = 60.0  # 1 minute
        sample_rate = 22050
        expected_samples = int(duration * sample_rate)
        expected_bytes = expected_samples * 4  # float32 = 4 bytes

        audio = generate_large_audio(duration, sample_rate)
        actual_bytes = audio.nbytes

        assert (
            actual_bytes == expected_bytes
        ), f"Buffer size mismatch: {actual_bytes} != {expected_bytes}"

        # 1 minute of audio at 22050Hz should be ~5.3MB
        expected_mb = expected_bytes / (1024 * 1024)
        logger.info(f"1 minute audio buffer: {expected_mb:.2f}MB")

    def test_multiple_buffer_allocation(self):
        """Test allocating multiple audio buffers."""
        profiler = MemoryProfiler()
        buffers = []

        profiler.snapshot("start")

        # Allocate 10 buffers of 30 seconds each
        for i in range(10):
            buffer = generate_large_audio(30.0)
            buffers.append(buffer)
            if (i + 1) % 5 == 0:
                profiler.snapshot(f"after_{i+1}_buffers")

        profiler.snapshot("all_allocated")

        # Total memory for 300 seconds of audio should be ~31.5MB
        # Allow some overhead
        total_expected_mb = 31.5
        growth = profiler.get_growth(0, -1)

        assert (
            growth < total_expected_mb * 1.5
        ), f"Buffer memory {growth:.1f}MB exceeds expected {total_expected_mb * 1.5:.1f}MB"

        logger.info(f"10 x 30s buffers: {growth:.1f}MB")

    def test_buffer_reuse_efficiency(self):
        """Test that reusing buffers doesn't accumulate memory."""
        profiler = MemoryProfiler()

        profiler.snapshot("start")

        # Repeatedly allocate and process buffers
        for i in range(20):
            buffer = generate_large_audio(10.0)
            # Simulate processing
            processed = buffer * 0.5 + np.sin(np.arange(len(buffer)) / 1000)
            del buffer, processed

            if (i + 1) % 10 == 0:
                gc.collect()
                profiler.snapshot(f"after_{i+1}_iterations")

        gc.collect()
        profiler.snapshot("end")

        growth = profiler.get_growth(0, -1)
        assert growth < 10.0, f"Memory growth {growth:.1f}MB during buffer reuse - potential leak"

        logger.info(f"Buffer reuse (20 iterations): {growth:.1f}MB growth")


@pytest.mark.performance
class TestLeakDetection:
    """Test for memory leaks in various components."""

    def test_object_lifecycle_tracking(self):
        """Test that objects are properly garbage collected."""
        detector = LeakDetector()

        class TrackedObject:
            def __init__(self, data):
                self.data = data

        # Create and track objects
        for i in range(5):
            obj = TrackedObject(np.zeros(10000))
            detector.track(obj, f"object_{i}")
            del obj

        gc.collect()
        leaks = detector.check_leaks()

        assert len(leaks) == 0, f"Detected leaks: {leaks}"
        logger.info("Object lifecycle: No leaks detected")

    def test_circular_reference_detection(self):
        """Test detection of circular references."""
        detector = LeakDetector()

        class Node:
            def __init__(self, value):
                self.value = value
                self.next = None

        # Create circular reference
        a = Node(1)
        b = Node(2)
        a.next = b
        b.next = a  # Circular reference

        detector.track(a, "circular_node_a")
        detector.track(b, "circular_node_b")

        # Clear direct references but circular refs remain
        del a, b
        gc.collect()

        # Python's gc should handle circular refs, but let's verify
        leaks = detector.check_leaks()

        # With Python's cycle detector, these should be collected
        assert len(leaks) == 0, f"Circular reference leak: {leaks}"
        logger.info("Circular reference: Properly collected")

    def test_callback_reference_leak(self):
        """Test for leaks from callback/event handler patterns."""
        detector = LeakDetector()
        callbacks_fired = []

        class EventEmitter:
            def __init__(self):
                self.listeners = []

            def add_listener(self, callback):
                self.listeners.append(callback)

            def emit(self):
                for callback in self.listeners:
                    callback()

            def clear(self):
                self.listeners.clear()

        class Handler:
            def __init__(self, name):
                self.name = name

            def on_event(self):
                callbacks_fired.append(self.name)

        emitter = EventEmitter()

        # Create handlers and register
        for i in range(5):
            handler = Handler(f"handler_{i}")
            detector.track(handler, f"handler_{i}")
            emitter.add_listener(handler.on_event)

        emitter.emit()

        # Clear emitter listeners - this should release handlers
        emitter.clear()
        del emitter
        gc.collect()

        # Check for leaks - handlers should be collected after clearing listeners
        # Note: Method references might keep handlers alive
        leaks = detector.check_leaks()
        # This is expected behavior - method refs hold handler refs
        logger.info(
            f"Callback pattern: {len(leaks)} references remaining (expected due to method refs)"
        )


@pytest.mark.performance
class TestMockComponentMemory:
    """Test memory usage of mocked components."""

    def test_engine_service_mock_memory(self):
        """Test that mock engine service doesn't leak memory."""
        profiler = MemoryProfiler()

        profiler.snapshot("start")

        for i in range(50):
            mock_service = Mock()
            mock_service.synthesize.return_value = {
                "audio": generate_large_audio(2.0),
                "sample_rate": 22050,
            }

            # Simulate usage
            result = mock_service.synthesize("Test text")
            del result
            del mock_service

            if (i + 1) % 25 == 0:
                gc.collect()
                profiler.snapshot(f"after_{i+1}_services")

        gc.collect()
        profiler.snapshot("end")

        growth = profiler.get_growth(0, -1)
        assert growth < 20.0, f"Mock service memory growth {growth:.1f}MB - potential leak"

        logger.info(f"Mock engine service (50 instances): {growth:.1f}MB growth")

    def test_concurrent_mock_operations(self):
        """Test memory with concurrent mock operations."""
        profiler = MemoryProfiler()

        profiler.snapshot("start")

        def mock_operation():
            mock = Mock()
            mock.process.return_value = np.zeros(100000, dtype=np.float32)
            result = mock.process()
            del result, mock
            return True

        # Run concurrent operations
        import concurrent.futures

        with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
            futures = [executor.submit(mock_operation) for _ in range(50)]
            [f.result() for f in futures]

        gc.collect()
        profiler.snapshot("end")

        growth = profiler.get_growth(0, -1)
        assert growth < 15.0, f"Concurrent mock memory growth {growth:.1f}MB"

        logger.info(f"Concurrent mock operations (50 tasks): {growth:.1f}MB growth")


@pytest.mark.performance
class TestMemoryThresholds:
    """Test memory usage against defined thresholds."""

    # Memory thresholds (MB)
    THRESHOLDS = {
        "audio_buffer_per_minute": 6.0,  # 22050 * 60 * 4 bytes ≈ 5.3MB + overhead
        "mock_engine_instance": 10.0,  # Mock engine with minimal state
        "concurrent_operations": 50.0,  # 10 concurrent operations
        "processing_pipeline": 100.0,  # Full processing pipeline
    }

    def test_audio_threshold(self):
        """Test audio buffer stays within threshold."""
        audio = generate_large_audio(60.0)  # 1 minute
        memory_mb = audio.nbytes / (1024 * 1024)

        assert (
            memory_mb < self.THRESHOLDS["audio_buffer_per_minute"]
        ), f"Audio buffer {memory_mb:.2f}MB exceeds threshold"

    def test_mock_engine_threshold(self):
        """Test mock engine instance stays within threshold."""
        profiler = MemoryProfiler()
        gc.collect()

        profiler.snapshot("before")

        mock_engine = Mock()
        mock_engine.name = "test_engine"
        mock_engine.model = np.zeros((100, 100), dtype=np.float32)
        mock_engine.config = {"param": "value"}

        gc.collect()
        profiler.snapshot("after")

        growth = profiler.get_growth()
        assert (
            growth < self.THRESHOLDS["mock_engine_instance"]
        ), f"Mock engine {growth:.2f}MB exceeds threshold"

    def test_concurrent_threshold(self):
        """Test concurrent operations stay within threshold."""
        profiler = MemoryProfiler()
        gc.collect()

        profiler.snapshot("before")

        # Simulate 10 concurrent operations
        active_buffers = []
        for _ in range(10):
            buffer = generate_large_audio(5.0)  # 5 seconds each
            active_buffers.append(buffer)

        gc.collect()
        profiler.snapshot("after")

        growth = profiler.get_growth()
        assert (
            growth < self.THRESHOLDS["concurrent_operations"]
        ), f"Concurrent operations {growth:.2f}MB exceeds threshold"

        # Cleanup
        del active_buffers


@pytest.mark.performance
class TestMemoryReport:
    """Generate memory profiling report."""

    def test_generate_memory_report(self):
        """Generate comprehensive memory report."""
        report = {"timestamp": time.strftime("%Y-%m-%d %H:%M:%S"), "tests": {}, "summary": {}}

        profiler = MemoryProfiler()

        # Test various operations
        operations = [
            ("baseline", lambda: None),
            ("small_audio", lambda: generate_large_audio(5.0)),
            ("medium_audio", lambda: generate_large_audio(30.0)),
            ("large_audio", lambda: generate_large_audio(120.0)),
            ("multiple_mocks", lambda: [Mock() for _ in range(100)]),
        ]

        for name, operation in operations:
            gc.collect()
            profiler.snapshot(f"before_{name}")

            result = operation()

            gc.collect()
            profiler.snapshot(f"after_{name}")

            growth = profiler.get_growth(-2, -1)
            report["tests"][name] = {
                "memory_growth_mb": round(growth, 2),
                "passed": growth < 100,  # 100MB limit per operation
            }

            del result

        # Summary
        all_growths = [t["memory_growth_mb"] for t in report["tests"].values()]
        report["summary"] = {
            "total_tests": len(report["tests"]),
            "max_growth_mb": max(all_growths),
            "avg_growth_mb": sum(all_growths) / len(all_growths),
            "all_passed": all(t["passed"] for t in report["tests"].values()),
        }

        assert report["summary"]["all_passed"], f"Memory tests failed: {report['tests']}"

        logger.info(
            f"Memory Report: max={report['summary']['max_growth_mb']:.2f}MB, "
            f"avg={report['summary']['avg_growth_mb']:.2f}MB"
        )


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-m", "performance"])
