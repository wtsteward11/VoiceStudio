"""
Featured Plugins Manager.

Phase 6D: Automated discovery and promotion of quality plugins.
Uses local algorithms — no cloud dependencies or paid services.

Features:
1. Automated quality scoring
2. Trending detection
3. New & noteworthy selection
4. Category-based featuring
5. Staff picks (manual curation)

Usage:
    manager = FeaturedPluginsManager(analytics)

    # Update featured lists
    manager.update_featured()

    # Get featured by category
    trending = manager.get_featured("trending")
    top_rated = manager.get_featured("top_rated")
"""

from __future__ import annotations

import logging
import math
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from typing import Any, Callable, Dict, List, Optional, Set

logger = logging.getLogger(__name__)


class FeatureReason(Enum):
    """Reasons a plugin may be featured."""

    TRENDING = "trending"
    TOP_RATED = "top_rated"
    NEW_NOTEWORTHY = "new_noteworthy"
    MOST_INSTALLED = "most_installed"
    STAFF_PICK = "staff_pick"
    CATEGORY_TOP = "category_top"
    EDITOR_CHOICE = "editor_choice"
    COMMUNITY_FAVORITE = "community_favorite"


@dataclass
class PluginScore:
    """Scoring components for a plugin."""

    plugin_id: str
    quality_score: float = 0.0  # Code quality, compliance
    popularity_score: float = 0.0  # Installs, active users
    engagement_score: float = 0.0  # Uses per user, retention
    rating_score: float = 0.0  # Average rating
    momentum_score: float = 0.0  # Growth rate
    recency_score: float = 0.0  # Recent activity

    @property
    def total_score(self) -> float:
        """Calculate weighted total score."""
        weights = {
            "quality": 0.25,
            "popularity": 0.20,
            "engagement": 0.20,
            "rating": 0.20,
            "momentum": 0.10,
            "recency": 0.05,
        }
        return (
            self.quality_score * weights["quality"]
            + self.popularity_score * weights["popularity"]
            + self.engagement_score * weights["engagement"]
            + self.rating_score * weights["rating"]
            + self.momentum_score * weights["momentum"]
            + self.recency_score * weights["recency"]
        )


@dataclass
class FeaturedPlugin:
    """A featured plugin entry."""

    plugin_id: str
    reason: FeatureReason
    score: float
    category: Optional[str] = None
    featured_at: datetime = field(default_factory=datetime.utcnow)
    expires_at: Optional[datetime] = None
    metadata: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "plugin_id": self.plugin_id,
            "reason": self.reason.value,
            "score": round(self.score, 2),
            "category": self.category,
            "featured_at": self.featured_at.isoformat(),
            "expires_at": self.expires_at.isoformat() if self.expires_at else None,
            "metadata": self.metadata,
        }


@dataclass
class FeaturedList:
    """A list of featured plugins."""

    name: str
    plugins: List[FeaturedPlugin] = field(default_factory=list)
    updated_at: datetime = field(default_factory=datetime.utcnow)
    max_size: int = 10

    def add(self, plugin: FeaturedPlugin):
        """Add plugin if not already in list."""
        if not any(p.plugin_id == plugin.plugin_id for p in self.plugins):
            self.plugins.append(plugin)
            self.plugins.sort(key=lambda p: p.score, reverse=True)
            self.plugins = self.plugins[: self.max_size]
            self.updated_at = datetime.utcnow()


class FeaturedPluginsManager:
    """
    Manages featured plugin discovery and curation.

    Automatically identifies and promotes quality plugins based on:
    - Quality metrics (compliance, code review)
    - Popularity (installs, users)
    - Engagement (usage patterns)
    - Ratings and reviews
    - Growth momentum

    Example:
        from backend.plugins.ecosystem import DeveloperAnalytics

        analytics = DeveloperAnalytics()
        manager = FeaturedPluginsManager(analytics)

        # Update all featured lists
        manager.update_featured()

        # Get specific list
        trending = manager.get_featured("trending")
        for plugin in trending:
            print(f"{plugin.plugin_id}: {plugin.score}")
    """

    def __init__(
        self,
        analytics: Optional[Any] = None,
        update_interval_hours: int = 6,
        trending_window_days: int = 7,
        new_window_days: int = 30,
    ):
        """
        Initialize featured plugins manager.

        Args:
            analytics: DeveloperAnalytics instance for metrics
            update_interval_hours: Hours between auto-updates
            trending_window_days: Days for trending calculation
            new_window_days: Days a plugin is considered "new"
        """
        self._analytics = analytics
        self._update_interval = timedelta(hours=update_interval_hours)
        self._trending_window = timedelta(days=trending_window_days)
        self._new_window = timedelta(days=new_window_days)

        # Featured lists
        self._lists: Dict[str, FeaturedList] = {
            "trending": FeaturedList(name="trending", max_size=10),
            "top_rated": FeaturedList(name="top_rated", max_size=10),
            "most_installed": FeaturedList(name="most_installed", max_size=10),
            "new_noteworthy": FeaturedList(name="new_noteworthy", max_size=10),
            "staff_picks": FeaturedList(name="staff_picks", max_size=10),
        }

        # Plugin metadata (publish dates, categories, etc.)
        self._plugin_metadata: Dict[str, Dict[str, Any]] = {}

        # Staff picks (manually curated)
        self._staff_picks: Set[str] = set()

        # Quality scores from compliance scans
        self._quality_scores: Dict[str, float] = {}

        # Category -> plugins mapping
        self._categories: Dict[str, List[str]] = {}

    def register_plugin(
        self,
        plugin_id: str,
        category: Optional[str] = None,
        publish_date: Optional[datetime] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ):
        """Register plugin metadata."""
        self._plugin_metadata[plugin_id] = {
            "category": category,
            "publish_date": publish_date or datetime.utcnow(),
            **(metadata or {}),
        }

        if category:
            if category not in self._categories:
                self._categories[category] = []
            if plugin_id not in self._categories[category]:
                self._categories[category].append(plugin_id)

    def set_quality_score(self, plugin_id: str, score: float):
        """Set quality score from compliance scan."""
        self._quality_scores[plugin_id] = max(0.0, min(1.0, score))

    def add_staff_pick(self, plugin_id: str):
        """Add plugin to staff picks."""
        self._staff_picks.add(plugin_id)

    def remove_staff_pick(self, plugin_id: str):
        """Remove plugin from staff picks."""
        self._staff_picks.discard(plugin_id)

    def _calculate_score(self, plugin_id: str) -> PluginScore:
        """Calculate comprehensive score for a plugin."""
        score = PluginScore(plugin_id=plugin_id)

        # Quality score from compliance
        score.quality_score = self._quality_scores.get(plugin_id, 0.5)

        # Get metrics from analytics if available
        if self._analytics:
            metrics = self._analytics.get_plugin_metrics(plugin_id)
            if metrics:
                # Popularity: normalize installs (log scale)
                if metrics.total_installs > 0:
                    score.popularity_score = min(
                        1.0,
                        math.log10(metrics.total_installs + 1) / 4.0,
                    )

                # Engagement: MAU ratio to installs
                if metrics.active_installs > 0:
                    score.engagement_score = min(
                        1.0,
                        metrics.monthly_active_users / metrics.active_installs,
                    )

                # Rating: normalize 0-5 to 0-1
                if metrics.rating_count > 0:
                    score.rating_score = metrics.avg_rating / 5.0

        # Momentum: recent growth (simplified)
        score.momentum_score = self._calculate_momentum(plugin_id)

        # Recency: how recently updated
        score.recency_score = self._calculate_recency(plugin_id)

        return score

    def _calculate_momentum(self, plugin_id: str) -> float:
        """Calculate growth momentum."""
        if not self._analytics:
            return 0.5

        trend = self._analytics.get_trend(plugin_id, "installs", days=7)
        if not trend.values or len(trend.values) < 2:
            return 0.5

        # Compare recent half to older half
        mid = len(trend.values) // 2
        recent = sum(trend.values[mid:])
        older = sum(trend.values[:mid])

        if older == 0:
            return 0.8 if recent > 0 else 0.5

        growth_rate = (recent - older) / older
        # Normalize to 0-1
        return max(0.0, min(1.0, 0.5 + growth_rate * 0.25))

    def _calculate_recency(self, plugin_id: str) -> float:
        """Calculate recency score based on publish date."""
        metadata = self._plugin_metadata.get(plugin_id, {})
        publish_date = metadata.get("publish_date")

        if not publish_date:
            return 0.5

        age_days = (datetime.utcnow() - publish_date).days

        # Score decreases with age
        if age_days <= 7:
            return 1.0
        elif age_days <= 30:
            return 0.8
        elif age_days <= 90:
            return 0.6
        elif age_days <= 365:
            return 0.4
        else:
            return 0.2

    def update_featured(self):
        """Update all featured lists."""
        all_plugins = list(self._plugin_metadata.keys())

        if not all_plugins:
            logger.info("No plugins registered, skipping featured update")
            return

        # Calculate scores for all plugins
        scores: Dict[str, PluginScore] = {}
        for plugin_id in all_plugins:
            scores[plugin_id] = self._calculate_score(plugin_id)

        # Update trending
        self._update_trending(scores)

        # Update top rated
        self._update_top_rated(scores)

        # Update most installed
        self._update_most_installed(scores)

        # Update new & noteworthy
        self._update_new_noteworthy(scores)

        # Update staff picks
        self._update_staff_picks(scores)

        logger.info("Updated all featured lists")

    def _update_trending(self, scores: Dict[str, PluginScore]):
        """Update trending list based on momentum."""
        self._lists["trending"] = FeaturedList(name="trending", max_size=10)

        # Sort by momentum score
        sorted_plugins = sorted(
            scores.items(),
            key=lambda x: x[1].momentum_score,
            reverse=True,
        )

        for plugin_id, score in sorted_plugins[:10]:
            if score.momentum_score > 0.5:  # Threshold
                self._lists["trending"].add(
                    FeaturedPlugin(
                        plugin_id=plugin_id,
                        reason=FeatureReason.TRENDING,
                        score=score.momentum_score,
                        metadata={"total_score": score.total_score},
                    )
                )

    def _update_top_rated(self, scores: Dict[str, PluginScore]):
        """Update top rated list."""
        self._lists["top_rated"] = FeaturedList(name="top_rated", max_size=10)

        # Filter plugins with sufficient ratings
        rated_plugins = [(pid, s) for pid, s in scores.items() if s.rating_score > 0]

        sorted_plugins = sorted(
            rated_plugins,
            key=lambda x: x[1].rating_score,
            reverse=True,
        )

        for plugin_id, score in sorted_plugins[:10]:
            self._lists["top_rated"].add(
                FeaturedPlugin(
                    plugin_id=plugin_id,
                    reason=FeatureReason.TOP_RATED,
                    score=score.rating_score,
                )
            )

    def _update_most_installed(self, scores: Dict[str, PluginScore]):
        """Update most installed list."""
        self._lists["most_installed"] = FeaturedList(
            name="most_installed",
            max_size=10,
        )

        sorted_plugins = sorted(
            scores.items(),
            key=lambda x: x[1].popularity_score,
            reverse=True,
        )

        for plugin_id, score in sorted_plugins[:10]:
            self._lists["most_installed"].add(
                FeaturedPlugin(
                    plugin_id=plugin_id,
                    reason=FeatureReason.MOST_INSTALLED,
                    score=score.popularity_score,
                )
            )

    def _update_new_noteworthy(self, scores: Dict[str, PluginScore]):
        """Update new & noteworthy list."""
        self._lists["new_noteworthy"] = FeaturedList(
            name="new_noteworthy",
            max_size=10,
        )

        cutoff = datetime.utcnow() - self._new_window

        # Filter to new plugins
        new_plugins = [
            (pid, s)
            for pid, s in scores.items()
            if self._plugin_metadata.get(pid, {}).get("publish_date", datetime.min) > cutoff
        ]

        # Sort by total score
        sorted_plugins = sorted(
            new_plugins,
            key=lambda x: x[1].total_score,
            reverse=True,
        )

        for plugin_id, score in sorted_plugins[:10]:
            self._lists["new_noteworthy"].add(
                FeaturedPlugin(
                    plugin_id=plugin_id,
                    reason=FeatureReason.NEW_NOTEWORTHY,
                    score=score.total_score,
                )
            )

    def _update_staff_picks(self, scores: Dict[str, PluginScore]):
        """Update staff picks list."""
        self._lists["staff_picks"] = FeaturedList(name="staff_picks", max_size=10)

        for plugin_id in self._staff_picks:
            if plugin_id in scores:
                self._lists["staff_picks"].add(
                    FeaturedPlugin(
                        plugin_id=plugin_id,
                        reason=FeatureReason.STAFF_PICK,
                        score=scores[plugin_id].total_score,
                    )
                )

    def get_featured(
        self,
        list_name: str,
        limit: Optional[int] = None,
    ) -> List[FeaturedPlugin]:
        """
        Get featured plugins from a specific list.

        Args:
            list_name: Name of the list (trending, top_rated, etc.)
            limit: Optional limit on results

        Returns:
            List of FeaturedPlugin entries
        """
        featured_list = self._lists.get(list_name)
        if not featured_list:
            return []

        plugins = featured_list.plugins
        if limit:
            plugins = plugins[:limit]

        return plugins

    def get_category_top(
        self,
        category: str,
        limit: int = 10,
    ) -> List[FeaturedPlugin]:
        """Get top plugins in a category."""
        plugin_ids = self._categories.get(category, [])
        if not plugin_ids:
            return []

        # Score and sort
        scored = [(pid, self._calculate_score(pid)) for pid in plugin_ids]
        scored.sort(key=lambda x: x[1].total_score, reverse=True)

        return [
            FeaturedPlugin(
                plugin_id=pid,
                reason=FeatureReason.CATEGORY_TOP,
                score=score.total_score,
                category=category,
            )
            for pid, score in scored[:limit]
        ]

    def get_all_featured(self) -> Dict[str, List[Dict[str, Any]]]:
        """Get all featured lists as a dictionary."""
        return {
            name: [p.to_dict() for p in featured_list.plugins]
            for name, featured_list in self._lists.items()
        }
