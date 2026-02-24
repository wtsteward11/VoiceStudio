"""
Authentication and Authorization System

Provides API key authentication, JWT token support, role-based access control,
and permission management for VoiceStudio API.
"""

from __future__ import annotations

import hashlib
import logging
import os
import secrets
from datetime import datetime, timedelta
from enum import Enum
from typing import Any
from uuid import uuid4

logger = logging.getLogger(__name__)

# Try to import bcrypt for password hashing
try:
    import bcrypt

    HAS_BCRYPT = True
except ImportError:
    HAS_BCRYPT = False
    logger.warning("bcrypt not available. " "Password hashing will use SHA256 (less secure).")

try:
    import jwt
    from jwt import PyJWTError

    HAS_JWT = True
except ImportError:
    HAS_JWT = False
    PyJWTError = Exception

# Try to import secure storage
try:
    from app.core.security.secure_storage import SecureStorage

    HAS_SECURE_STORAGE = True
except ImportError:
    HAS_SECURE_STORAGE = False

logger = logging.getLogger(__name__)

# JWT Configuration
# Security: In production, JWT_SECRET_KEY MUST be set to a stable secret.
# In development, use a stable dev secret to avoid session invalidation on restart.
_jwt_secret_env = os.getenv("JWT_SECRET_KEY")
if _jwt_secret_env:
    JWT_SECRET_KEY = _jwt_secret_env
elif os.getenv("VOICESTUDIO_ENV") == "production":
    raise ValueError(
        "JWT_SECRET_KEY environment variable must be set in production. "
        'Generate with: python -c "import secrets; print(secrets.token_urlsafe(32))"'
    )
else:
    # Development mode: use stable secret to preserve sessions across restarts
    JWT_SECRET_KEY = "voicestudio-dev-secret-do-not-use-in-production"
    logger.warning("Using development JWT secret. Set JWT_SECRET_KEY env var for production.")
JWT_ALGORITHM = "HS256"
JWT_ACCESS_TOKEN_EXPIRE_MINUTES = 60 * 24  # 24 hours
JWT_REFRESH_TOKEN_EXPIRE_DAYS = 30  # 30 days

# API Key Configuration
API_KEY_PREFIX = "vs_"
API_KEY_LENGTH = 32


class UserRole(str, Enum):
    """User roles for role-based access control."""

    ADMIN = "admin"
    USER = "user"
    GUEST = "guest"
    SERVICE = "service"  # For service-to-service authentication


class Permission(str, Enum):
    """API permissions."""

    # Profile permissions
    PROFILE_READ = "profile:read"
    PROFILE_WRITE = "profile:write"
    PROFILE_DELETE = "profile:delete"

    # Project permissions
    PROJECT_READ = "project:read"
    PROJECT_WRITE = "project:write"
    PROJECT_DELETE = "project:delete"

    # Synthesis permissions
    SYNTHESIS_CREATE = "synthesis:create"
    SYNTHESIS_READ = "synthesis:read"

    # Training permissions
    TRAINING_CREATE = "training:create"
    TRAINING_READ = "training:read"
    TRAINING_DELETE = "training:delete"

    # Engine permissions
    ENGINE_USE = "engine:use"
    ENGINE_ADMIN = "engine:admin"

    # System permissions
    SYSTEM_ADMIN = "system:admin"
    SYSTEM_READ = "system:read"
    SYSTEM_WRITE = "system:write"


# Role to permissions mapping
ROLE_PERMISSIONS: dict[UserRole, set[Permission]] = {
    UserRole.ADMIN: {
        Permission.PROFILE_READ,
        Permission.PROFILE_WRITE,
        Permission.PROFILE_DELETE,
        Permission.PROJECT_READ,
        Permission.PROJECT_WRITE,
        Permission.PROJECT_DELETE,
        Permission.SYNTHESIS_CREATE,
        Permission.SYNTHESIS_READ,
        Permission.TRAINING_CREATE,
        Permission.TRAINING_READ,
        Permission.TRAINING_DELETE,
        Permission.ENGINE_USE,
        Permission.ENGINE_ADMIN,
        Permission.SYSTEM_ADMIN,
        Permission.SYSTEM_READ,
        Permission.SYSTEM_WRITE,
    },
    UserRole.USER: {
        Permission.PROFILE_READ,
        Permission.PROFILE_WRITE,
        Permission.PROJECT_READ,
        Permission.PROJECT_WRITE,
        Permission.SYNTHESIS_CREATE,
        Permission.SYNTHESIS_READ,
        Permission.TRAINING_CREATE,
        Permission.TRAINING_READ,
        Permission.ENGINE_USE,
        Permission.SYSTEM_READ,
    },
    UserRole.GUEST: {
        Permission.PROFILE_READ,
        Permission.PROJECT_READ,
        Permission.SYNTHESIS_READ,
        Permission.SYSTEM_READ,
    },
    UserRole.SERVICE: {
        Permission.SYNTHESIS_CREATE,
        Permission.SYNTHESIS_READ,
        Permission.ENGINE_USE,
        Permission.SYSTEM_READ,
    },
}


class User:
    """User model for authentication."""

    def __init__(
        self,
        user_id: str,
        username: str,
        email: str | None = None,
        role: UserRole = UserRole.USER,
        api_key: str | None = None,
        password_hash: str | None = None,
        permissions: set[Permission] | None = None,
        is_active: bool = True,
        created_at: datetime | None = None,
    ):
        self.user_id = user_id
        self.username = username
        self.email = email
        self.role = role
        self.api_key = api_key
        self.password_hash = password_hash
        self.permissions = permissions or ROLE_PERMISSIONS.get(role, set())
        self.is_active = is_active
        self.created_at = created_at or datetime.utcnow()
        self.last_login: datetime | None = None

    def has_permission(self, permission: Permission) -> bool:
        """Check if user has a specific permission."""
        if not self.is_active:
            return False
        return permission in self.permissions

    def has_any_permission(self, permissions: set[Permission]) -> bool:
        """Check if user has any of the specified permissions."""
        if not self.is_active:
            return False
        return bool(self.permissions & permissions)

    def has_all_permissions(self, permissions: set[Permission]) -> bool:
        """Check if user has all of the specified permissions."""
        if not self.is_active:
            return False
        return permissions.issubset(self.permissions)


class APIKeyManager:
    """Manages API keys for authentication."""

    def __init__(self):
        """Initialize API key manager."""
        self._api_keys: dict[str, User] = {}
        self._users: dict[str, User] = {}
        self._secure_storage = SecureStorage() if HAS_SECURE_STORAGE else None

    def generate_api_key(self) -> str:
        """Generate a new API key."""
        random_part = secrets.token_urlsafe(API_KEY_LENGTH)
        return f"{API_KEY_PREFIX}{random_part}"

    def hash_api_key(self, api_key: str) -> str:
        """Hash an API key for storage."""
        return hashlib.sha256(api_key.encode()).hexdigest()

    def hash_password(self, password: str) -> str:
        """Hash a password for storage using bcrypt or SHA256 fallback."""
        if HAS_BCRYPT:
            # Use bcrypt for secure password hashing
            salt = bcrypt.gensalt()
            return str(bcrypt.hashpw(password.encode("utf-8"), salt).decode("utf-8"))
        else:
            # Fallback to SHA256 (less secure, but better than plain text)
            logger.warning(
                "Using SHA256 for password hashing. " "Install bcrypt for better security."
            )
            return hashlib.sha256(password.encode()).hexdigest()

    def verify_password(self, password: str, password_hash: str) -> bool:
        """Verify a password against its hash."""
        if not password or not password_hash:
            return False

        if HAS_BCRYPT:
            try:
                # Try bcrypt verification first
                return bool(bcrypt.checkpw(password.encode("utf-8"), password_hash.encode("utf-8")))
            except (ValueError, TypeError):
                # If bcrypt fails, try SHA256 fallback
                return hashlib.sha256(password.encode()).hexdigest() == password_hash
        else:
            # Fallback to SHA256 comparison
            return hashlib.sha256(password.encode()).hexdigest() == password_hash

    def create_user(
        self,
        username: str,
        email: str | None = None,
        role: UserRole = UserRole.USER,
        password: str | None = None,
        generate_api_key: bool = True,
    ) -> tuple[User, str | None]:
        """
        Create a new user.

        Returns:
            Tuple of (User, API key if generated)
        """
        user_id = str(uuid4())
        api_key = None

        if generate_api_key:
            api_key = self.generate_api_key()

        password_hash = None
        if password:
            password_hash = self.hash_password(password)

        user = User(
            user_id=user_id,
            username=username,
            email=email,
            role=role,
            api_key=self.hash_api_key(api_key) if api_key else None,
            password_hash=password_hash,
        )

        self._users[user_id] = user
        if api_key:
            self._api_keys[self.hash_api_key(api_key)] = user

        return user, api_key

    def authenticate_password(self, username: str, password: str) -> User | None:
        """Authenticate using username and password."""
        if not username or not password:
            return None

        # Find user by username
        user = self.get_user(username)
        if not user or not user.is_active:
            return None

        # Verify password
        if not user.password_hash:
            # User doesn't have a password set
            return None

        if not self.verify_password(password, user.password_hash):
            return None

        return user

    def authenticate_api_key(self, api_key: str) -> User | None:
        """Authenticate using API key."""
        if not api_key or not api_key.startswith(API_KEY_PREFIX):
            return None

        hashed_key = self.hash_api_key(api_key)
        user = self._api_keys.get(hashed_key)

        if user and user.is_active:
            user.last_login = datetime.utcnow()
            return user

        return None

    def get_user(self, user_id: str) -> User | None:
        """Get user by ID."""
        return self._users.get(user_id)

    def revoke_api_key(self, api_key: str) -> bool:
        """Revoke an API key."""
        hashed_key = self.hash_api_key(api_key)
        if hashed_key in self._api_keys:
            del self._api_keys[hashed_key]
            return True
        return False


class JWTManager:
    """Manages JWT tokens for authentication."""

    def __init__(self, secret_key: str = JWT_SECRET_KEY, algorithm: str = JWT_ALGORITHM):
        """Initialize JWT manager."""
        if not HAS_JWT:
            raise ImportError("PyJWT is required for JWT authentication")

        self.secret_key = secret_key
        self.algorithm = algorithm

    def create_access_token(
        self,
        user_id: str,
        username: str,
        role: UserRole,
        expires_delta: timedelta | None = None,
    ) -> str:
        """Create a JWT access token."""
        if expires_delta is None:
            expires_delta = timedelta(minutes=JWT_ACCESS_TOKEN_EXPIRE_MINUTES)

        expire = datetime.utcnow() + expires_delta

        payload = {
            "sub": user_id,
            "username": username,
            "role": role.value,
            "exp": expire,
            "iat": datetime.utcnow(),
            "type": "access",
        }

        return str(jwt.encode(payload, self.secret_key, algorithm=self.algorithm))

    def create_refresh_token(
        self,
        user_id: str,
        username: str,
        expires_delta: timedelta | None = None,
    ) -> str:
        """Create a JWT refresh token."""
        if expires_delta is None:
            expires_delta = timedelta(days=JWT_REFRESH_TOKEN_EXPIRE_DAYS)

        expire = datetime.utcnow() + expires_delta

        payload = {
            "sub": user_id,
            "username": username,
            "exp": expire,
            "iat": datetime.utcnow(),
            "type": "refresh",
        }

        return str(jwt.encode(payload, self.secret_key, algorithm=self.algorithm))

    def verify_token(self, token: str) -> dict[str, Any] | None:
        """Verify and decode a JWT token."""
        try:
            payload: dict[str, Any] = jwt.decode(
                token, self.secret_key, algorithms=[self.algorithm]
            )
            return payload
        except PyJWTError as e:
            logger.warning(f"JWT verification failed: {e}")
            return None

    def refresh_access_token(self, refresh_token: str) -> str | None:
        """Refresh an access token using a refresh token."""
        payload = self.verify_token(refresh_token)
        if not payload or payload.get("type") != "refresh":
            return None

        user_id = str(payload.get("sub", ""))
        username = str(payload.get("username", ""))
        role_str = payload.get("role", UserRole.USER.value)

        try:
            role = UserRole(role_str)
        except ValueError:
            role = UserRole.USER

        return self.create_access_token(user_id, username, role)


# Global instances
_api_key_manager = APIKeyManager()
_jwt_manager = JWTManager() if HAS_JWT else None


def get_api_key_manager() -> APIKeyManager:
    """Get global API key manager."""
    return _api_key_manager


def get_jwt_manager() -> JWTManager | None:
    """Get global JWT manager."""
    return _jwt_manager


def get_current_user_from_token(token: str) -> User | None:
    """Get user from JWT token."""
    if not _jwt_manager:
        return None

    payload = _jwt_manager.verify_token(token)
    if not payload or payload.get("type") != "access":
        return None

    user_id = payload.get("sub")
    if not user_id:
        return None

    return _api_key_manager.get_user(user_id)


def get_current_user_from_api_key(api_key: str) -> User | None:
    """Get user from API key."""
    return _api_key_manager.authenticate_api_key(api_key)


def require_permission(permission: Permission):
    """Decorator to require a specific permission."""

    def decorator(func):
        func._required_permission = permission
        return func

    return decorator


def require_any_permission(permissions: set[Permission]):
    """Decorator to require any of the specified permissions."""

    def decorator(func):
        func._required_permissions = permissions
        func._permission_mode = "any"
        return func

    return decorator


def require_all_permissions(permissions: set[Permission]):
    """Decorator to require all of the specified permissions."""

    def decorator(func):
        func._required_permissions = permissions
        func._permission_mode = "all"
        return func

    return decorator


def require_role(role: UserRole):
    """Decorator to require a specific role."""

    def decorator(func):
        func._required_role = role
        return func

    return decorator


# Re-export require_auth_if_enabled from middleware for backward compatibility
try:
    from .middleware.auth_middleware import require_auth_if_enabled
except ImportError:
    # Fallback if middleware not available
    from starlette.requests import Request

    async def require_auth_if_enabled(request: Request) -> User | None:
        """Stub when middleware not available."""
        return None
