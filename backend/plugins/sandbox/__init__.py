"""
VoiceStudio Plugin Subprocess Sandbox.

Phase 4 Enhancement: Lane B subprocess isolation model.
Phase 5A Enhancement: Resource monitoring, network policy, storage isolation,
    and optional Docker container-based isolation.
Phase 5D Enhancement: Crash recovery with auto-restart, exponential backoff,
    and state preservation.

This module provides subprocess-based isolation for plugins requiring
stronger security boundaries. Plugins run in separate Python processes
with controlled communication through a JSON-RPC bridge. Optionally,
plugins can run in Docker containers for stronger isolation.

Architecture:
    ┌─────────────────────────────────────────────────────────────┐
    │                     VoiceStudio Host                        │
    │  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
    │  │ PluginRunner │───>│ IPCBridge    │<───│ HostAPI      │  │
    │  │ (spawns)     │    │ (JSON-RPC)   │    │ (implements) │  │
    │  └──────────────┘    └──────────────┘    └──────────────┘  │
    │         │                   │                    │          │
    │         │    ┌──────────────────────────────┐    │          │
    │         └───>│ ResourceMonitor (Phase 5A)  │<───┘          │
    │              │ (psutil-based enforcement)  │               │
    │              └──────────────────────────────┘               │
    └─────────────────────────────────────────────────────────────┘
              │    stdin/stdout   │
              ▼                   ▼
    ┌─────────────────────────────────────────────────────────────┐
    │                   Plugin Subprocess                          │
    │  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
    │  │ PluginBridge │<───│ Message Loop │───>│ Plugin Code  │  │
    │  │ (client)     │    │ (protocol)   │    │ (isolated)   │  │
    │  └──────────────┘    └──────────────┘    └──────────────┘  │
    └─────────────────────────────────────────────────────────────┘

Components:
    - PluginRunner: Spawns and manages plugin subprocesses
    - DockerRunner: Optional Docker container-based runner (Phase 5A)
    - IPCBridge: Bidirectional JSON-RPC communication
    - HostAPI: Services exposed to plugins (audio, UI, settings, etc.)
    - Protocol: Message format and RPC conventions
    - ResourceMonitor: CPU/memory enforcement via psutil (Phase 5A)
    - NetworkPolicyEnforcer: Network egress control (Phase 5A)
    - StorageManager: Per-plugin isolated storage (Phase 5A)

Security Features:
    - Process isolation (separate memory space)
    - Permission-gated API access
    - Timeout enforcement
    - Resource monitoring and enforcement (Phase 5A)
    - Network egress policy enforcement (Phase 5A)
    - Per-plugin storage isolation with path validation (Phase 5A)
"""

from .bridge import BridgeState, IPCBridge
from .crash_recovery import (
    BackoffConfig,
    CircuitBreaker,
    CircuitState,
    CrashEvent,
    CrashRecoveryManager,
    PluginState,
    RecoveryConfig,
    RestartPolicy,
    get_all_recovery_stats,
    get_recovery_manager,
    remove_recovery_manager,
)
from .docker_runner import (
    DOCKER_AVAILABLE,
    ContainerState,
    DockerRunner,
    DockerRunnerConfig,
    DockerRunnerManager,
    get_docker_manager,
    reset_docker_manager,
)
from .host_api import HostAPI, HostAPIRegistry
from .network_policy import (
    ConnectionAttempt,
    ConnectionType,
    NetworkAction,
    NetworkPermissions,
    NetworkPolicyEnforcer,
    SocketWrapper,
)
from .network_policy import create_enforcer as create_network_enforcer
from .network_policy import (
    generate_socket_bootstrap,
)
from .network_policy import get_enforcer as get_network_enforcer
from .network_policy import remove_enforcer as remove_network_enforcer
from .permissions import (
    PermissionAuditor,
    PermissionCategory,
    PermissionCheck,
    PermissionEnforcer,
    PermissionLevel,
    PermissionRegistry,
    get_auditor,
    get_registry,
    permission_guard,
)
from .protocol import (
    ErrorCode,
    Message,
    MessageType,
    Notification,
    Request,
    Response,
    RPCError,
)
from .resource_monitor import (
    ResourceLimits,
    ResourceMonitor,
    ResourceMonitorRegistry,
    ResourceSnapshot,
    ViolationAction,
    ViolationEvent,
    ViolationType,
    get_resource_monitor_registry,
)
from .runner import PluginRunner, ProcessState, RunnerConfig
from .storage_isolation import (
    PathValidationError,
    PluginStorage,
    QuotaExceededError,
    StorageManager,
    StorageQuota,
    StorageType,
    StorageUsage,
    get_plugin_storage,
    get_storage_manager,
)
from .workspace_isolation import (
    PluginWorkspaceConfig,
    Workspace,
    WorkspaceError,
    WorkspaceExistsError,
    WorkspaceLockError,
    WorkspaceManager,
    WorkspaceMetadata,
    WorkspaceNotFoundError,
    WorkspaceState,
    get_active_workspace,
    get_plugin_data_dir,
    get_workspace_manager,
    reset_workspace_manager,
)

__all__ = [
    "DOCKER_AVAILABLE",
    "BackoffConfig",
    "BridgeState",
    "CircuitBreaker",
    "CircuitState",
    "ConnectionAttempt",
    "ConnectionType",
    "ContainerState",
    "CrashEvent",
    # Crash Recovery (Phase 5D)
    "CrashRecoveryManager",
    # Docker Runner (Phase 5A)
    "DockerRunner",
    "DockerRunnerConfig",
    "DockerRunnerManager",
    "ErrorCode",
    # Host API
    "HostAPI",
    "HostAPIRegistry",
    # Bridge
    "IPCBridge",
    # Protocol
    "Message",
    "MessageType",
    "NetworkAction",
    # Network Policy (Phase 5A)
    "NetworkPermissions",
    "NetworkPolicyEnforcer",
    "Notification",
    "PathValidationError",
    "PermissionAuditor",
    "PermissionCategory",
    "PermissionCheck",
    # Permissions
    "PermissionEnforcer",
    "PermissionLevel",
    "PermissionRegistry",
    # Runner
    "PluginRunner",
    "PluginState",
    # Storage Isolation (Phase 5A)
    "PluginStorage",
    "PluginWorkspaceConfig",
    "ProcessState",
    "QuotaExceededError",
    "RPCError",
    "RecoveryConfig",
    "Request",
    "ResourceLimits",
    # Resource Monitoring (Phase 5A)
    "ResourceMonitor",
    "ResourceMonitorRegistry",
    "ResourceSnapshot",
    "Response",
    "RestartPolicy",
    "RunnerConfig",
    "SocketWrapper",
    "StorageManager",
    "StorageQuota",
    "StorageType",
    "StorageUsage",
    "ViolationAction",
    "ViolationEvent",
    "ViolationType",
    # Workspace Isolation (Phase 5A)
    "Workspace",
    "WorkspaceError",
    "WorkspaceExistsError",
    "WorkspaceLockError",
    "WorkspaceManager",
    "WorkspaceMetadata",
    "WorkspaceNotFoundError",
    "WorkspaceState",
    "create_network_enforcer",
    "generate_socket_bootstrap",
    "get_active_workspace",
    "get_all_recovery_stats",
    "get_auditor",
    "get_docker_manager",
    "get_network_enforcer",
    "get_plugin_data_dir",
    "get_plugin_storage",
    "get_recovery_manager",
    "get_registry",
    "get_resource_monitor_registry",
    "get_storage_manager",
    "get_workspace_manager",
    "permission_guard",
    "remove_network_enforcer",
    "remove_recovery_manager",
    "reset_docker_manager",
    "reset_workspace_manager",
]
