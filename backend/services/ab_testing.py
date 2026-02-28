"""
A/B Testing Service — Phase 8.1

Provides server-side A/B testing capabilities for VoiceStudio.

Features:
- Experiment configuration and management
- User bucketing with stable hashing
- Variant assignment with percentage rollouts
- Experiment analytics and tracking
- Multi-arm bandit support (future)

Local-first: All data stored locally, no external dependencies.
Privacy: User IDs are hashed; no PII stored.
"""

from __future__ import annotations

import hashlib
import json
import logging
from dataclasses import asdict, dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

# Default experiments config directory
DEFAULT_EXPERIMENTS_DIR = Path("config/experiments")


class ExperimentStatus(str, Enum):
    """Experiment lifecycle status."""

    DRAFT = "draft"
    RUNNING = "running"
    PAUSED = "paused"
    COMPLETED = "completed"
    ARCHIVED = "archived"


class VariantType(str, Enum):
    """Type of variant."""

    CONTROL = "control"
    TREATMENT = "treatment"


@dataclass
class Variant:
    """A single variant in an experiment."""

    id: str
    name: str
    description: str = ""
    weight: int = 50  # Percentage weight (0-100)
    config: dict[str, Any] = field(default_factory=dict)


@dataclass
class Experiment:
    """An A/B test experiment."""

    id: str
    name: str
    description: str
    status: ExperimentStatus = ExperimentStatus.DRAFT
    variants: list[Variant] = field(default_factory=list)
    created_at: datetime = field(default_factory=datetime.utcnow)
    updated_at: datetime = field(default_factory=datetime.utcnow)
    start_date: datetime | None = None
    end_date: datetime | None = None
    target_sample_size: int = 0
    current_sample_size: int = 0
    metrics: list[str] = field(default_factory=list)
    tags: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for serialization."""
        return {
            "id": self.id,
            "name": self.name,
            "description": self.description,
            "status": self.status.value,
            "variants": [asdict(v) for v in self.variants],
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
            "start_date": self.start_date.isoformat() if self.start_date else None,
            "end_date": self.end_date.isoformat() if self.end_date else None,
            "target_sample_size": self.target_sample_size,
            "current_sample_size": self.current_sample_size,
            "metrics": self.metrics,
            "tags": self.tags,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Experiment:
        """Create from dictionary."""
        variants = [
            Variant(
                id=v["id"],
                name=v["name"],
                description=v.get("description", ""),
                weight=v.get("weight", 50),
                config=v.get("config", {}),
            )
            for v in data.get("variants", [])
        ]
        return cls(
            id=data["id"],
            name=data["name"],
            description=data.get("description", ""),
            status=ExperimentStatus(data.get("status", "draft")),
            variants=variants,
            created_at=(
                datetime.fromisoformat(data["created_at"])
                if data.get("created_at")
                else datetime.utcnow()
            ),
            updated_at=(
                datetime.fromisoformat(data["updated_at"])
                if data.get("updated_at")
                else datetime.utcnow()
            ),
            start_date=(
                datetime.fromisoformat(data["start_date"]) if data.get("start_date") else None
            ),
            end_date=datetime.fromisoformat(data["end_date"]) if data.get("end_date") else None,
            target_sample_size=data.get("target_sample_size", 0),
            current_sample_size=data.get("current_sample_size", 0),
            metrics=data.get("metrics", []),
            tags=data.get("tags", []),
        )


@dataclass
class VariantAssignment:
    """Records a user's assignment to a variant."""

    user_id_hash: str
    experiment_id: str
    variant_id: str
    assigned_at: datetime = field(default_factory=datetime.utcnow)
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass
class ExperimentEvent:
    """An event in an experiment (exposure, conversion, etc.)."""

    user_id_hash: str
    experiment_id: str
    variant_id: str
    event_type: str  # "exposure", "conversion", custom events
    event_data: dict[str, Any] = field(default_factory=dict)
    timestamp: datetime = field(default_factory=datetime.utcnow)


class ABTestingService:
    """
    Service for managing A/B tests and experiments.

    Provides:
    - Experiment CRUD operations
    - Deterministic user bucketing
    - Variant assignment
    - Event tracking
    - Basic analytics
    """

    def __init__(self, config_dir: Path | None = None, data_dir: Path | None = None):
        """
        Initialize the A/B testing service.

        Args:
            config_dir: Directory for experiment configurations
            data_dir: Directory for experiment data/events
        """
        self.config_dir = config_dir or DEFAULT_EXPERIMENTS_DIR
        self.data_dir = data_dir or Path(".buildlogs/experiments")

        self._experiments: dict[str, Experiment] = {}
        self._assignments: dict[str, dict[str, VariantAssignment]] = (
            {}
        )  # user_hash -> exp_id -> assignment
        self._events: list[ExperimentEvent] = []

        # Ensure directories exist
        self.config_dir.mkdir(parents=True, exist_ok=True)
        self.data_dir.mkdir(parents=True, exist_ok=True)

        # Load experiments from config
        self._load_experiments()

    def _load_experiments(self) -> None:
        """Load experiment configurations from disk."""
        config_file = self.config_dir / "experiments.json"
        if config_file.exists():
            try:
                with open(config_file, encoding="utf-8") as f:
                    data = json.load(f)
                for exp_data in data.get("experiments", []):
                    exp = Experiment.from_dict(exp_data)
                    self._experiments[exp.id] = exp
                logger.info(f"Loaded {len(self._experiments)} experiments from config")
            except Exception as e:
                logger.error(f"Failed to load experiments: {e}")

    def _save_experiments(self) -> None:
        """Save experiment configurations to disk."""
        config_file = self.config_dir / "experiments.json"
        try:
            data = {
                "experiments": [exp.to_dict() for exp in self._experiments.values()],
                "updated_at": datetime.utcnow().isoformat(),
            }
            with open(config_file, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
        except Exception as e:
            logger.error(f"Failed to save experiments: {e}")

    @staticmethod
    def _hash_user_id(user_id: str) -> str:
        """Hash user ID for privacy."""
        return hashlib.sha256(user_id.encode()).hexdigest()[:16]

    @staticmethod
    def _get_bucket(user_id: str, experiment_id: str, num_buckets: int = 100) -> int:
        """
        Get a stable bucket (0 to num_buckets-1) for a user in an experiment.

        Uses consistent hashing for deterministic assignment.
        """
        key = f"{user_id}:{experiment_id}"
        hash_bytes = hashlib.sha256(key.encode()).digest()
        # Use first 4 bytes as unsigned int
        value = int.from_bytes(hash_bytes[:4], byteorder="big")
        return value % num_buckets

    # =========================================================================
    # Experiment Management
    # =========================================================================

    def create_experiment(
        self,
        id: str,
        name: str,
        description: str = "",
        control_name: str = "Control",
        treatment_name: str = "Treatment",
        treatment_weight: int = 50,
    ) -> Experiment:
        """
        Create a new A/B experiment with control and treatment variants.

        Args:
            id: Unique experiment identifier
            name: Human-readable name
            description: Description of the experiment
            control_name: Name for control variant
            treatment_name: Name for treatment variant
            treatment_weight: Percentage of users in treatment (0-100)

        Returns:
            The created experiment
        """
        if id in self._experiments:
            raise ValueError(f"Experiment '{id}' already exists")

        control_weight = 100 - treatment_weight

        experiment = Experiment(
            id=id,
            name=name,
            description=description,
            variants=[
                Variant(id="control", name=control_name, weight=control_weight),
                Variant(id="treatment", name=treatment_name, weight=treatment_weight),
            ],
        )

        self._experiments[id] = experiment
        self._save_experiments()
        logger.info(f"Created experiment: {id}")

        return experiment

    def register_experiment(self, experiment: Experiment) -> None:
        """Register an experiment (add or update). Persists to config."""
        self._experiments[experiment.id] = experiment
        self._save_experiments()
        logger.info(f"Registered experiment: {experiment.id}")

    def get_experiment(self, experiment_id: str) -> Experiment | None:
        """Get an experiment by ID."""
        return self._experiments.get(experiment_id)

    def list_experiments(
        self,
        status: ExperimentStatus | None = None,
        tags: list[str] | None = None,
    ) -> list[Experiment]:
        """List experiments, optionally filtered by status or tags."""
        experiments = list(self._experiments.values())

        if status:
            experiments = [e for e in experiments if e.status == status]

        if tags:
            experiments = [e for e in experiments if any(t in e.tags for t in tags)]

        return experiments

    def update_experiment_status(
        self,
        experiment_id: str,
        status: ExperimentStatus,
    ) -> Experiment | None:
        """Update experiment status."""
        exp = self._experiments.get(experiment_id)
        if not exp:
            return None

        old_status = exp.status
        exp.status = status
        exp.updated_at = datetime.utcnow()

        if status == ExperimentStatus.RUNNING and not exp.start_date:
            exp.start_date = datetime.utcnow()
        elif status == ExperimentStatus.COMPLETED and not exp.end_date:
            exp.end_date = datetime.utcnow()

        self._save_experiments()
        logger.info(f"Experiment {experiment_id} status: {old_status} -> {status}")

        return exp

    def delete_experiment(self, experiment_id: str) -> bool:
        """Delete an experiment (must be draft or archived)."""
        exp = self._experiments.get(experiment_id)
        if not exp:
            return False

        if exp.status not in (ExperimentStatus.DRAFT, ExperimentStatus.ARCHIVED):
            raise ValueError(f"Cannot delete experiment in status: {exp.status}")

        del self._experiments[experiment_id]
        self._save_experiments()
        logger.info(f"Deleted experiment: {experiment_id}")

        return True

    # =========================================================================
    # Variant Assignment
    # =========================================================================

    def get_variant(self, user_id: str, experiment_id: str) -> str | None:
        """
        Get the variant ID for a user in an experiment.

        Returns None if experiment doesn't exist or isn't running.
        Uses consistent hashing for deterministic assignment.
        """
        exp = self._experiments.get(experiment_id)
        if not exp or exp.status != ExperimentStatus.RUNNING:
            return None

        user_hash = self._hash_user_id(user_id)

        # Check for existing assignment
        if user_hash in self._assignments and experiment_id in self._assignments[user_hash]:
            return self._assignments[user_hash][experiment_id].variant_id

        # Calculate variant based on bucket
        bucket = self._get_bucket(user_id, experiment_id)

        cumulative = 0
        variant_id = exp.variants[-1].id  # Default to last variant

        for variant in exp.variants:
            cumulative += variant.weight
            if bucket < cumulative:
                variant_id = variant.id
                break

        # Record assignment
        if user_hash not in self._assignments:
            self._assignments[user_hash] = {}

        self._assignments[user_hash][experiment_id] = VariantAssignment(
            user_id_hash=user_hash,
            experiment_id=experiment_id,
            variant_id=variant_id,
        )

        # Update sample size
        exp.current_sample_size += 1

        return variant_id

    def is_in_treatment(self, user_id: str, experiment_id: str) -> bool:
        """Check if user is in the treatment group."""
        variant = self.get_variant(user_id, experiment_id)
        return variant == "treatment"

    def get_variant_config(self, user_id: str, experiment_id: str) -> dict[str, Any]:
        """Get the configuration for the user's assigned variant."""
        exp = self._experiments.get(experiment_id)
        if not exp:
            return {}

        variant_id = self.get_variant(user_id, experiment_id)
        if not variant_id:
            return {}

        for variant in exp.variants:
            if variant.id == variant_id:
                return variant.config

        return {}

    # =========================================================================
    # Event Tracking
    # =========================================================================

    def track_exposure(
        self,
        user_id: str,
        experiment_id: str,
        metadata: dict[str, Any] | None = None,
    ) -> ExperimentEvent | None:
        """
        Track that a user was exposed to an experiment.

        Call this when the user sees the variant.
        """
        variant_id = self.get_variant(user_id, experiment_id)
        if not variant_id:
            return None

        return self._track_event(user_id, experiment_id, variant_id, "exposure", metadata)

    def track_conversion(
        self,
        user_id: str,
        experiment_id: str,
        conversion_value: float | None = None,
        metadata: dict[str, Any] | None = None,
    ) -> ExperimentEvent | None:
        """
        Track a conversion event for an experiment.

        Call this when the user completes the target action.
        """
        user_hash = self._hash_user_id(user_id)

        # Get variant from assignment
        if user_hash not in self._assignments:
            return None
        if experiment_id not in self._assignments[user_hash]:
            return None

        variant_id = self._assignments[user_hash][experiment_id].variant_id

        event_data = metadata or {}
        if conversion_value is not None:
            event_data["conversion_value"] = conversion_value

        return self._track_event(user_id, experiment_id, variant_id, "conversion", event_data)

    def track_custom_event(
        self,
        user_id: str,
        experiment_id: str,
        event_type: str,
        event_data: dict[str, Any] | None = None,
    ) -> ExperimentEvent | None:
        """Track a custom event for an experiment."""
        user_hash = self._hash_user_id(user_id)

        if user_hash not in self._assignments:
            return None
        if experiment_id not in self._assignments[user_hash]:
            return None

        variant_id = self._assignments[user_hash][experiment_id].variant_id

        return self._track_event(user_id, experiment_id, variant_id, event_type, event_data)

    def _track_event(
        self,
        user_id: str,
        experiment_id: str,
        variant_id: str,
        event_type: str,
        event_data: dict[str, Any] | None = None,
    ) -> ExperimentEvent:
        """Internal method to record an event."""
        event = ExperimentEvent(
            user_id_hash=self._hash_user_id(user_id),
            experiment_id=experiment_id,
            variant_id=variant_id,
            event_type=event_type,
            event_data=event_data or {},
        )

        self._events.append(event)

        # Persist events periodically (simple implementation)
        if len(self._events) % 100 == 0:
            self._persist_events()

        return event

    def _persist_events(self) -> None:
        """Persist events to disk."""
        events_file = self.data_dir / f"events_{datetime.utcnow().strftime('%Y%m%d')}.jsonl"
        try:
            with open(events_file, "a", encoding="utf-8") as f:
                for event in self._events[-100:]:
                    f.write(json.dumps(asdict(event), default=str) + "\n")
        except Exception as e:
            logger.error(f"Failed to persist events: {e}")

    # =========================================================================
    # Analytics
    # =========================================================================

    def get_experiment_stats(self, experiment_id: str) -> dict[str, Any]:
        """
        Get basic statistics for an experiment.

        Returns counts and conversion rates by variant.
        """
        exp = self._experiments.get(experiment_id)
        if not exp:
            return {}

        # Count assignments by variant
        variant_counts: dict[str, int] = {v.id: 0 for v in exp.variants}
        for user_assignments in self._assignments.values():
            if experiment_id in user_assignments:
                variant_id = user_assignments[experiment_id].variant_id
                variant_counts[variant_id] = variant_counts.get(variant_id, 0) + 1

        # Count events by variant and type
        event_counts: dict[str, dict[str, int]] = {v.id: {} for v in exp.variants}
        for event in self._events:
            if event.experiment_id == experiment_id:
                if event.variant_id not in event_counts:
                    event_counts[event.variant_id] = {}
                event_type = event.event_type
                event_counts[event.variant_id][event_type] = (
                    event_counts[event.variant_id].get(event_type, 0) + 1
                )

        # Calculate conversion rates
        variants: list[dict[str, Any]] = []
        for variant in exp.variants:
            users = variant_counts.get(variant.id, 0)
            exposures = event_counts.get(variant.id, {}).get("exposure", 0)
            conversions = event_counts.get(variant.id, {}).get("conversion", 0)

            conversion_rate = (conversions / exposures * 100) if exposures > 0 else 0

            variants.append(
                {
                    "id": variant.id,
                    "name": variant.name,
                    "weight": variant.weight,
                    "users": users,
                    "exposures": exposures,
                    "conversions": conversions,
                    "conversion_rate": round(conversion_rate, 2),
                }
            )

        return {
            "experiment_id": experiment_id,
            "name": exp.name,
            "status": exp.status.value,
            "total_users": sum(variant_counts.values()),
            "variants": variants,
        }

    def export_flags_config(self) -> dict[str, bool]:
        """
        Export experiment flags in a format compatible with FeatureFlagsService.

        Returns dict of flag_name -> enabled_for_treatment.
        """
        flags: dict[str, bool] = {}

        for exp in self._experiments.values():
            if exp.status == ExperimentStatus.RUNNING:
                # Export as feature flag (treatment = enabled)
                flag_name = f"ABTest_{exp.id}"
                flags[flag_name] = True

        return flags


# =============================================================================
# Default instance
# =============================================================================

_default_service: ABTestingService | None = None


def get_ab_testing_service() -> ABTestingService:
    """Get the default A/B testing service instance."""
    global _default_service
    if _default_service is None:
        _default_service = ABTestingService()
    return _default_service


def reset_ab_testing_service() -> None:
    """Reset the default service (for testing)."""
    global _default_service
    _default_service = None
