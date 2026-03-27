"""
Unit Tests for Authentication Middleware
Tests authentication and authorization middleware functionality.
"""

import sys
from pathlib import Path
from unittest.mock import AsyncMock, Mock, patch

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the auth middleware
try:
    from backend.api.auth import Permission, User, UserRole
    from backend.api.middleware import auth_middleware
except ImportError:
    pytest.skip("Could not import auth_middleware or auth dependencies", allow_module_level=True)


class TestAuthMiddlewareImports:
    """Test auth middleware can be imported."""

    def test_module_imports(self):
        """Test module can be imported."""
        assert auth_middleware is not None, "Failed to import auth_middleware module"

    def test_module_has_functions(self):
        """Test module has expected functions."""
        functions = dir(auth_middleware)
        assert "get_current_user" in functions, "get_current_user should exist"
        assert "require_authentication" in functions, "require_authentication should exist"
        assert (
            "require_permission_middleware" in functions
        ), "require_permission_middleware should exist"
        assert "require_role_middleware" in functions, "require_role_middleware should exist"
        assert "get_optional_user" in functions, "get_optional_user should exist"


class TestGetCurrentUser:
    """Test get_current_user function."""

    @pytest.mark.asyncio
    async def test_get_current_user_with_api_key(self):
        """Test get_current_user with API key authentication."""
        # Create mock user
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"
        mock_user.username = "testuser"

        # Create mock request with API key
        mock_request = Mock()
        mock_request.headers = {"X-API-Key": "vs_test_api_key"}

        with patch(
            "backend.api.middleware.auth_middleware.get_current_user_from_api_key"
        ) as mock_auth:
            mock_auth.return_value = mock_user

            user = await auth_middleware.get_current_user(mock_request)

            assert user is not None, "Should return user with valid API key"
            assert user.user_id == "user123", "Should return correct user"
            mock_auth.assert_called_once_with("vs_test_api_key")

    @pytest.mark.asyncio
    async def test_get_current_user_with_jwt_token(self):
        """Test get_current_user with JWT token authentication."""
        # Create mock user
        mock_user = Mock(spec=User)
        mock_user.user_id = "user456"
        mock_user.username = "jwtuser"

        # Create mock request with JWT token
        mock_request = Mock()
        mock_request.headers = {}

        # Mock HTTPBearer security (async function)
        mock_credentials = Mock()
        mock_credentials.credentials = "jwt_token_here"

        # Patch security to return async mock
        mock_security_async = AsyncMock(return_value=mock_credentials)
        with patch.object(auth_middleware, "security", mock_security_async):
            with patch(
                "backend.api.middleware.auth_middleware.get_current_user_from_token"
            ) as mock_token_auth:
                mock_token_auth.return_value = mock_user

                user = await auth_middleware.get_current_user(mock_request)

                assert user is not None, "Should return user with valid JWT token"
                assert user.user_id == "user456", "Should return correct user"
                mock_token_auth.assert_called_once_with("jwt_token_here")

    @pytest.mark.asyncio
    async def test_get_current_user_no_authentication(self):
        """Test get_current_user with no authentication."""
        mock_request = Mock()
        mock_request.headers = {}

        # Mock HTTPBearer to return None (async)
        mock_security_async = AsyncMock(return_value=None)
        with patch.object(auth_middleware, "security", mock_security_async):
            with patch(
                "backend.api.middleware.auth_middleware.get_current_user_from_api_key"
            ) as mock_api_key:
                mock_api_key.return_value = None

                user = await auth_middleware.get_current_user(mock_request)

                assert user is None, "Should return None when no authentication provided"

    @pytest.mark.asyncio
    async def test_get_current_user_api_key_precedence(self):
        """Test that API key takes precedence over JWT token."""
        mock_user_api = Mock(spec=User)
        mock_user_api.user_id = "api_user"
        mock_user_api.username = "apiuser"

        mock_user_jwt = Mock(spec=User)
        mock_user_jwt.user_id = "jwt_user"

        mock_request = Mock()
        mock_request.headers = {"X-API-Key": "vs_api_key"}

        mock_credentials = Mock()
        mock_credentials.credentials = "jwt_token"

        with patch(
            "backend.api.middleware.auth_middleware.get_current_user_from_api_key"
        ) as mock_api_key:
            mock_api_key.return_value = mock_user_api
            mock_security_async = AsyncMock(return_value=mock_credentials)
            with patch.object(auth_middleware, "security", mock_security_async):
                user = await auth_middleware.get_current_user(mock_request)

                assert user is not None, "Should return user"
                assert user.user_id == "api_user", "Should prefer API key over JWT"
                mock_api_key.assert_called_once()

    @pytest.mark.asyncio
    async def test_get_current_user_invalid_api_key(self):
        """Test get_current_user with invalid API key."""
        mock_request = Mock()
        mock_request.headers = {"X-API-Key": "invalid_key"}

        with patch(
            "backend.api.middleware.auth_middleware.get_current_user_from_api_key"
        ) as mock_auth:
            mock_auth.return_value = None
            mock_security_async = AsyncMock(return_value=None)
            with patch.object(auth_middleware, "security", mock_security_async):
                user = await auth_middleware.get_current_user(mock_request)

                assert user is None, "Should return None with invalid API key"

    @pytest.mark.asyncio
    async def test_get_current_user_invalid_jwt_token(self):
        """Test get_current_user with invalid JWT token."""
        mock_request = Mock()
        mock_request.headers = {}

        mock_credentials = Mock()
        mock_credentials.credentials = "invalid_jwt_token"

        mock_security_async = AsyncMock(return_value=mock_credentials)
        with patch.object(auth_middleware, "security", mock_security_async):
            with patch(
                "backend.api.middleware.auth_middleware.get_current_user_from_token"
            ) as mock_token_auth:
                mock_token_auth.return_value = None

                user = await auth_middleware.get_current_user(mock_request)

                assert user is None, "Should return None with invalid JWT token"


class TestRequireAuthentication:
    """Test require_authentication function."""

    @pytest.mark.asyncio
    async def test_require_authentication_success(self):
        """Test require_authentication with valid user."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req123"
        mock_request.url.path = "/api/test"
        mock_request.method = "GET"

        with patch(
            "backend.api.middleware.auth_middleware.get_current_user",
            new_callable=AsyncMock,
        ) as mock_get_user:
            mock_get_user.return_value = mock_user

            user = await auth_middleware.require_authentication(mock_request)

            assert user is not None, "Should return user when authenticated"
            assert user.user_id == "user123", "Should return correct user"

    @pytest.mark.asyncio
    async def test_require_authentication_failure(self):
        """Test require_authentication raises exception when not authenticated."""
        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req456"
        mock_request.url.path = "/api/test"
        mock_request.method = "GET"

        with patch(
            "backend.api.middleware.auth_middleware.get_current_user",
            new_callable=AsyncMock,
        ) as mock_get_user:
            mock_get_user.return_value = None

            with pytest.raises(Exception) as exc_info:
                await auth_middleware.require_authentication(mock_request)

            # Should raise HTTPException with 401 status
            assert exc_info.value.status_code == 401, "Should raise 401 Unauthorized"

    @pytest.mark.asyncio
    async def test_require_authentication_logs_failure(self):
        """Test require_authentication logs authentication failure."""
        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req789"
        mock_request.url.path = "/api/protected"
        mock_request.method = "POST"

        with patch(
            "backend.api.middleware.auth_middleware.get_current_user",
            new_callable=AsyncMock,
        ) as mock_get_user:
            mock_get_user.return_value = None
            with patch("backend.api.middleware.auth_middleware.logger") as mock_logger:
                try:
                    await auth_middleware.require_authentication(mock_request)
                except Exception:
                    pass  # Expected exception

                # Verify logging was called
                mock_logger.warning.assert_called_once()


class TestRequirePermissionMiddleware:
    """Test require_permission_middleware function."""

    @pytest.mark.asyncio
    async def test_require_permission_success(self):
        """Test require_permission_middleware with user having permission."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"
        mock_user.has_permission = Mock(return_value=True)

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req123"
        mock_request.url.path = "/api/test"
        mock_request.method = "GET"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user

            user = await auth_middleware.require_permission_middleware(
                mock_request, Permission.PROFILE_READ
            )

            assert user is not None, "Should return user with permission"
            mock_user.has_permission.assert_called_once_with(Permission.PROFILE_READ)

    @pytest.mark.asyncio
    async def test_require_permission_denied(self):
        """Test require_permission_middleware raises exception when permission denied."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"
        mock_user.username = "testuser"
        mock_user.has_permission = Mock(return_value=False)

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req456"
        mock_request.url.path = "/api/protected"
        mock_request.method = "POST"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user

            with pytest.raises(Exception) as exc_info:
                await auth_middleware.require_permission_middleware(
                    mock_request, Permission.PROFILE_DELETE
                )

            # Should raise HTTPException with 403 status
            assert exc_info.value.status_code == 403, "Should raise 403 Forbidden"

    @pytest.mark.asyncio
    async def test_require_permission_logs_denial(self):
        """Test require_permission_middleware logs permission denial."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"
        mock_user.username = "testuser"
        mock_user.has_permission = Mock(return_value=False)

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req789"
        mock_request.url.path = "/api/protected"
        mock_request.method = "DELETE"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user
            with patch("backend.api.middleware.auth_middleware.logger") as mock_logger:
                try:
                    await auth_middleware.require_permission_middleware(
                        mock_request, Permission.PROJECT_DELETE
                    )
                except Exception:
                    pass  # Expected exception

                # Verify logging was called
                mock_logger.warning.assert_called_once()


class TestRequireRoleMiddleware:
    """Test require_role_middleware function."""

    @pytest.mark.asyncio
    async def test_require_role_success_admin(self):
        """Test require_role_middleware with admin role."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "admin123"
        mock_user.username = "admin"
        mock_user.role = UserRole.ADMIN

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req123"
        mock_request.url.path = "/api/admin"
        mock_request.method = "GET"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user

            user = await auth_middleware.require_role_middleware(mock_request, UserRole.ADMIN)

            assert user is not None, "Should return user with required role"

    @pytest.mark.asyncio
    async def test_require_role_success_hierarchy(self):
        """Test require_role_middleware with role hierarchy (admin > user)."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "admin123"
        mock_user.username = "admin"
        mock_user.role = UserRole.ADMIN

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req456"
        mock_request.url.path = "/api/user"
        mock_request.method = "GET"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user

            # Admin should have access to USER level endpoints
            user = await auth_middleware.require_role_middleware(mock_request, UserRole.USER)

            assert user is not None, "Admin should have access to user-level endpoints"

    @pytest.mark.asyncio
    async def test_require_role_denied(self):
        """Test require_role_middleware raises exception when role denied."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"
        mock_user.username = "regularuser"
        mock_user.role = UserRole.USER

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req789"
        mock_request.url.path = "/api/admin"
        mock_request.method = "GET"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user

            with pytest.raises(Exception) as exc_info:
                await auth_middleware.require_role_middleware(mock_request, UserRole.ADMIN)

            # Should raise HTTPException with 403 status
            assert exc_info.value.status_code == 403, "Should raise 403 Forbidden"

    @pytest.mark.asyncio
    async def test_require_role_guest_denied_user(self):
        """Test require_role_middleware denies guest access to user endpoints."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "guest123"
        mock_user.username = "guest"
        mock_user.role = UserRole.GUEST

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req999"
        mock_request.url.path = "/api/user"
        mock_request.method = "GET"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user

            with pytest.raises(Exception) as exc_info:
                await auth_middleware.require_role_middleware(mock_request, UserRole.USER)

            assert exc_info.value.status_code == 403, "Should raise 403 Forbidden"

    @pytest.mark.asyncio
    async def test_require_role_logs_denial(self):
        """Test require_role_middleware logs role denial."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"
        mock_user.username = "regularuser"
        mock_user.role = UserRole.USER

        mock_request = Mock()
        mock_request.state = Mock()
        mock_request.state.request_id = "req111"
        mock_request.url.path = "/api/admin"
        mock_request.method = "POST"

        with patch(
            "backend.api.middleware.auth_middleware.require_authentication",
            new_callable=AsyncMock,
        ) as mock_req_auth:
            mock_req_auth.return_value = mock_user
            with patch("backend.api.middleware.auth_middleware.logger") as mock_logger:
                try:
                    await auth_middleware.require_role_middleware(mock_request, UserRole.ADMIN)
                except Exception:
                    pass  # Expected exception

                # Verify logging was called
                mock_logger.warning.assert_called_once()


class TestGetOptionalUser:
    """Test get_optional_user function."""

    def test_get_optional_user_with_authenticated_user(self):
        """Test get_optional_user returns user when authenticated."""
        mock_user = Mock(spec=User)
        mock_user.user_id = "user123"

        mock_request = Mock()

        # Use AsyncMock so the coroutine is properly awaited by the real event loop
        with patch(
            "backend.api.middleware.auth_middleware.get_current_user",
            new_callable=AsyncMock,
        ) as mock_get_user:
            mock_get_user.return_value = mock_user

            user = auth_middleware.get_optional_user(mock_request)

            assert user is not None, "Should return user when authenticated"
            assert user.user_id == "user123", "Should return correct user"

    def test_get_optional_user_no_authentication(self):
        """Test get_optional_user returns None when not authenticated."""
        mock_request = Mock()

        # Use AsyncMock so the coroutine is properly awaited by the real event loop
        with patch(
            "backend.api.middleware.auth_middleware.get_current_user",
            new_callable=AsyncMock,
        ) as mock_get_user:
            mock_get_user.return_value = None

            user = auth_middleware.get_optional_user(mock_request)

            assert user is None, "Should return None when not authenticated"

    def test_get_optional_user_handles_runtime_error(self):
        """Test get_optional_user handles RuntimeError gracefully."""
        mock_request = Mock()

        # When loop is running, get_optional_user returns None without calling get_current_user
        with patch("asyncio.get_event_loop") as mock_get_loop:
            mock_loop = Mock()
            mock_loop.is_running.return_value = True
            mock_get_loop.return_value = mock_loop

            user = auth_middleware.get_optional_user(mock_request)

            assert user is None, "Should return None when event loop is running"

    def test_get_optional_user_handles_exception(self):
        """Test get_optional_user handles exceptions gracefully."""
        mock_request = Mock()

        # get_event_loop raises RuntimeError -> code falls back to asyncio.run
        # get_current_user returns coroutine that raises when awaited
        # asyncio.run runs it, exception propagates, get_optional_user catches and returns None
        with patch("asyncio.get_event_loop") as mock_get_loop:
            mock_get_loop.side_effect = RuntimeError("No event loop")

            with patch(
                "backend.api.middleware.auth_middleware.get_current_user",
                new_callable=AsyncMock,
            ) as mock_get_user:
                mock_get_user.side_effect = Exception("Test exception")

                user = auth_middleware.get_optional_user(mock_request)

                assert user is None, "Should return None on exception"


class TestSecurityScheme:
    """Test security scheme configuration."""

    def test_security_scheme_exists(self):
        """Test security scheme is configured."""
        assert hasattr(auth_middleware, "security"), "Should have security scheme"
        assert auth_middleware.security is not None, "Security scheme should be initialized"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
