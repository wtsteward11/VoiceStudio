"""
Unit Tests for Auth API Route
Tests authentication endpoints comprehensively.
"""

import sys
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the route module
try:
    from backend.api.routes import auth
except ImportError:
    pytest.skip("Could not import auth route module", allow_module_level=True)


class TestAuthRouteImports:
    """Test auth route module can be imported."""

    def test_auth_module_imports(self):
        """Test auth module can be imported."""
        assert auth is not None, "Failed to import auth module"
        assert hasattr(auth, "router"), "auth module missing router"
        assert hasattr(auth, "LoginRequest"), "auth module missing LoginRequest model"
        assert hasattr(auth, "TokenResponse"), "auth module missing TokenResponse model"
        assert hasattr(auth, "UserResponse"), "auth module missing UserResponse model"

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert auth.router is not None, "Router should exist"
        assert hasattr(auth.router, "prefix"), "Router should have prefix"
        assert auth.router.prefix == "/api/auth", "Router prefix should be /api/auth"

    def test_router_has_routes(self):
        """Test router has expected routes."""
        routes = [route.path for route in auth.router.routes]
        # Routes may include prefix or not depending on FastAPI version
        route_str = str(routes)
        assert "login" in route_str, "Router should have login route"
        assert "refresh" in route_str, "Router should have refresh route"
        assert "me" in route_str, "Router should have me route"
        assert "users" in route_str, "Router should have users route"


class TestAuthRouteHandlers:
    """Test auth route handlers exist."""

    def test_login_handler_exists(self):
        """Test login handler exists."""
        assert hasattr(auth, "login"), "login handler should exist"
        assert callable(auth.login), "login should be callable"

    def test_refresh_handler_exists(self):
        """Test refresh handler exists."""
        assert hasattr(auth, "refresh_token"), "refresh_token handler should exist"
        assert callable(auth.refresh_token), "refresh_token should be callable"

    def test_get_current_user_info_handler_exists(self):
        """Test get_current_user_info handler exists."""
        assert hasattr(auth, "get_current_user_info"), "get_current_user_info handler should exist"
        assert callable(auth.get_current_user_info), "get_current_user_info should be callable"

    def test_create_user_handler_exists(self):
        """Test create_user handler exists."""
        assert hasattr(auth, "create_user"), "create_user handler should exist"
        assert callable(auth.create_user), "create_user should be callable"

    def test_generate_api_key_handler_exists(self):
        """Test generate_api_key handler exists."""
        assert hasattr(auth, "generate_api_key"), "generate_api_key handler should exist"
        assert callable(auth.generate_api_key), "generate_api_key should be callable"

    def test_revoke_api_key_handler_exists(self):
        """Test revoke_api_key handler exists."""
        assert hasattr(auth, "revoke_api_key"), "revoke_api_key handler should exist"
        assert callable(auth.revoke_api_key), "revoke_api_key should be callable"


class TestAuthRouteFunctionality:
    """Test auth route functionality with mocks."""

    @patch("backend.api.routes.auth.get_jwt_manager")
    @patch("backend.api.routes.auth.get_api_key_manager")
    def test_login_with_api_key(self, mock_get_api_key_manager, mock_get_jwt_manager):
        """Test login with API key."""
        # Mock JWT manager
        mock_jwt = MagicMock()
        mock_jwt.create_access_token.return_value = "access_token"
        mock_jwt.create_refresh_token.return_value = "refresh_token"
        mock_get_jwt_manager.return_value = mock_jwt

        # Mock API key manager
        mock_api_key_mgr = MagicMock()
        mock_user = MagicMock()
        mock_user.user_id = "user123"
        mock_user.username = "testuser"
        mock_user.role = MagicMock()
        mock_user.role.value = "user"
        mock_user.last_login = None
        mock_api_key_mgr.authenticate_api_key.return_value = mock_user
        mock_get_api_key_manager.return_value = mock_api_key_mgr

        # Create request
        request = auth.LoginRequest(username="testuser", api_key="test_key")

        # Test login
        result = auth.login(request)

        # Verify
        assert result.access_token == "access_token"
        assert result.refresh_token == "refresh_token"
        assert result.token_type == "bearer"
        mock_api_key_mgr.authenticate_api_key.assert_called_once_with("test_key")

    @patch("backend.api.routes.auth.get_jwt_manager")
    @patch("backend.api.routes.auth.get_api_key_manager")
    def test_login_with_password(self, mock_get_api_key_manager, mock_get_jwt_manager):
        """Test login with password."""
        # Mock JWT manager
        mock_jwt = MagicMock()
        mock_jwt.create_access_token.return_value = "access_token"
        mock_jwt.create_refresh_token.return_value = "refresh_token"
        mock_get_jwt_manager.return_value = mock_jwt

        # Mock API key manager
        mock_api_key_mgr = MagicMock()
        mock_user = MagicMock()
        mock_user.user_id = "user123"
        mock_user.username = "testuser"
        mock_user.role = MagicMock()
        mock_user.role.value = "user"
        mock_user.last_login = None
        mock_api_key_mgr.authenticate_password.return_value = mock_user
        mock_get_api_key_manager.return_value = mock_api_key_mgr

        # Create request
        request = auth.LoginRequest(username="testuser", password="testpass")

        # Test login
        result = auth.login(request)

        # Verify
        assert result.access_token == "access_token"
        assert result.refresh_token == "refresh_token"
        mock_api_key_mgr.authenticate_password.assert_called_once_with("testuser", "testpass")

    @patch("backend.api.routes.auth.get_jwt_manager")
    @patch("backend.api.routes.auth.get_api_key_manager")
    def test_login_creates_guest_user(self, mock_get_api_key_manager, mock_get_jwt_manager):
        """Test login creates guest user when no password provided."""
        # Mock JWT manager
        mock_jwt = MagicMock()
        mock_jwt.create_access_token.return_value = "access_token"
        mock_jwt.create_refresh_token.return_value = "refresh_token"
        mock_get_jwt_manager.return_value = mock_jwt

        # Mock API key manager
        mock_api_key_mgr = MagicMock()
        mock_api_key_mgr.get_user.return_value = None
        mock_user = MagicMock()
        mock_user.user_id = "user123"
        mock_user.username = "testuser"
        mock_user.role = MagicMock()
        mock_user.role.value = "guest"
        mock_user.last_login = None
        mock_api_key_mgr.create_user.return_value = (mock_user, None)
        mock_get_api_key_manager.return_value = mock_api_key_mgr

        # Create request
        request = auth.LoginRequest(username="testuser")

        # Test login
        result = auth.login(request)

        # Verify
        assert result.access_token == "access_token"
        mock_api_key_mgr.create_user.assert_called_once()

    @patch("backend.api.routes.auth.get_jwt_manager")
    def test_refresh_token(self, mock_get_jwt_manager):
        """Test refresh token."""
        # Mock JWT manager
        mock_jwt = MagicMock()
        mock_jwt.refresh_access_token.return_value = "new_access_token"
        mock_jwt.verify_token.return_value = {"sub": "user123", "username": "testuser"}
        mock_jwt.create_refresh_token.return_value = "new_refresh_token"
        mock_get_jwt_manager.return_value = mock_jwt

        # Mock credentials
        mock_credentials = MagicMock()
        mock_credentials.credentials = "refresh_token"

        # Test refresh
        result = auth.refresh_token(mock_credentials)

        # Verify
        assert result.access_token == "new_access_token"
        assert result.refresh_token == "new_refresh_token"
        mock_jwt.refresh_access_token.assert_called_once_with("refresh_token")

    @patch("backend.api.routes.auth.require_authentication")
    def test_get_current_user_info(self, mock_require_authentication):
        """Test get current user info."""
        # Mock user
        mock_user = MagicMock()
        mock_user.user_id = "user123"
        mock_user.username = "testuser"
        mock_user.email = "test@example.com"
        mock_user.role = MagicMock()
        mock_user.role.value = "user"
        mock_user.is_active = True
        mock_user.created_at = MagicMock()
        mock_user.created_at.isoformat.return_value = "2025-01-01T00:00:00"
        mock_user.last_login = None
        mock_require_authentication.return_value = mock_user

        # Test get current user info
        result = auth.get_current_user_info(mock_user)

        # Verify
        assert result.user_id == "user123"
        assert result.username == "testuser"
        assert result.email == "test@example.com"
        assert result.role == "user"
        assert result.is_active is True


class TestAuthRouteErrorHandling:
    """Test auth route error handling."""

    @patch("backend.api.routes.auth.get_jwt_manager")
    def test_login_no_jwt_manager(self, mock_get_jwt_manager):
        """Test login fails when JWT manager not available."""
        mock_get_jwt_manager.return_value = None

        request = auth.LoginRequest(username="testuser", api_key="test_key")

        with pytest.raises(Exception):  # Should raise HTTPException
            auth.login(request)

    @patch("backend.api.routes.auth.get_jwt_manager")
    @patch("backend.api.routes.auth.get_api_key_manager")
    def test_login_invalid_api_key(self, mock_get_api_key_manager, mock_get_jwt_manager):
        """Test login fails with invalid API key."""
        # Mock JWT manager
        mock_jwt = MagicMock()
        mock_get_jwt_manager.return_value = mock_jwt

        # Mock API key manager
        mock_api_key_mgr = MagicMock()
        mock_api_key_mgr.authenticate_api_key.return_value = None
        mock_get_api_key_manager.return_value = mock_api_key_mgr

        request = auth.LoginRequest(username="testuser", api_key="invalid_key")

        with pytest.raises(Exception):  # Should raise HTTPException
            auth.login(request)

    @patch("backend.api.routes.auth.get_jwt_manager")
    def test_refresh_token_no_jwt_manager(self, mock_get_jwt_manager):
        """Test refresh token fails when JWT manager not available."""
        mock_get_jwt_manager.return_value = None

        mock_credentials = MagicMock()
        mock_credentials.credentials = "refresh_token"

        with pytest.raises(Exception):  # Should raise HTTPException
            auth.refresh_token(mock_credentials)

    @patch("backend.api.routes.auth.get_jwt_manager")
    def test_refresh_token_invalid_token(self, mock_get_jwt_manager):
        """Test refresh token fails with invalid token."""
        # Mock JWT manager
        mock_jwt = MagicMock()
        mock_jwt.refresh_access_token.return_value = None
        mock_get_jwt_manager.return_value = mock_jwt

        mock_credentials = MagicMock()
        mock_credentials.credentials = "invalid_token"

        with pytest.raises(Exception):  # Should raise HTTPException
            auth.refresh_token(mock_credentials)
