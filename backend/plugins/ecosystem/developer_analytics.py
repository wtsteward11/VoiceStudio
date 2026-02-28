"""
Developer Analytics for Plugin Ecosystem.

Phase 6D: Provides analytics and insights for plugin developers.
All analytics are computed locally with no cloud dependencies.

Features:
1. Plugin performance metrics (installs, usage, errors)
2. User engagement analytics
3. Trend analysis
4. Comparative benchmarking
5. Developer dashboard data

Usage:
    analytics = DeveloperAnalytics(data_store)

    # Record event
    analytics.record_event("plugin-id", "install", user_id="user-123")

    # Get plugin stats
    stats = analytics.get_plugin_stats("plugin-id")

    # Get developer dashboard data
    dashboard = analytics.get_developer_dashboard("developer-id")
"""

from __future__ import annotations

import logging
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from typing import Any, Dict, List, Optional

logger = logging.getLogger(__name__)


class EventType(Enum):
    """Types of analytics events."""

    VIEW = "view"
    INSTALL = "install"
    UNINSTALL = "uninstall"
    ACTIVATE = "activate"
    DEACTIVATE = "deactivate"
    USE = "use"
    ERROR = "error"
    CRASH = "crash"
    RATE = "rate"
    REVIEW = "review"


@dataclass
class AnalyticsEvent:
    """A single analytics event."""

    plugin_id: str
    event_type: EventType
    timestamp: datetime = field(default_factory=datetime.utcnow)
    user_id: Optional[str] = None
    session_id: Optional[str] = None
    metadata: Dict[str, Any] = field(default_factory=dict)


@dataclass
class PluginMetrics:
    """Aggregated metrics for a plugin."""

    plugin_id: str
    total_installs: int = 0
    active_installs: int = 0
    total_views: int = 0
    total_uses: int = 0
    total_errors: int = 0
    total_crashes: int = 0
    avg_rating: float = 0.0
    rating_count: int = 0
    review_count: int = 0
    daily_active_users: int = 0
    weekly_active_users: int = 0
    monthly_active_users: int = 0
    retention_rate_7d: float = 0.0
    retention_rate_30d: float = 0.0
    error_rate: float = 0.0
    crash_rate: float = 0.0
    last_updated: datetime = field(default_factory=datetime.utcnow)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "plugin_id": self.plugin_id,
            "total_installs": self.total_installs,
            "active_installs": self.active_installs,
            "total_views": self.total_views,
            "total_uses": self.total_uses,
            "total_errors": self.total_errors,
            "avg_rating": round(self.avg_rating, 2),
            "rating_count": self.rating_count,
            "daily_active_users": self.daily_active_users,
            "weekly_active_users": self.weekly_active_users,
            "monthly_active_users": self.monthly_active_users,
            "retention_rate_7d": round(self.retention_rate_7d, 2),
            "retention_rate_30d": round(self.retention_rate_30d, 2),
            "error_rate": round(self.error_rate, 4),
            "crash_rate": round(self.crash_rate, 4),
            "last_updated": self.last_updated.isoformat(),
        }


@dataclass
class DeveloperStats:
    """Aggregated stats for a developer."""

    developer_id: str
    total_plugins: int = 0
    total_installs: int = 0
    total_active_users: int = 0
    avg_rating: float = 0.0
    total_reviews: int = 0
    plugins: List[PluginMetrics] = field(default_factory=list)
    trending_plugins: List[str] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "developer_id": self.developer_id,
            "total_plugins": self.total_plugins,
            "total_installs": self.total_installs,
            "total_active_users": self.total_active_users,
            "avg_rating": round(self.avg_rating, 2),
            "total_reviews": self.total_reviews,
            "plugins": [p.to_dict() for p in self.plugins],
            "trending_plugins": self.trending_plugins,
        }


@dataclass
class TrendData:
    """Time-series trend data."""

    dates: List[str]
    values: List[float]
    metric_name: str

    def to_dict(self) -> Dict[str, Any]:
        return {
            "metric_name": self.metric_name,
            "dates": self.dates,
            "values": self.values,
        }


class DeveloperAnalytics:
    """
    Developer analytics engine.

    Provides comprehensive analytics for plugin developers.
    All computations are performed locally.

    Example:
        analytics = DeveloperAnalytics()

        # Record events
        analytics.record_event("my-plugin", EventType.INSTALL, user_id="user-1")
        analytics.record_event("my-plugin", EventType.USE, user_id="user-1")

        # Get metrics
        metrics = analytics.get_plugin_metrics("my-plugin")
        print(f"Installs: {metrics.total_installs}")

        # Get trends
        trend = analytics.get_trend("my-plugin", "installs", days=30)
    """

    def __init__(
        self,
        retention_days: int = 90,
        aggregation_interval_minutes: int = 60,
    ):
        """
        Initialize analytics engine.

        Args:
            retention_days: Days to retain raw events
            aggregation_interval_minutes: Interval for aggregation
        """
        self._retention_days = retention_days
        self._aggregation_interval = aggregation_interval_minutes

        # Raw events (in production, would be stored in database)
        self._events: List[AnalyticsEvent] = []

        # Aggregated metrics by plugin
        self._metrics: Dict[str, PluginMetrics] = {}

        # Developer -> plugin mapping
        self._developer_plugins: Dict[str, List[str]] = defaultdict(list)

        # Rating data
        self._ratings: Dict[str, List[float]] = defaultdict(list)

        # User activity tracking
        self._user_activity: Dict[str, Dict[str, datetime]] = defaultdict(dict)

    def register_plugin(self, plugin_id: str, developer_id: str):
        """Register a plugin with its developer."""
        if plugin_id not in self._developer_plugins[developer_id]:
            self._developer_plugins[developer_id].append(plugin_id)

        if plugin_id not in self._metrics:
            self._metrics[plugin_id] = PluginMetrics(plugin_id=plugin_id)

    def record_event(
        self,
        plugin_id: str,
        event_type: EventType,
        user_id: Optional[str] = None,
        session_id: Optional[str] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ):
        """
        Record an analytics event.

        Args:
            plugin_id: Plugin identifier
            event_type: Type of event
            user_id: Optional user identifier
            session_id: Optional session identifier
            metadata: Optional additional data
        """
        event = AnalyticsEvent(
            plugin_id=plugin_id,
            event_type=event_type,
            user_id=user_id,
            session_id=session_id,
            metadata=metadata or {},
        )
        self._events.append(event)

        # Update aggregated metrics
        self._update_metrics(event)

        # Update user activity tracking
        if user_id:
            self._user_activity[plugin_id][user_id] = event.timestamp

    def record_rating(
        self,
        plugin_id: str,
        rating: float,
        user_id: Optional[str] = None,
    ):
        """Record a plugin rating."""
        self._ratings[plugin_id].append(rating)
        self.record_event(
            plugin_id,
            EventType.RATE,
            user_id=user_id,
            metadata={"rating": rating},
        )

        # Update average rating
        if plugin_id in self._metrics:
            ratings = self._ratings[plugin_id]
            self._metrics[plugin_id].avg_rating = sum(ratings) / len(ratings)
            self._metrics[plugin_id].rating_count = len(ratings)

    def _update_metrics(self, event: AnalyticsEvent):
        """Update aggregated metrics from event."""
        if event.plugin_id not in self._metrics:
            self._metrics[event.plugin_id] = PluginMetrics(plugin_id=event.plugin_id)

        metrics = self._metrics[event.plugin_id]
        metrics.last_updated = datetime.utcnow()

        if event.event_type == EventType.VIEW:
            metrics.total_views += 1
        elif event.event_type == EventType.INSTALL:
            metrics.total_installs += 1
            metrics.active_installs += 1
        elif event.event_type == EventType.UNINSTALL:
            metrics.active_installs = max(0, metrics.active_installs - 1)
        elif event.event_type == EventType.USE:
            metrics.total_uses += 1
        elif event.event_type == EventType.ERROR:
            metrics.total_errors += 1
        elif event.event_type == EventType.CRASH:
            metrics.total_crashes += 1
        elif event.event_type == EventType.REVIEW:
            metrics.review_count += 1

        # Update error/crash rates
        if metrics.total_uses > 0:
            metrics.error_rate = metrics.total_errors / metrics.total_uses
            metrics.crash_rate = metrics.total_crashes / metrics.total_uses

    def get_plugin_metrics(self, plugin_id: str) -> Optional[PluginMetrics]:
        """Get metrics for a plugin."""
        metrics = self._metrics.get(plugin_id)
        if metrics:
            # Update active user counts
            self._update_active_users(plugin_id)
        return metrics

    def _update_active_users(self, plugin_id: str):
        """Update DAU/WAU/MAU for a plugin."""
        if plugin_id not in self._metrics:
            return

        metrics = self._metrics[plugin_id]
        now = datetime.utcnow()

        activity = self._user_activity.get(plugin_id, {})

        day_ago = now - timedelta(days=1)
        week_ago = now - timedelta(days=7)
        month_ago = now - timedelta(days=30)

        metrics.daily_active_users = sum(1 for t in activity.values() if t > day_ago)
        metrics.weekly_active_users = sum(1 for t in activity.values() if t > week_ago)
        metrics.monthly_active_users = sum(1 for t in activity.values() if t > month_ago)

    def get_developer_stats(self, developer_id: str) -> DeveloperStats:
        """Get aggregated stats for a developer."""
        plugin_ids = self._developer_plugins.get(developer_id, [])

        stats = DeveloperStats(developer_id=developer_id)
        stats.total_plugins = len(plugin_ids)

        total_rating_sum = 0
        total_rating_count = 0

        for plugin_id in plugin_ids:
            metrics = self.get_plugin_metrics(plugin_id)
            if metrics:
                stats.plugins.append(metrics)
                stats.total_installs += metrics.total_installs
                stats.total_active_users += metrics.monthly_active_users
                stats.total_reviews += metrics.review_count

                if metrics.rating_count > 0:
                    total_rating_sum += metrics.avg_rating * metrics.rating_count
                    total_rating_count += metrics.rating_count

        if total_rating_count > 0:
            stats.avg_rating = total_rating_sum / total_rating_count

        # Find trending plugins
        stats.trending_plugins = self._get_trending_plugins(plugin_ids)

        return stats

    def _get_trending_plugins(self, plugin_ids: List[str]) -> List[str]:
        """Get plugins with growing metrics."""
        trending = []

        for plugin_id in plugin_ids:
            recent_installs = self._count_recent_events(plugin_id, EventType.INSTALL, days=7)
            if recent_installs > 5:  # Threshold for "trending"
                trending.append(plugin_id)

        return trending

    def _count_recent_events(
        self,
        plugin_id: str,
        event_type: EventType,
        days: int,
    ) -> int:
        """Count recent events of a type."""
        cutoff = datetime.utcnow() - timedelta(days=days)
        return sum(
            1
            for e in self._events
            if e.plugin_id == plugin_id and e.event_type == event_type and e.timestamp > cutoff
        )

    def get_trend(
        self,
        plugin_id: str,
        metric: str,
        days: int = 30,
    ) -> TrendData:
        """
        Get time-series trend data for a metric.

        Args:
            plugin_id: Plugin identifier
            metric: Metric name (installs, views, uses, errors)
            days: Number of days to include

        Returns:
            TrendData with dates and values
        """
        event_type_map = {
            "installs": EventType.INSTALL,
            "views": EventType.VIEW,
            "uses": EventType.USE,
            "errors": EventType.ERROR,
        }

        event_type = event_type_map.get(metric)
        if not event_type:
            return TrendData(dates=[], values=[], metric_name=metric)

        now = datetime.utcnow()
        dates = []
        values = []

        for i in range(days, -1, -1):
            date = now - timedelta(days=i)
            date_str = date.strftime("%Y-%m-%d")

            count = sum(
                1
                for e in self._events
                if e.plugin_id == plugin_id
                and e.event_type == event_type
                and e.timestamp.strftime("%Y-%m-%d") == date_str
            )

            dates.append(date_str)
            values.append(count)

        return TrendData(dates=dates, values=values, metric_name=metric)

    def get_benchmark(self, plugin_id: str) -> Dict[str, Any]:
        """
        Get benchmark data comparing plugin to similar plugins.

        Args:
            plugin_id: Plugin identifier

        Returns:
            Benchmark comparison data
        """
        metrics = self.get_plugin_metrics(plugin_id)
        if not metrics:
            return {}

        # Calculate percentiles across all plugins
        all_metrics = list(self._metrics.values())
        if len(all_metrics) < 2:
            return {"message": "Not enough plugins for benchmarking"}

        def percentile_rank(value: float, values: List[float]) -> float:
            if not values:
                return 0.0
            below = sum(1 for v in values if v < value)
            return (below / len(values)) * 100

        return {
            "plugin_id": plugin_id,
            "install_percentile": percentile_rank(
                metrics.total_installs,
                [m.total_installs for m in all_metrics],
            ),
            "rating_percentile": percentile_rank(
                metrics.avg_rating,
                [m.avg_rating for m in all_metrics if m.rating_count > 0],
            ),
            "engagement_percentile": percentile_rank(
                metrics.monthly_active_users,
                [m.monthly_active_users for m in all_metrics],
            ),
            "quality_percentile": percentile_rank(
                1 - metrics.error_rate,
                [1 - m.error_rate for m in all_metrics],
            ),
        }

    def cleanup_old_events(self):
        """Remove events older than retention period."""
        cutoff = datetime.utcnow() - timedelta(days=self._retention_days)
        self._events = [e for e in self._events if e.timestamp > cutoff]
        logger.info(f"Cleaned up events older than {cutoff}")

    # =========================================================================
    # D-1: Persistence Integration
    # =========================================================================

    def save_event_to_db(self, event: AnalyticsEvent) -> bool:
        """
        Save a single analytics event to persistent storage.

        Args:
            event: The event to persist.

        Returns:
            True if saved successfully, False otherwise.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import get_phase6_persistence

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available, event not saved")
            return False

        return persistence.record_analytics_event(
            plugin_id=event.plugin_id,
            event_type=event.event_type.value,
            user_id=event.user_id,
            metadata=event.metadata,
            version=event.version,
        )

    def save_metrics_to_db(self, plugin_id: str) -> bool:
        """
        Save aggregated metrics for a plugin to persistent storage.

        Args:
            plugin_id: Plugin identifier.

        Returns:
            True if saved successfully, False otherwise.
        """
        metrics = self._metrics.get(plugin_id)
        if not metrics:
            logger.warning(f"No metrics found for plugin {plugin_id}")
            return False

        try:
            from backend.plugins.persistence.phase6_persistence import get_phase6_persistence

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available, metrics not saved")
            return False

        return persistence.update_plugin_metrics(
            plugin_id=plugin_id,
            total_installs=metrics.total_installs,
            total_uninstalls=metrics.total_uninstalls,
            active_installs=metrics.active_installs,
            monthly_active_users=metrics.monthly_active_users,
            avg_rating=metrics.avg_rating,
            rating_count=metrics.rating_count,
            error_rate=metrics.error_rate,
        )

    def load_metrics_from_db(self, plugin_id: str) -> bool:
        """
        Load aggregated metrics for a plugin from persistent storage.

        Args:
            plugin_id: Plugin identifier.

        Returns:
            True if loaded successfully, False otherwise.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import get_phase6_persistence

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available, metrics not loaded")
            return False

        pm = persistence.get_plugin_metrics(plugin_id)
        if not pm:
            return False

        self._metrics[plugin_id] = PluginMetrics(
            plugin_id=pm.plugin_id,
            total_installs=pm.total_installs,
            total_uninstalls=pm.total_uninstalls,
            active_installs=pm.active_installs,
            monthly_active_users=pm.monthly_active_users,
            avg_rating=pm.avg_rating,
            rating_count=pm.rating_count,
            error_rate=pm.error_rate,
            last_updated=pm.last_updated,
        )

        logger.info(f"Loaded metrics for plugin {plugin_id} from database")
        return True

    def get_events_from_db(
        self,
        plugin_id: Optional[str] = None,
        event_type: Optional[str] = None,
        limit: int = 100,
    ) -> List[AnalyticsEvent]:
        """
        Retrieve analytics events from persistent storage.

        Args:
            plugin_id: Filter by plugin ID (optional).
            event_type: Filter by event type (optional).
            limit: Maximum number of events to return.

        Returns:
            List of AnalyticsEvent objects.
        """
        try:
            from backend.plugins.persistence.phase6_persistence import get_phase6_persistence

            persistence = get_phase6_persistence()
        except ImportError:
            logger.warning("Phase 6 persistence not available")
            return []

        persisted = persistence.get_analytics_events(
            plugin_id=plugin_id,
            event_type=event_type,
            limit=limit,
        )

        events = []
        for pe in persisted:
            try:
                et = EventType(pe.event_type)
            except ValueError:
                continue

            events.append(
                AnalyticsEvent(
                    event_id=pe.event_id,
                    plugin_id=pe.plugin_id,
                    event_type=et,
                    timestamp=pe.timestamp,
                    user_id=pe.user_id,
                    metadata=pe.metadata,
                    version=pe.version,
                )
            )

        return events
