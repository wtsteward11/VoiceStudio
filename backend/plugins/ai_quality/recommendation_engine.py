"""
Plugin Recommendation Engine.

Phase 6B: Recommends plugins based on user preferences and usage patterns.
Uses collaborative filtering and content similarity - no cloud ML required.

Recommendation Approaches:
1. Content-Based: Match plugin features to user preferences
2. Collaborative Filtering: Find similar users, recommend their plugins
3. Popularity: Rank by downloads, ratings, active users

Usage:
    engine = RecommendationEngine()

    # Record user interactions
    engine.record_interaction("user-1", "plugin-a", InteractionType.INSTALL)
    engine.record_interaction("user-1", "plugin-b", InteractionType.USE)

    # Get recommendations
    recommendations = engine.recommend("user-1", limit=5)

    for rec in recommendations:
        print(f"{rec.plugin_id}: {rec.score:.2f} - {rec.reason}")
"""

from __future__ import annotations

import logging
import math
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any, Dict, List, Optional, Set, Tuple

logger = logging.getLogger(__name__)


class InteractionType(Enum):
    """Types of user-plugin interactions."""

    VIEW = "view"
    INSTALL = "install"
    UNINSTALL = "uninstall"
    USE = "use"
    RATE = "rate"
    REVIEW = "review"
    FAVORITE = "favorite"


# Weights for different interaction types
INTERACTION_WEIGHTS = {
    InteractionType.VIEW: 1.0,
    InteractionType.INSTALL: 5.0,
    InteractionType.UNINSTALL: -3.0,
    InteractionType.USE: 2.0,
    InteractionType.RATE: 3.0,
    InteractionType.REVIEW: 4.0,
    InteractionType.FAVORITE: 6.0,
}


@dataclass
class PluginFeatures:
    """
    Feature vector for a plugin.

    Attributes:
        plugin_id: Plugin identifier
        categories: Plugin categories
        tags: Plugin tags
        capabilities: Required capabilities
        author: Author identifier
        downloads: Download count
        rating: Average rating (0-5)
        created_at: Creation timestamp
    """

    plugin_id: str
    categories: List[str] = field(default_factory=list)
    tags: List[str] = field(default_factory=list)
    capabilities: List[str] = field(default_factory=list)
    author: str = ""
    downloads: int = 0
    rating: float = 0.0
    created_at: Optional[datetime] = None

    def get_feature_set(self) -> Set[str]:
        """Get all features as a set for similarity calculation."""
        features = set()
        features.update(f"cat:{c}" for c in self.categories)
        features.update(f"tag:{t}" for t in self.tags)
        features.update(f"cap:{c}" for c in self.capabilities)
        if self.author:
            features.add(f"author:{self.author}")
        return features


@dataclass
class Recommendation:
    """
    A plugin recommendation.

    Attributes:
        plugin_id: Plugin identifier
        score: Recommendation score (0-1)
        reason: Explanation for recommendation
        method: Method used to generate recommendation
        features: Plugin features
    """

    plugin_id: str
    score: float
    reason: str
    method: str
    features: Optional[PluginFeatures] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "plugin_id": self.plugin_id,
            "score": self.score,
            "reason": self.reason,
            "method": self.method,
        }


@dataclass
class UserInteraction:
    """A single user-plugin interaction."""

    user_id: str
    plugin_id: str
    interaction_type: InteractionType
    timestamp: datetime = field(default_factory=datetime.utcnow)
    value: float = 1.0  # For ratings, actual value


class RecommendationEngine:
    """
    Plugin recommendation engine.

    Uses collaborative filtering and content similarity to recommend
    plugins to users. All operations run locally.

    Example:
        engine = RecommendationEngine()

        # Add plugin features
        engine.add_plugin_features(PluginFeatures(
            plugin_id="audio-fx",
            categories=["audio", "effects"],
            tags=["reverb", "delay", "chorus"],
        ))

        # Record interactions
        engine.record_interaction("user-1", "audio-fx", InteractionType.INSTALL)

        # Get recommendations
        recs = engine.recommend("user-1", limit=5)
    """

    def __init__(
        self,
        similarity_threshold: float = 0.2,
        min_interactions: int = 3,
        collaborative_weight: float = 0.4,
        content_weight: float = 0.4,
        popularity_weight: float = 0.2,
    ):
        """
        Initialize recommendation engine.

        Args:
            similarity_threshold: Min similarity to consider (0-1)
            min_interactions: Min interactions before recommending
            collaborative_weight: Weight for collaborative filtering
            content_weight: Weight for content-based filtering
            popularity_weight: Weight for popularity-based ranking
        """
        self._similarity_threshold = similarity_threshold
        self._min_interactions = min_interactions
        self._collaborative_weight = collaborative_weight
        self._content_weight = content_weight
        self._popularity_weight = popularity_weight

        # Plugin features
        self._plugins: Dict[str, PluginFeatures] = {}

        # User interactions
        self._interactions: List[UserInteraction] = []

        # Aggregated user-plugin scores
        self._user_plugin_scores: Dict[str, Dict[str, float]] = defaultdict(
            lambda: defaultdict(float)
        )

        # Pre-computed similarity cache
        self._similarity_cache: Dict[Tuple[str, str], float] = {}

    def add_plugin_features(self, features: PluginFeatures):
        """Add or update plugin features."""
        self._plugins[features.plugin_id] = features
        # Invalidate similarity cache
        self._similarity_cache.clear()

    def record_interaction(
        self,
        user_id: str,
        plugin_id: str,
        interaction_type: InteractionType,
        value: float = 1.0,
    ):
        """
        Record a user-plugin interaction.

        Args:
            user_id: User identifier
            plugin_id: Plugin identifier
            interaction_type: Type of interaction
            value: Interaction value (e.g., rating 1-5)
        """
        interaction = UserInteraction(
            user_id=user_id,
            plugin_id=plugin_id,
            interaction_type=interaction_type,
            timestamp=datetime.utcnow(),
            value=value,
        )
        self._interactions.append(interaction)

        # Update aggregated score
        weight = INTERACTION_WEIGHTS.get(interaction_type, 1.0)
        self._user_plugin_scores[user_id][plugin_id] += weight * value

    def recommend(
        self,
        user_id: str,
        limit: int = 10,
        exclude_installed: bool = True,
    ) -> List[Recommendation]:
        """
        Get plugin recommendations for a user.

        Args:
            user_id: User identifier
            limit: Maximum recommendations to return
            exclude_installed: Exclude already-installed plugins

        Returns:
            List of recommendations sorted by score
        """
        # Get user's interacted plugins
        user_plugins = set(self._user_plugin_scores.get(user_id, {}).keys())

        if len(user_plugins) < self._min_interactions:
            # Not enough interactions, return popularity-based
            return self._recommend_popular(user_id, limit, exclude_installed)

        # Compute scores from different methods
        content_scores = self._content_based_scores(user_id, user_plugins)
        collaborative_scores = self._collaborative_scores(user_id, user_plugins)
        popularity_scores = self._popularity_scores()

        # Combine scores
        all_plugins = set(content_scores.keys()) | set(collaborative_scores.keys())
        combined_scores: Dict[str, float] = {}

        for plugin_id in all_plugins:
            if exclude_installed and plugin_id in user_plugins:
                continue

            score = (
                self._content_weight * content_scores.get(plugin_id, 0)
                + self._collaborative_weight * collaborative_scores.get(plugin_id, 0)
                + self._popularity_weight * popularity_scores.get(plugin_id, 0)
            )
            combined_scores[plugin_id] = score

        # Sort and return top recommendations
        sorted_plugins = sorted(
            combined_scores.items(),
            key=lambda x: x[1],
            reverse=True,
        )[:limit]

        recommendations = []
        for plugin_id, score in sorted_plugins:
            reason = self._generate_reason(
                plugin_id,
                user_plugins,
                content_scores.get(plugin_id, 0),
                collaborative_scores.get(plugin_id, 0),
            )
            recommendations.append(
                Recommendation(
                    plugin_id=plugin_id,
                    score=score,
                    reason=reason,
                    method="hybrid",
                    features=self._plugins.get(plugin_id),
                )
            )

        return recommendations

    def _content_based_scores(
        self,
        user_id: str,
        user_plugins: Set[str],
    ) -> Dict[str, float]:
        """Compute content-based similarity scores."""
        scores: Dict[str, float] = {}

        # Build user preference profile from liked plugins
        user_features: Set[str] = set()
        for plugin_id in user_plugins:
            if plugin_id in self._plugins:
                user_features.update(self._plugins[plugin_id].get_feature_set())

        if not user_features:
            return scores

        # Score all plugins by similarity to user profile
        for plugin_id, features in self._plugins.items():
            if plugin_id in user_plugins:
                continue

            plugin_features = features.get_feature_set()
            similarity = self._jaccard_similarity(user_features, plugin_features)

            if similarity >= self._similarity_threshold:
                scores[plugin_id] = similarity

        return scores

    def _collaborative_scores(
        self,
        user_id: str,
        user_plugins: Set[str],
    ) -> Dict[str, float]:
        """Compute collaborative filtering scores."""
        scores: Dict[str, float] = defaultdict(float)

        # Find similar users
        user_scores = self._user_plugin_scores.get(user_id, {})
        similar_users: List[Tuple[str, float]] = []

        for other_user_id, other_scores in self._user_plugin_scores.items():
            if other_user_id == user_id:
                continue

            # Compute user similarity (cosine similarity of interaction vectors)
            similarity = self._user_similarity(user_scores, other_scores)
            if similarity > self._similarity_threshold:
                similar_users.append((other_user_id, similarity))

        # Get recommendations from similar users
        for other_user_id, similarity in similar_users:
            other_scores = self._user_plugin_scores[other_user_id]
            for plugin_id, score in other_scores.items():
                if plugin_id not in user_plugins and score > 0:
                    scores[plugin_id] += similarity * score

        # Normalize scores
        if scores:
            max_score = max(scores.values())
            if max_score > 0:
                for plugin_id in scores:
                    scores[plugin_id] /= max_score

        return dict(scores)

    def _popularity_scores(self) -> Dict[str, float]:
        """Compute popularity-based scores."""
        scores: Dict[str, float] = {}

        # Count total interactions per plugin
        plugin_counts: Dict[str, float] = defaultdict(float)
        for interaction in self._interactions:
            weight = INTERACTION_WEIGHTS.get(interaction.interaction_type, 1.0)
            plugin_counts[interaction.plugin_id] += weight

        # Normalize
        if plugin_counts:
            max_count = max(plugin_counts.values())
            if max_count > 0:
                for plugin_id, count in plugin_counts.items():
                    scores[plugin_id] = count / max_count

        return scores

    def _recommend_popular(
        self,
        user_id: str,
        limit: int,
        exclude_installed: bool,
    ) -> List[Recommendation]:
        """Fallback to popularity-based recommendations."""
        user_plugins = set(self._user_plugin_scores.get(user_id, {}).keys())
        popularity_scores = self._popularity_scores()

        # Add plugin features popularity
        for plugin_id, features in self._plugins.items():
            if plugin_id not in popularity_scores:
                popularity_scores[plugin_id] = 0.0

            # Boost by rating and downloads
            if features.rating > 0:
                popularity_scores[plugin_id] += features.rating / 5.0 * 0.3
            if features.downloads > 0:
                popularity_scores[plugin_id] += min(1.0, features.downloads / 1000) * 0.2

        # Filter and sort
        candidates = [
            (pid, score)
            for pid, score in popularity_scores.items()
            if not exclude_installed or pid not in user_plugins
        ]
        candidates.sort(key=lambda x: x[1], reverse=True)

        recommendations = []
        for plugin_id, score in candidates[:limit]:
            recommendations.append(
                Recommendation(
                    plugin_id=plugin_id,
                    score=score,
                    reason="Popular in the community",
                    method="popularity",
                    features=self._plugins.get(plugin_id),
                )
            )

        return recommendations

    def _jaccard_similarity(self, set1: Set[str], set2: Set[str]) -> float:
        """Compute Jaccard similarity between two sets."""
        if not set1 or not set2:
            return 0.0

        intersection = len(set1 & set2)
        union = len(set1 | set2)

        return intersection / union if union > 0 else 0.0

    def _user_similarity(
        self,
        scores1: Dict[str, float],
        scores2: Dict[str, float],
    ) -> float:
        """Compute cosine similarity between user interaction vectors."""
        common_plugins = set(scores1.keys()) & set(scores2.keys())

        if not common_plugins:
            return 0.0

        dot_product = sum(scores1[p] * scores2[p] for p in common_plugins)
        norm1 = math.sqrt(sum(v**2 for v in scores1.values()))
        norm2 = math.sqrt(sum(v**2 for v in scores2.values()))

        if norm1 == 0 or norm2 == 0:
            return 0.0

        return dot_product / (norm1 * norm2)

    def _generate_reason(
        self,
        plugin_id: str,
        user_plugins: Set[str],
        content_score: float,
        collaborative_score: float,
    ) -> str:
        """Generate human-readable recommendation reason."""
        reasons = []

        if content_score > 0.5:
            # Find similar plugins user already uses
            features = self._plugins.get(plugin_id)
            if features:
                similar = self._find_similar_installed(plugin_id, user_plugins)
                if similar:
                    reasons.append(f"Similar to {similar[0]}")
                elif features.categories:
                    reasons.append(f"Based on your interest in {features.categories[0]}")

        if collaborative_score > 0.5:
            reasons.append("Users like you also use this")

        if not reasons:
            features = self._plugins.get(plugin_id)
            if features and features.rating >= 4.0:
                reasons.append(f"Highly rated ({features.rating:.1f}/5)")
            else:
                reasons.append("You might find this useful")

        return "; ".join(reasons)

    def _find_similar_installed(
        self,
        plugin_id: str,
        user_plugins: Set[str],
    ) -> List[str]:
        """Find installed plugins similar to a candidate."""
        target_features = self._plugins.get(plugin_id)
        if not target_features:
            return []

        target_set = target_features.get_feature_set()
        similar = []

        for installed_id in user_plugins:
            installed_features = self._plugins.get(installed_id)
            if installed_features:
                installed_set = installed_features.get_feature_set()
                similarity = self._jaccard_similarity(target_set, installed_set)
                if similarity > 0.3:
                    similar.append(installed_id)

        return similar

    def get_similar_plugins(
        self,
        plugin_id: str,
        limit: int = 5,
    ) -> List[Recommendation]:
        """Get plugins similar to a given plugin."""
        target_features = self._plugins.get(plugin_id)
        if not target_features:
            return []

        target_set = target_features.get_feature_set()
        similarities: List[Tuple[str, float]] = []

        for other_id, features in self._plugins.items():
            if other_id == plugin_id:
                continue

            other_set = features.get_feature_set()
            similarity = self._jaccard_similarity(target_set, other_set)

            if similarity >= self._similarity_threshold:
                similarities.append((other_id, similarity))

        similarities.sort(key=lambda x: x[1], reverse=True)

        recommendations = []
        for pid, score in similarities[:limit]:
            recommendations.append(
                Recommendation(
                    plugin_id=pid,
                    score=score,
                    reason=f"Similar to {plugin_id}",
                    method="content-similarity",
                    features=self._plugins.get(pid),
                )
            )

        return recommendations
