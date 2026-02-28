"""
LLM Provider Service for VoiceStudio.

This service provides a clean abstraction over LLM providers, keeping
the engine layer decoupled from the API routes (Clean Architecture).

Routes should use this service instead of importing from app.core.engines directly.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Any, Protocol

logger = logging.getLogger(__name__)


@dataclass
class ProviderInfo:
    """Information about an LLM provider."""

    name: str
    local: bool
    available: bool
    models: list[str] = None
    endpoint: str | None = None

    def __post_init__(self):
        if self.models is None:
            self.models = []

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "name": self.name,
            "local": self.local,
            "available": self.available,
            "models": self.models,
            "endpoint": self.endpoint,
        }


class ILLMProviderService(Protocol):
    """Interface for LLM provider service."""

    def get_available_providers(self) -> list[ProviderInfo]:
        """Get all available LLM providers."""
        ...

    def get_provider_info(self, name: str) -> ProviderInfo | None:
        """Get information about a specific provider."""
        ...

    def is_provider_available(self, name: str) -> bool:
        """Check if a provider is available."""
        ...


class LLMProviderService:
    """
    Service that manages LLM provider information.

    This service encapsulates access to LLM providers, allowing routes
    to query provider availability without directly importing from
    the engine layer.
    """

    def __init__(self):
        self._providers_cache: dict[str, ProviderInfo] | None = None
        self._cache_valid = False

    def _load_providers(self) -> dict[str, ProviderInfo]:
        """
        Load provider information from the engine layer.

        This is the only place that imports from app.core.engines.
        """
        providers: dict[str, ProviderInfo] = {}

        # Ollama (local)
        try:
            from app.core.engines.llm_local_adapter import OllamaLLMProvider

            provider = OllamaLLMProvider()
            providers["ollama"] = ProviderInfo(
                name="ollama",
                local=True,
                available=provider.is_available,
                models=getattr(provider, "available_models", []),
                endpoint=getattr(provider, "endpoint", None),
            )
        except ImportError:
            logger.debug("OllamaLLMProvider not available")
            providers["ollama"] = ProviderInfo(name="ollama", local=True, available=False)
        except Exception as e:
            logger.warning(f"Error loading Ollama provider: {e}")
            providers["ollama"] = ProviderInfo(name="ollama", local=True, available=False)

        # LocalAI (local)
        try:
            from app.core.engines.llm_local_adapter import LocalAILLMProvider

            provider = LocalAILLMProvider()
            providers["localai"] = ProviderInfo(
                name="localai",
                local=True,
                available=provider.is_available,
                models=getattr(provider, "available_models", []),
                endpoint=getattr(provider, "endpoint", None),
            )
        except ImportError:
            logger.debug("LocalAILLMProvider not available")
            providers["localai"] = ProviderInfo(name="localai", local=True, available=False)
        except Exception as e:
            logger.warning(f"Error loading LocalAI provider: {e}")
            providers["localai"] = ProviderInfo(name="localai", local=True, available=False)

        # OpenAI (remote)
        try:
            from app.core.engines.llm_openai_adapter import OpenAILLMProvider

            provider = OpenAILLMProvider()
            providers["openai"] = ProviderInfo(
                name="openai",
                local=False,
                available=provider.is_available,
                models=getattr(provider, "available_models", []),
            )
        except ImportError:
            logger.debug("OpenAILLMProvider not available")
            providers["openai"] = ProviderInfo(name="openai", local=False, available=False)
        except Exception as e:
            logger.warning(f"Error loading OpenAI provider: {e}")
            providers["openai"] = ProviderInfo(name="openai", local=False, available=False)

        return providers

    def _ensure_cache(self) -> None:
        """Ensure the provider cache is populated."""
        if not self._cache_valid or self._providers_cache is None:
            self._providers_cache = self._load_providers()
            self._cache_valid = True

    def invalidate_cache(self) -> None:
        """Invalidate the provider cache, forcing a refresh on next access."""
        self._cache_valid = False

    def get_available_providers(self) -> list[ProviderInfo]:
        """Get all available LLM providers."""
        self._ensure_cache()
        return list(self._providers_cache.values())

    def get_provider_info(self, name: str) -> ProviderInfo | None:
        """Get information about a specific provider."""
        self._ensure_cache()
        return self._providers_cache.get(name)

    def is_provider_available(self, name: str) -> bool:
        """Check if a provider is available."""
        info = self.get_provider_info(name)
        return info.available if info else False

    def get_providers_for_api(self) -> list[dict[str, Any]]:
        """
        Get provider information formatted for API responses.

        Returns a list of dictionaries suitable for JSON serialization.
        """
        return [p.to_dict() for p in self.get_available_providers()]

    def get_best_provider_instance(self) -> Any | None:
        """
        Get the best available LLM provider instance.

        Priority: Ollama (local) > LocalAI > OpenAI (cloud)

        Returns the actual provider instance for making LLM calls.
        This centralizes provider selection logic in the service layer.
        """
        # Try Ollama first (local, free)
        try:
            from app.core.engines.llm_local_adapter import OllamaLLMProvider

            provider = OllamaLLMProvider()
            if provider.is_available:
                logger.info("Using Ollama LLM provider (local)")
                return provider
        # ALLOWED: bare except - Provider not installed is expected, try next
        except ImportError:
            pass
        except Exception as e:
            logger.warning(f"Error checking Ollama provider: {e}")

        # Try LocalAI (local)
        try:
            from app.core.engines.llm_local_adapter import LocalAILLMProvider

            provider = LocalAILLMProvider()
            if provider.is_available:
                logger.info("Using LocalAI LLM provider (local)")
                return provider
        # ALLOWED: bare except - Provider not installed is expected, try next
        except ImportError:
            pass
        except Exception as e:
            logger.warning(f"Error checking LocalAI provider: {e}")

        # Fallback to OpenAI (cloud, requires API key)
        try:
            from app.core.engines.llm_openai_adapter import OpenAILLMProvider

            provider = OpenAILLMProvider()
            if provider.is_available:
                logger.info("Using OpenAI LLM provider (cloud)")
                return provider
        # ALLOWED: bare except - Provider not installed is expected, try next
        except ImportError:
            pass
        except Exception as e:
            logger.warning(f"Error checking OpenAI provider: {e}")

        return None

    def get_provider_instance_by_name(self, name: str) -> Any | None:
        """
        Get a specific provider instance by name.

        Returns the actual provider instance for making LLM calls.
        """
        try:
            if name == "ollama":
                from app.core.engines.llm_local_adapter import OllamaLLMProvider

                return OllamaLLMProvider()
            elif name == "localai":
                from app.core.engines.llm_local_adapter import LocalAILLMProvider

                return LocalAILLMProvider()
            elif name == "openai":
                from app.core.engines.llm_openai_adapter import OpenAILLMProvider

                return OpenAILLMProvider()
        except ImportError:
            logger.warning(f"Provider {name} not available (import error)")
        except Exception as e:
            logger.warning(f"Error getting provider {name}: {e}")

        return None


# Singleton instance
_provider_service: LLMProviderService | None = None


def get_llm_provider_service() -> LLMProviderService:
    """Get the singleton LLM provider service instance."""
    global _provider_service
    if _provider_service is None:
        _provider_service = LLMProviderService()
    return _provider_service
