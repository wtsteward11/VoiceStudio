"""
Model Registry Service — Phase 8 WS1

Centralized catalog of model artifacts with version tracking, active/archived
status, and rollback support. Local-first, JSON-backed persistence.

Builds on:
- engines/models.index.json (seed catalog)
- app.core.engines.manifest_loader (engine discovery)
- backend.services.model_preflight (validation)
"""

from __future__ import annotations

import json
import logging
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from backend.config.path_config import get_path

logger = logging.getLogger(__name__)


@dataclass
class ModelArtifact:
    """A single model artifact in the registry."""

    engine_id: str
    model_name: str
    version: str
    path: str
    size_bytes: int = 0
    sha256: str = ""
    status: str = "available"  # active | available | archived
    registered_at: str = ""
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for serialization."""
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> ModelArtifact:
        """Create from dictionary."""
        return cls(
            engine_id=data["engine_id"],
            model_name=data["model_name"],
            version=data.get("version", "1.0"),
            path=data["path"],
            size_bytes=data.get("size_bytes", 0),
            sha256=data.get("sha256", ""),
            status=data.get("status", "available"),
            registered_at=data.get("registered_at", ""),
            metadata=data.get("metadata", {}),
        )


@dataclass
class EngineModelState:
    """State of models for a single engine."""

    engine_id: str
    active_model: str | None = None  # model_name of active version
    active_version: str | None = None
    previous_model: str | None = None  # for rollback
    previous_version: str | None = None
    artifacts: list[ModelArtifact] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "engine_id": self.engine_id,
            "active_model": self.active_model,
            "active_version": self.active_version,
            "previous_model": self.previous_model,
            "previous_version": self.previous_version,
            "artifacts": [a.to_dict() for a in self.artifacts],
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> EngineModelState:
        """Create from dictionary."""
        artifacts = [ModelArtifact.from_dict(a) for a in data.get("artifacts", [])]
        return cls(
            engine_id=data["engine_id"],
            active_model=data.get("active_model"),
            active_version=data.get("active_version"),
            previous_model=data.get("previous_model"),
            previous_version=data.get("previous_version"),
            artifacts=artifacts,
        )


class ModelRegistryService:
    """
    Centralized model lifecycle management.

    - Catalog of model artifacts per engine
    - Active vs available vs archived versions
    - Activate/rollback operations
    - Persists to data/model_registry.json (local-first)
    """

    def __init__(self, data_dir: Path | None = None):
        """
        Initialize the model registry.

        Args:
            data_dir: Directory for registry JSON. Defaults to get_path("data").
        """
        self._data_dir = data_dir or get_path("data")
        self._registry_path = self._data_dir / "model_registry.json"
        self._registry: dict[str, EngineModelState] = {}
        self._lock = None
        self._load()

    def _load(self) -> None:
        """Load registry from disk."""
        if self._registry_path.exists():
            try:
                with open(self._registry_path, encoding="utf-8") as f:
                    data = json.load(f)
                self._registry = {}
                for engine_id, state_data in data.get("engines", {}).items():
                    self._registry[engine_id] = EngineModelState.from_dict(
                        {"engine_id": engine_id, **state_data}
                    )
                logger.info(f"Model registry loaded: {len(self._registry)} engines")
            except (json.JSONDecodeError, OSError) as e:
                logger.warning(f"Failed to load model registry: {e}")
                self._registry = {}
        else:
            self._registry = {}
            self._seed_from_config()
            self._seed_from_index()

    def _seed_from_config(self) -> None:
        """Seed registry from engines/config.json installed engines (Phase 10 WS2)."""
        config_path = Path(__file__).resolve().parents[2] / "engines" / "config.json"
        if not config_path.exists():
            return
        try:
            with open(config_path, encoding="utf-8") as f:
                data = json.load(f)
        except (json.JSONDecodeError, OSError) as e:
            logger.debug(f"Could not load engines/config.json: {e}")
            return

        installed = data.get("installed", [])
        if not installed:
            return

        models_path = get_path("models")
        for engine_id in installed:
            if engine_id in self._registry:
                continue
            engine_path = models_path / engine_id
            artifact = ModelArtifact(
                engine_id=engine_id,
                model_name="default",
                version="1.0",
                path=str(engine_path),
                size_bytes=0,
                sha256="",
                status="available",
                registered_at=datetime.now(timezone.utc).isoformat(),
                metadata={"source": "engines/config.json"},
            )
            self._registry[engine_id] = EngineModelState(
                engine_id=engine_id,
                artifacts=[artifact],
            )
        self._save()
        logger.info(f"Model registry seeded from config: {len(installed)} engines")

    def _seed_from_index(self) -> None:
        """Seed registry from engines/models.index.json if present (adds to existing)."""
        index_path = Path("engines/models.index.json")
        if not index_path.exists():
            return
        try:
            with open(index_path, encoding="utf-8") as f:
                data = json.load(f)
            for entry in data.get("models", []):
                engine = entry.get("engine", "unknown")
                name = entry.get("name", "default")
                version = entry.get("updated", "1.0")[:10] if entry.get("updated") else "1.0"
                path = str(get_path("models") / engine / name)
                artifact = ModelArtifact(
                    engine_id=engine,
                    model_name=name,
                    version=version,
                    path=path,
                    size_bytes=entry.get("size_bytes", 0),
                    sha256=entry.get("sha256", ""),
                    status="available",
                    registered_at=datetime.now(timezone.utc).isoformat(),
                    metadata={"source": "models.index.json"},
                )
                if engine not in self._registry:
                    self._registry[engine] = EngineModelState(engine_id=engine, artifacts=[])
                self._registry[engine].artifacts.append(artifact)
            self._save()
        except (json.JSONDecodeError, OSError) as e:
            logger.debug(f"Could not seed from models.index.json: {e}")

    def _save(self) -> None:
        """Save registry to disk atomically."""
        self._registry_path.parent.mkdir(parents=True, exist_ok=True)
        tmp_path = self._registry_path.with_suffix(".json.tmp")
        try:
            data = {
                "version": "1.0",
                "updated_at": datetime.now(timezone.utc).isoformat(),
                "engines": {
                    eid: {
                        "active_model": state.active_model,
                        "active_version": state.active_version,
                        "previous_model": state.previous_model,
                        "previous_version": state.previous_version,
                        "artifacts": [a.to_dict() for a in state.artifacts],
                    }
                    for eid, state in self._registry.items()
                },
            }
            with open(tmp_path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
            tmp_path.replace(self._registry_path)
        except OSError as e:
            logger.error(f"Failed to save model registry: {e}")
        finally:
            if tmp_path.exists():
                try:
                    tmp_path.unlink()
                except OSError as e:
                    logger.debug("Could not remove temp file %s: %s", tmp_path, e)

    def list_models(self, engine_id: str | None = None) -> list[dict[str, Any]]:
        """
        List all model artifacts, optionally filtered by engine.

        Returns:
            List of model dicts with engine_id, model_name, version, path,
            size_bytes, status, etc.
        """
        result: list[dict[str, Any]] = []
        engines_to_include = (
            self._registry[e] for e in self._registry if engine_id is None or e == engine_id
        )
        for state in engines_to_include:
            for art in state.artifacts:
                result.append(
                    {
                        **art.to_dict(),
                        "active": (
                            art.model_name == state.active_model
                            and art.version == state.active_version
                        ),
                    }
                )
        return result

    def get_engine_models(self, engine_id: str) -> dict[str, Any]:
        """
        Get model state for a specific engine.

        Returns:
            Dict with engine_id, active_model, active_version, previous_model,
            previous_version, artifacts list.
        """
        if engine_id not in self._registry:
            return {
                "engine_id": engine_id,
                "active_model": None,
                "active_version": None,
                "previous_model": None,
                "previous_version": None,
                "artifacts": [],
            }
        return self._registry[engine_id].to_dict()

    def activate_model(
        self,
        engine_id: str,
        model_name: str,
        version: str | None = None,
    ) -> dict[str, Any]:
        """
        Activate a model version for an engine.

        Sets previous_model to current active before switching.
        Validates target exists via model_preflight when applicable.

        Returns:
            Updated engine state dict.
        """
        if engine_id not in self._registry:
            self._registry[engine_id] = EngineModelState(engine_id=engine_id)

        state = self._registry[engine_id]
        candidates = [
            a
            for a in state.artifacts
            if a.model_name == model_name and (version is None or a.version == version)
        ]
        if not candidates:
            # Register as new artifact if path exists
            models_path = get_path("models")
            candidate_path = models_path / engine_id / model_name
            if candidate_path.exists():
                artifact = ModelArtifact(
                    engine_id=engine_id,
                    model_name=model_name,
                    version=version or "1.0",
                    path=str(candidate_path),
                    size_bytes=0,
                    sha256="",
                    status="available",
                    registered_at=datetime.now(timezone.utc).isoformat(),
                )
                state.artifacts.append(artifact)
                candidates = [artifact]
            else:
                raise ValueError(
                    f"Model {engine_id}/{model_name} not found in registry "
                    "and path does not exist"
                )

        target = candidates[0]
        state.previous_model = state.active_model
        state.previous_version = state.active_version
        state.active_model = target.model_name
        state.active_version = target.version
        for a in state.artifacts:
            a.status = (
                "active"
                if a.model_name == target.model_name and a.version == target.version
                else ("archived" if a.status == "active" else a.status)
            )
        self._save()
        logger.info(f"Activated model {engine_id}/{model_name} v{target.version}")
        return self.get_engine_models(engine_id)

    def rollback(self, engine_id: str) -> dict[str, Any]:
        """
        Rollback to the previous known-good model version.

        Returns:
            Updated engine state after rollback.
        """
        if engine_id not in self._registry:
            raise ValueError(f"Engine {engine_id} not in registry")

        state = self._registry[engine_id]
        if not state.previous_model or not state.previous_version:
            raise ValueError(f"No previous model to rollback to for {engine_id}")

        return self.activate_model(engine_id, state.previous_model, state.previous_version)

    def register_artifact(
        self,
        engine_id: str,
        model_name: str,
        path: str,
        version: str = "1.0",
        size_bytes: int = 0,
        sha256: str = "",
        metadata: dict[str, Any] | None = None,
    ) -> ModelArtifact:
        """
        Register a new model artifact without activating it.

        Returns:
            The registered ModelArtifact.
        """
        if engine_id not in self._registry:
            self._registry[engine_id] = EngineModelState(engine_id=engine_id)

        state = self._registry[engine_id]
        existing = [
            a for a in state.artifacts if a.model_name == model_name and a.version == version
        ]
        if existing:
            return existing[0]

        artifact = ModelArtifact(
            engine_id=engine_id,
            model_name=model_name,
            version=version,
            path=path,
            size_bytes=size_bytes,
            sha256=sha256,
            status="available",
            registered_at=datetime.now(timezone.utc).isoformat(),
            metadata=metadata or {},
        )
        state.artifacts.append(artifact)
        self._save()
        return artifact

    def get_active_model(self, engine_id: str) -> tuple[str | None, str | None]:
        """
        Get the active model name and version for an engine.

        Returns:
            (model_name, version) or (None, None) if none active.
        """
        if engine_id not in self._registry:
            return (None, None)
        state = self._registry[engine_id]
        return (state.active_model, state.active_version)


_registry_instance: ModelRegistryService | None = None


def get_model_registry_service(
    data_dir: Path | None = None,
) -> ModelRegistryService:
    """Get or create the model registry service singleton."""
    global _registry_instance
    if _registry_instance is None:
        _registry_instance = ModelRegistryService(data_dir=data_dir)
    return _registry_instance
