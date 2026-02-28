"""
JSON-RPC Protocol for Plugin Subprocess Communication.

Phase 4 Enhancement: Defines message format and RPC conventions
for bidirectional communication between host and plugin subprocess.

Protocol is based on JSON-RPC 2.0 with VoiceStudio-specific extensions.
"""

import json
import time
from dataclasses import dataclass, field
from enum import Enum, IntEnum
from typing import Any, Dict, List, Optional, Union


class MessageType(str, Enum):
    """Types of messages in the protocol."""

    REQUEST = "request"
    RESPONSE = "response"
    NOTIFICATION = "notification"
    ERROR = "error"


class ErrorCode(IntEnum):
    """Standard JSON-RPC and VoiceStudio-specific error codes."""

    # Standard JSON-RPC errors
    PARSE_ERROR = -32700
    INVALID_REQUEST = -32600
    METHOD_NOT_FOUND = -32601
    INVALID_PARAMS = -32602
    INTERNAL_ERROR = -32603

    # VoiceStudio-specific errors (-32000 to -32099)
    PERMISSION_DENIED = -32000
    TIMEOUT = -32001
    PLUGIN_ERROR = -32002
    RESOURCE_LIMIT = -32003
    NOT_INITIALIZED = -32004
    ALREADY_INITIALIZED = -32005
    INVALID_STATE = -32006
    SHUTDOWN_IN_PROGRESS = -32007
    HOST_UNAVAILABLE = -32008
    SERIALIZATION_ERROR = -32009


@dataclass
class RPCError:
    """JSON-RPC error object."""

    code: int
    message: str
    data: Optional[Any] = None

    def to_dict(self) -> Dict[str, Any]:
        """Convert to JSON-RPC error format."""
        result = {"code": self.code, "message": self.message}
        if self.data is not None:
            result["data"] = self.data
        return result

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "RPCError":
        """Create from dictionary."""
        return cls(
            code=data["code"],
            message=data["message"],
            data=data.get("data"),
        )

    @classmethod
    def parse_error(cls, detail: Optional[str] = None) -> "RPCError":
        """Create a parse error."""
        return cls(ErrorCode.PARSE_ERROR, "Parse error", detail)

    @classmethod
    def invalid_request(cls, detail: Optional[str] = None) -> "RPCError":
        """Create an invalid request error."""
        return cls(ErrorCode.INVALID_REQUEST, "Invalid request", detail)

    @classmethod
    def method_not_found(cls, method: str) -> "RPCError":
        """Create a method not found error."""
        return cls(ErrorCode.METHOD_NOT_FOUND, f"Method not found: {method}")

    @classmethod
    def invalid_params(cls, detail: Optional[str] = None) -> "RPCError":
        """Create an invalid params error."""
        return cls(ErrorCode.INVALID_PARAMS, "Invalid params", detail)

    @classmethod
    def internal_error(cls, detail: Optional[str] = None) -> "RPCError":
        """Create an internal error."""
        return cls(ErrorCode.INTERNAL_ERROR, "Internal error", detail)

    @classmethod
    def permission_denied(cls, permission: str) -> "RPCError":
        """Create a permission denied error."""
        return cls(
            ErrorCode.PERMISSION_DENIED,
            f"Permission denied: {permission}",
            {"required_permission": permission},
        )

    @classmethod
    def timeout(cls, timeout_ms: int) -> "RPCError":
        """Create a timeout error."""
        return cls(
            ErrorCode.TIMEOUT,
            f"Request timed out after {timeout_ms}ms",
            {"timeout_ms": timeout_ms},
        )


@dataclass
class Message:
    """Base message class for protocol communication."""

    jsonrpc: str = "2.0"
    timestamp: float = field(default_factory=time.time)

    def to_json(self) -> str:
        """Serialize to JSON string."""
        return json.dumps(self.to_dict())

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "jsonrpc": self.jsonrpc,
            "_timestamp": self.timestamp,
        }

    @classmethod
    def from_json(cls, data: str) -> "Message":
        """Parse from JSON string."""
        try:
            parsed = json.loads(data)
            return cls.from_dict(parsed)
        except json.JSONDecodeError as e:
            raise ValueError(f"Invalid JSON: {e}") from e

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "Message":
        """Parse from dictionary to appropriate message type."""
        if "method" in data and "id" in data:
            return Request.from_dict(data)
        elif "method" in data and "id" not in data:
            return Notification.from_dict(data)
        elif "result" in data or "error" in data:
            return Response.from_dict(data)
        else:
            raise ValueError("Unknown message type")


@dataclass
class Request(Message):
    """JSON-RPC request message."""

    id: Union[int, str] = 0
    method: str = ""
    params: Optional[Union[Dict[str, Any], List[Any]]] = None

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        result = {
            "jsonrpc": self.jsonrpc,
            "id": self.id,
            "method": self.method,
        }
        if self.params is not None:
            result["params"] = self.params
        result["_timestamp"] = self.timestamp
        return result

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "Request":
        """Create from dictionary."""
        return cls(
            jsonrpc=data.get("jsonrpc", "2.0"),
            id=data["id"],
            method=data["method"],
            params=data.get("params"),
            timestamp=data.get("_timestamp", time.time()),
        )


@dataclass
class Response(Message):
    """JSON-RPC response message."""

    id: Union[int, str] = 0
    result: Optional[Any] = None
    error: Optional[RPCError] = None

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        result = {
            "jsonrpc": self.jsonrpc,
            "id": self.id,
        }
        if self.error is not None:
            result["error"] = self.error.to_dict()
        else:
            result["result"] = self.result
        result["_timestamp"] = self.timestamp
        return result

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "Response":
        """Create from dictionary."""
        error = None
        if "error" in data:
            error = RPCError.from_dict(data["error"])
        return cls(
            jsonrpc=data.get("jsonrpc", "2.0"),
            id=data.get("id", 0),
            result=data.get("result"),
            error=error,
            timestamp=data.get("_timestamp", time.time()),
        )

    @classmethod
    def success(cls, request_id: Union[int, str], result: Any = None) -> "Response":
        """Create a success response."""
        return cls(id=request_id, result=result)

    @classmethod
    def failure(cls, request_id: Union[int, str], error: RPCError) -> "Response":
        """Create an error response."""
        return cls(id=request_id, error=error)


@dataclass
class Notification(Message):
    """JSON-RPC notification (request without id, no response expected)."""

    method: str = ""
    params: Optional[Union[Dict[str, Any], List[Any]]] = None

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        result = {
            "jsonrpc": self.jsonrpc,
            "method": self.method,
        }
        if self.params is not None:
            result["params"] = self.params
        result["_timestamp"] = self.timestamp
        return result

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "Notification":
        """Create from dictionary."""
        return cls(
            jsonrpc=data.get("jsonrpc", "2.0"),
            method=data["method"],
            params=data.get("params"),
            timestamp=data.get("_timestamp", time.time()),
        )


# Well-known method names for host API
class HostMethods:
    """Standard method names for host-to-plugin and plugin-to-host calls."""

    # Lifecycle methods (host -> plugin)
    INITIALIZE = "plugin.initialize"
    SHUTDOWN = "plugin.shutdown"
    ACTIVATE = "plugin.activate"
    DEACTIVATE = "plugin.deactivate"

    # Capability methods (host -> plugin)
    GET_CAPABILITIES = "plugin.getCapabilities"
    INVOKE_CAPABILITY = "plugin.invokeCapability"

    # Audio methods (plugin -> host)
    AUDIO_PLAY = "host.audio.play"
    AUDIO_STOP = "host.audio.stop"
    AUDIO_GET_DEVICES = "host.audio.getDevices"
    AUDIO_PROCESS = "host.audio.process"

    # UI methods (plugin -> host)
    UI_NOTIFY = "host.ui.notify"
    UI_SHOW_DIALOG = "host.ui.showDialog"
    UI_UPDATE_PANEL = "host.ui.updatePanel"

    # Storage methods (plugin -> host)
    STORAGE_GET = "host.storage.get"
    STORAGE_SET = "host.storage.set"
    STORAGE_DELETE = "host.storage.delete"

    # Settings methods (plugin -> host)
    SETTINGS_GET = "host.settings.get"
    SETTINGS_SET = "host.settings.set"

    # Engine methods (plugin -> host)
    ENGINE_INVOKE = "host.engine.invoke"
    ENGINE_LIST = "host.engine.list"

    # Notifications (bidirectional)
    LOG = "notify.log"
    PROGRESS = "notify.progress"
    HEARTBEAT = "notify.heartbeat"


def encode_message(message: Message) -> bytes:
    """Encode a message for transmission (length-prefixed JSON)."""
    json_data = message.to_json().encode("utf-8")
    length = len(json_data)
    # 4-byte length prefix (big-endian)
    return length.to_bytes(4, byteorder="big") + json_data


def decode_message_header(data: bytes) -> int:
    """Decode the length prefix from message header."""
    if len(data) < 4:
        raise ValueError("Insufficient data for header")
    return int.from_bytes(data[:4], byteorder="big")
