"""
Authentication Middleware

Provides authentication and authorization middleware for FastAPI.
"""

from __future__ import annotations

import logging

from fastapi import HTTPException, Request, WebSocket, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from ..auth import (
    Permission,
    User,
    UserRole,
    get_current_user_from_api_key,
    get_current_user_from_token,
)

logger = logging.getLogger(__name__)

# Security scheme for OpenAPI
security = HTTPBearer(auto_error=False)


async def get_current_user(request: Request) -> User | None:
    """
    Get current authenticated user from request.

    Supports both API key (X-API-Key header) and JWT token (Authorization header).
    """
    # Try API key first
    api_key = request.headers.get("X-API-Key")
    if api_key:
        user = get_current_user_from_api_key(api_key)
        if user:
            # Log authentication
            logger.info(
                f"API key authentication successful for user {user.user_id}",
                extra={
                    "user_id": user.user_id,
                    "username": user.username,
                    "auth_method": "api_key",
                },
            )
            return user

    # Try JWT token
    credentials: HTTPAuthorizationCredentials | None = await security(request)
    if credentials:
        token = credentials.credentials
        user = get_current_user_from_token(token)
        if user:
            # Log authentication
            logger.info(
                f"JWT authentication successful for user {user.user_id}",
                extra={
                    "user_id": user.user_id,
                    "username": user.username,
                    "auth_method": "jwt",
                },
            )
            return user

    return None


async def require_authentication(request: Request) -> User:
    """
    Require authentication and return user.

    Raises HTTPException if not authenticated.
    """
    user = await get_current_user(request)

    if not user:
        request_id = getattr(request.state, "request_id", None)

        # Log authentication failure
        logger.warning(
            f"Authentication required but not provided for {request.url.path}",
            extra={
                "path": request.url.path,
                "method": request.method,
                "request_id": request_id,
            },
        )

        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication required",
            headers={"WWW-Authenticate": "Bearer"},
        )

    return user


async def require_permission_middleware(
    request: Request,
    required_permission: Permission,
) -> User:
    """
    Require authentication and specific permission.

    Raises HTTPException if not authenticated or lacks permission.
    """
    user = await require_authentication(request)

    if not user.has_permission(required_permission):
        request_id = getattr(request.state, "request_id", None)

        # Log authorization failure
        logger.warning(
            f"Permission denied for user {user.user_id}: {required_permission.value}",
            extra={
                "user_id": user.user_id,
                "username": user.username,
                "required_permission": required_permission.value,
                "path": request.url.path,
                "method": request.method,
                "request_id": request_id,
            },
        )

        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail=f"Permission denied: {required_permission.value}",
        )

    return user


async def require_role_middleware(
    request: Request,
    required_role: UserRole,
) -> User:
    """
    Require authentication and specific role.

    Raises HTTPException if not authenticated or lacks role.
    """
    user = await require_authentication(request)

    # Check role hierarchy (admin > user > guest)
    role_hierarchy = {
        UserRole.ADMIN: 3,
        UserRole.USER: 2,
        UserRole.GUEST: 1,
        UserRole.SERVICE: 2,
    }

    user_level = role_hierarchy.get(user.role, 0)
    required_level = role_hierarchy.get(required_role, 0)

    if user_level < required_level:
        request_id = getattr(request.state, "request_id", None)

        # Log authorization failure
        logger.warning(
            f"Role denied for user {user.user_id}: required {required_role.value}, has {user.role.value}",
            extra={
                "user_id": user.user_id,
                "username": user.username,
                "required_role": required_role.value,
                "user_role": user.role.value,
                "path": request.url.path,
                "method": request.method,
                "request_id": request_id,
            },
        )

        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail=f"Role denied: requires {required_role.value}",
        )

    return user


def get_optional_user(request: Request) -> User | None:
    """
    Get current user if authenticated, None otherwise.

    Does not raise exceptions - use for optional authentication.
    """
    import asyncio

    try:
        loop = asyncio.get_event_loop()
        if loop.is_running():
            # If loop is running, we need to handle this differently
            # For now, return None and let the route handle it
            return None
        else:
            return loop.run_until_complete(get_current_user(request))
    except RuntimeError:
        # No event loop, create one
        try:
            return asyncio.run(get_current_user(request))
        except Exception:
            return None
    except Exception:
        return None


# Environment-aware authentication toggle for local-first design
import os

# Default to no auth required for local desktop usage
AUTH_REQUIRED = os.getenv("VOICESTUDIO_REQUIRE_AUTH", "false").lower() == "true"

# RFC 6455 private-use range: application-defined WebSocket close code for auth required
WS_CLOSE_AUTH_REQUIRED = 4001


def _get_user_from_websocket_headers(ws: WebSocket) -> User | None:
    """Resolve User from WebSocket upgrade headers (same sources as HTTP)."""
    api_key = ws.headers.get("x-api-key") or ws.headers.get("X-API-Key")
    if api_key:
        user = get_current_user_from_api_key(api_key)
        if user:
            return user

    auth_header = ws.headers.get("authorization") or ws.headers.get("Authorization") or ""
    if len(auth_header) >= 7 and auth_header[:7].lower() == "bearer ":
        token = auth_header[7:].strip()
        if token:
            user = get_current_user_from_token(token)
            if user:
                return user

    return None


async def require_ws_auth_if_enabled(ws: WebSocket) -> User | None:
    """
    Handshake-time auth for WebSocket connections.

    Reads X-API-Key and Authorization: Bearer from the upgrade request.
    When AUTH_REQUIRED is False, credentials are optional (local desktop).
    When AUTH_REQUIRED is True and no valid user: accept, close with WS_CLOSE_AUTH_REQUIRED, return None.
    """
    user = _get_user_from_websocket_headers(ws)

    if not AUTH_REQUIRED:
        return user

    if user is None:
        request_id = getattr(ws.state, "request_id", None)
        logger.warning(
            "WS auth required but not provided for %s",
            ws.url.path,
            extra={"path": ws.url.path, "request_id": request_id},
        )
        await ws.accept()
        await ws.close(code=WS_CLOSE_AUTH_REQUIRED)
        return None

    return user


async def require_auth_if_enabled(request: Request) -> User | None:
    """
    Require authentication only when VOICESTUDIO_REQUIRE_AUTH=true.

    For local desktop usage, authentication is optional by default.
    Enable for network/multi-user deployments by setting the env var.

    Returns:
        User if authenticated, None if auth disabled, raises if auth required but missing.
    """
    if not AUTH_REQUIRED:
        # Local mode: authentication optional, try to get user but don't require
        return await get_current_user(request)

    # Auth required mode: enforce authentication
    return await require_authentication(request)


def create_permission_dependency(permission: Permission):
    """
    Factory for creating permission-checking dependencies.

    Usage:
        @router.post("/admin-only", dependencies=[Depends(create_permission_dependency(Permission.ADMIN))])
    """

    async def check_permission(request: Request) -> User:
        if not AUTH_REQUIRED:
            # Local mode: skip permission check
            user = await get_current_user(request)
            if user:
                return user
            # Create anonymous local user
            return User(
                user_id="local",
                username="local_user",
                role=UserRole.ADMIN,  # Local user has full access
                permissions=set(Permission),
            )
        return await require_permission_middleware(request, permission)

    return check_permission
