"""
Unit tests for Response Cache (API Response Caching System)

Tests cover:
- LRU cache with TTL
- Cache key generation
- Cache hit/miss statistics
- Cache eviction
- Cache invalidation
- Middleware functionality
- Cache decorator
"""

import time
from collections import OrderedDict
from unittest.mock import MagicMock

import pytest

# Try to import the response cache module
try:
    from backend.api.response_cache import (
        ResponseCache,
        cache_response,
        get_response_cache,
        response_cache_middleware,
        set_response_cache,
    )

    HAS_RESPONSE_CACHE = True
except ImportError:
    HAS_RESPONSE_CACHE = False

pytestmark = pytest.mark.skipif(
    not HAS_RESPONSE_CACHE,
    reason="Response cache not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def response_cache():
    """Create a ResponseCache instance for testing."""
    if not HAS_RESPONSE_CACHE:
        pytest.skip("Response cache not available")

    cache = ResponseCache(max_size=100, default_ttl=300, cleanup_interval=60)
    yield cache
    cache.clear()


@pytest.fixture
def mock_request():
    """Create a mock FastAPI request."""
    request = MagicMock()
    request.method = "GET"
    request.url.path = "/api/test"
    request.query_params = {}
    request.headers = {}
    return request


class TestResponseCacheImports:
    """Test that ResponseCache can be imported."""

    def test_import_cache_class(self):
        """Test that ResponseCache can be imported."""
        if not HAS_RESPONSE_CACHE:
            pytest.skip("Response cache not available")
        from backend.api.response_cache import ResponseCache

        assert ResponseCache is not None

    def test_import_functions(self):
        """Test that cache functions can be imported."""
        if not HAS_RESPONSE_CACHE:
            pytest.skip("Response cache not available")
        from backend.api.response_cache import (
            get_response_cache,
            set_response_cache,
        )

        assert get_response_cache is not None
        assert set_response_cache is not None


class TestResponseCacheStructure:
    """Test ResponseCache class structure and basic functionality."""

    def test_cache_initialization(self, response_cache):
        """Test that cache initializes correctly."""
        assert response_cache is not None
        assert response_cache.max_size == 100
        assert response_cache.default_ttl == 300
        assert response_cache.cleanup_interval == 60

    def test_cache_storage(self, response_cache):
        """Test that cache has OrderedDict storage."""
        assert hasattr(response_cache, "_cache")
        assert isinstance(response_cache._cache, OrderedDict)
        assert len(response_cache._cache) == 0

    def test_cache_statistics(self, response_cache):
        """Test that cache has statistics tracking."""
        assert hasattr(response_cache, "_hits")
        assert hasattr(response_cache, "_misses")
        assert hasattr(response_cache, "_evictions")
        assert response_cache._hits == 0
        assert response_cache._misses == 0
        assert response_cache._evictions == 0


class TestResponseCacheLRU:
    """Test LRU cache functionality."""

    def test_cache_set_and_get(self, response_cache):
        """Test setting and getting from cache."""
        cache_key = "test_key"
        response_data = {"data": "test"}

        response_cache.set(cache_key, response_data)
        result = response_cache.get(cache_key)

        assert result == response_data
        assert response_cache._hits == 1

    def test_cache_miss(self, response_cache):
        """Test cache miss behavior."""
        result = response_cache.get("nonexistent_key")
        assert result is None
        assert response_cache._misses == 1

    def test_lru_eviction(self, response_cache):
        """Test LRU eviction when cache is full."""
        response_cache.max_size = 3

        # Fill cache to max size
        for i in range(3):
            response_cache.set(f"key{i}", {"data": f"value{i}"})

        assert len(response_cache._cache) == 3

        # Add one more - should evict oldest
        response_cache.set("key3", {"data": "value3"})

        # Check that oldest was evicted
        assert len(response_cache._cache) == 3
        assert "key0" not in response_cache._cache
        assert "key3" in response_cache._cache
        assert response_cache._evictions == 1

    def test_lru_update_on_get(self, response_cache):
        """Test that LRU is updated when getting from cache."""
        response_cache.max_size = 3

        # Fill cache
        for i in range(3):
            response_cache.set(f"key{i}", {"data": f"value{i}"})

        # Access first key (should move to end)
        response_cache.get("key0")

        # Add new key - should evict key1 (not key0)
        response_cache.set("key3", {"data": "value3"})

        assert "key0" in response_cache._cache
        assert "key1" not in response_cache._cache
        assert "key3" in response_cache._cache


class TestResponseCacheTTL:
    """Test TTL (Time To Live) functionality."""

    def test_ttl_expiration(self, response_cache):
        """Test that entries expire after TTL."""
        cache_key = "test_key"
        response_data = {"data": "test"}

        # Set with short TTL
        response_cache.set(cache_key, response_data, ttl=1)

        # Should be available immediately
        result = response_cache.get(cache_key)
        assert result == response_data

        # Wait for expiration
        time.sleep(1.1)

        # Should be expired
        result = response_cache.get(cache_key)
        assert result is None
        assert response_cache._misses == 1

    def test_default_ttl(self, response_cache):
        """Test that default TTL is used when not specified."""
        cache_key = "test_key"
        response_data = {"data": "test"}

        response_cache.set(cache_key, response_data)

        # Check that default TTL was used
        # Cache stores: (response_data, timestamp, ttl, size_bytes, tags)
        _, _timestamp, ttl, _, _ = response_cache._cache[cache_key]
        assert ttl == response_cache.default_ttl

    def test_custom_ttl(self, response_cache):
        """Test that custom TTL can be set."""
        cache_key = "test_key"
        response_data = {"data": "test"}
        custom_ttl = 600

        response_cache.set(cache_key, response_data, ttl=custom_ttl)

        # Check that custom TTL was used
        # Cache stores: (response_data, timestamp, ttl, size_bytes, tags)
        _, _timestamp, ttl, _, _ = response_cache._cache[cache_key]
        assert ttl == custom_ttl


class TestResponseCacheCleanup:
    """Test cache cleanup functionality."""

    def test_cleanup_expired_entries(self, response_cache):
        """Test that expired entries are cleaned up."""
        # Set entries with short TTL
        for i in range(3):
            response_cache.set(f"key{i}", {"data": f"value{i}"}, ttl=1)

        assert len(response_cache._cache) == 3

        # Wait for expiration
        time.sleep(1.1)

        # Force cleanup by setting cleanup interval to 0 and calling cleanup
        response_cache.cleanup_interval = 0
        response_cache._last_cleanup = 0
        response_cache._cleanup_if_needed()

        # All should be expired and removed
        assert len(response_cache._cache) == 0

    def test_cleanup_interval(self, response_cache):
        """Test that cleanup respects cleanup interval."""
        response_cache.cleanup_interval = 2

        # Set entry
        response_cache.set("key1", {"data": "value1"}, ttl=1)

        # Wait for expiration but not cleanup interval
        time.sleep(1.1)

        # Manually trigger cleanup check
        response_cache._cleanup_if_needed()

        # Should still be there (cleanup interval not reached)
        # But get should return None due to expiration
        result = response_cache.get("key1")
        assert result is None


class TestResponseCacheInvalidation:
    """Test cache invalidation functionality."""

    def test_invalidate_all(self, response_cache):
        """Test invalidating all cache entries."""
        # Add entries
        for i in range(5):
            response_cache.set(f"key{i}", {"data": f"value{i}"})

        assert len(response_cache._cache) == 5

        # Invalidate all
        count = response_cache.invalidate()

        assert count == 5
        assert len(response_cache._cache) == 0

    def test_invalidate_pattern(self, response_cache):
        """Test invalidating entries by pattern."""
        # Add entries with different patterns
        response_cache.set("api_profiles_key1", {"data": "value1"})
        response_cache.set("api_profiles_key2", {"data": "value2"})
        response_cache.set("api_voice_key1", {"data": "value3"})

        assert len(response_cache._cache) == 3

        # Invalidate only profiles entries
        count = response_cache.invalidate("api_profiles")

        assert count == 2
        assert len(response_cache._cache) == 1
        assert "api_voice_key1" in response_cache._cache


class TestResponseCacheStatistics:
    """Test cache statistics functionality."""

    def test_get_stats(self, response_cache):
        """Test getting cache statistics."""
        # Add some entries
        response_cache.set("key1", {"data": "value1"})
        response_cache.set("key2", {"data": "value2"})

        # Get some hits
        response_cache.get("key1")
        response_cache.get("key1")

        # Get a miss
        response_cache.get("nonexistent")

        stats = response_cache.get_stats()

        assert isinstance(stats, dict)
        assert "size" in stats
        assert "max_size" in stats
        assert "hits" in stats
        assert "misses" in stats
        assert "hit_rate" in stats
        assert "evictions" in stats
        assert stats["size"] == 2
        assert stats["hits"] == 2
        assert stats["misses"] == 1

    def test_hit_rate_calculation(self, response_cache):
        """Test hit rate calculation."""
        # Add entry
        response_cache.set("key1", {"data": "value1"})

        # Get 3 hits
        for _ in range(3):
            response_cache.get("key1")

        # Get 1 miss
        response_cache.get("nonexistent")

        stats = response_cache.get_stats()
        assert stats["hit_rate"] == "75.00%"

    def test_clear_statistics(self, response_cache):
        """Test clearing cache and statistics."""
        # Add entries and generate stats
        response_cache.set("key1", {"data": "value1"})
        response_cache.get("key1")
        response_cache.get("nonexistent")

        assert response_cache._hits == 1
        assert response_cache._misses == 1
        assert len(response_cache._cache) == 1

        # Clear
        response_cache.clear()

        assert response_cache._hits == 0
        assert response_cache._misses == 0
        assert response_cache._evictions == 0
        assert len(response_cache._cache) == 0


class TestResponseCacheKeyGeneration:
    """Test cache key generation."""

    def test_generate_cache_key(self, response_cache, mock_request):
        """Test cache key generation from request."""
        key1 = response_cache._generate_cache_key(mock_request)
        key2 = response_cache._generate_cache_key(mock_request)

        # Same request should generate same key
        assert key1 == key2
        assert isinstance(key1, str)
        assert len(key1) > 0

    def test_cache_key_with_query_params(self, response_cache, mock_request):
        """Test cache key includes query parameters."""
        mock_request.query_params = {"param1": "value1", "param2": "value2"}

        key1 = response_cache._generate_cache_key(mock_request, include_query=True)
        key2 = response_cache._generate_cache_key(mock_request, include_query=True)

        assert key1 == key2

    def test_cache_key_without_query_params(self, response_cache, mock_request):
        """Test cache key without query parameters."""
        mock_request.query_params = {"param1": "value1"}

        key_with = response_cache._generate_cache_key(mock_request, include_query=True)
        key_without = response_cache._generate_cache_key(mock_request, include_query=False)

        # Should be different
        assert key_with != key_without


class TestResponseCacheGlobalFunctions:
    """Test global cache functions."""

    def test_get_response_cache(self):
        """Test getting global cache instance."""
        if not HAS_RESPONSE_CACHE:
            pytest.skip("Response cache not available")

        cache = get_response_cache()
        assert cache is not None
        assert isinstance(cache, ResponseCache)

    def test_set_response_cache(self):
        """Test setting global cache instance."""
        if not HAS_RESPONSE_CACHE:
            pytest.skip("Response cache not available")

        new_cache = ResponseCache(max_size=50)
        set_response_cache(new_cache)

        cache = get_response_cache()
        assert cache is new_cache
        assert cache.max_size == 50


class TestResponseCacheOptimization:
    """Test optimization features."""

    def test_lru_caching_optimization(self, response_cache):
        """Test that LRU caching is working."""
        assert isinstance(response_cache._cache, OrderedDict)
        assert response_cache.max_size > 0

    def test_ttl_optimization(self, response_cache):
        """Test that TTL is configured."""
        assert response_cache.default_ttl > 0
        assert response_cache.cleanup_interval > 0

    def test_statistics_tracking(self, response_cache):
        """Test that statistics are tracked."""
        assert hasattr(response_cache, "_hits")
        assert hasattr(response_cache, "_misses")
        assert hasattr(response_cache, "_evictions")
