// Phase 2 Gap Fix: Unified AuthService Implementation
// Provides authentication and authorization for VoiceStudio

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace VoiceStudio.App.Services;

/// <summary>
/// Phase 2 Gap Fix: Implementation of unified authentication service.
/// Supports both local desktop mode (auth optional) and network mode (auth required).
/// </summary>
public class AuthService : IUnifiedAuthService
{
    private readonly HttpClient? _httpClient;
    private AuthenticatedUser? _currentUser;
    private string? _currentToken;
    private string? _currentApiKey;
    private string? _refreshToken;
    private DateTime? _tokenExpiry;

    private const string TokenStorageKey = "VoiceStudio_AuthToken";
    private const string RefreshTokenStorageKey = "VoiceStudio_RefreshToken";
    private const string ApiKeyStorageKey = "VoiceStudio_ApiKey";
    private const string UserStorageKey = "VoiceStudio_UserInfo";

    public event EventHandler<AuthStateChangedEventArgs>? AuthStateChanged;

    public AuthService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient;
        
        // Check environment for auth requirement
        var envAuthRequired = Environment.GetEnvironmentVariable("VOICESTUDIO_REQUIRE_AUTH");
        IsAuthRequired = string.Equals(envAuthRequired, "true", StringComparison.OrdinalIgnoreCase);
    }

    #region Properties

    public AuthenticatedUser? CurrentUser => _currentUser;

    public bool IsAuthenticated => _currentUser != null;

    public bool IsAuthRequired { get; private set; }

    public bool IsLocalSession => _currentUser?.IsLocalUser == true;

    #endregion

    #region Authentication Methods

    public async Task InitializeAsync()
    {
        try
        {
            // Try to restore existing session
            await RestoreSessionAsync();

            // If no session and not auth required, create local session
            if (!IsAuthenticated && !IsAuthRequired)
            {
                await CreateLocalSessionAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] Initialization failed: {ex.Message}");
            
            // Fallback to local session if not auth required
            if (!IsAuthRequired)
            {
                await CreateLocalSessionAsync();
            }
        }
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        if (_httpClient == null)
        {
            return new AuthResult
            {
                Success = false,
                ErrorMessage = "HTTP client not configured"
            };
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new
            {
                username,
                password
            });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (result != null)
                {
                    _currentToken = result.AccessToken;
                    _refreshToken = result.RefreshToken;
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(result.ExpiresIn);

                    _currentUser = new AuthenticatedUser
                    {
                        UserId = result.UserId ?? string.Empty,
                        Username = result.Username ?? username,
                        Role = ParseRole(result.Role),
                        Permissions = ParsePermissions(result.Permissions),
                        TokenExpiry = _tokenExpiry,
                        IsLocalUser = false
                    };

                    await SaveSessionAsync();
                    RaiseAuthStateChanged(null, _currentUser);

                    return new AuthResult
                    {
                        Success = true,
                        User = _currentUser,
                        Token = _currentToken,
                        RefreshToken = _refreshToken,
                        ExpiresAt = _tokenExpiry
                    };
                }
            }

            var errorMessage = await response.Content.ReadAsStringAsync();
            return new AuthResult
            {
                Success = false,
                ErrorMessage = $"Login failed: {errorMessage}"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] Login failed: {ex.Message}");
            return new AuthResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<AuthResult> LoginWithApiKeyAsync(string apiKey)
    {
        if (_httpClient == null)
        {
            return new AuthResult
            {
                Success = false,
                ErrorMessage = "HTTP client not configured"
            };
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/validate");
            request.Headers.Add("X-API-Key", apiKey);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ValidateResponse>();
                if (result != null)
                {
                    _currentApiKey = apiKey;

                    _currentUser = new AuthenticatedUser
                    {
                        UserId = result.UserId ?? "api-key-user",
                        Username = result.Username ?? "API User",
                        Role = ParseRole(result.Role),
                        Permissions = ParsePermissions(result.Permissions),
                        IsLocalUser = false
                    };

                    await SaveSessionAsync();
                    RaiseAuthStateChanged(null, _currentUser);

                    return new AuthResult
                    {
                        Success = true,
                        User = _currentUser
                    };
                }
            }

            return new AuthResult
            {
                Success = false,
                ErrorMessage = "Invalid API key"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] API key login failed: {ex.Message}");
            return new AuthResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task LogoutAsync()
    {
        var previousUser = _currentUser;

        _currentUser = null;
        _currentToken = null;
        _currentApiKey = null;
        _refreshToken = null;
        _tokenExpiry = null;

        await ClearSessionAsync();
        RaiseAuthStateChanged(previousUser, null);

        // Create local session if auth not required
        if (!IsAuthRequired)
        {
            await CreateLocalSessionAsync();
        }
    }

    public async Task<AuthResult> RefreshTokenAsync()
    {
        if (_refreshToken == null || _httpClient == null)
        {
            return new AuthResult
            {
                Success = false,
                ErrorMessage = "No refresh token available"
            };
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/refresh", new
            {
                refresh_token = _refreshToken
            });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (result != null)
                {
                    _currentToken = result.AccessToken;
                    _refreshToken = result.RefreshToken;
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(result.ExpiresIn);

                    if (_currentUser != null)
                    {
                        // Create new instance with updated TokenExpiry (AuthenticatedUser is a class, not record)
                        _currentUser = new AuthenticatedUser
                        {
                            UserId = _currentUser.UserId,
                            Username = _currentUser.Username,
                            Role = _currentUser.Role,
                            Permissions = _currentUser.Permissions,
                            TokenExpiry = _tokenExpiry,
                            IsLocalUser = _currentUser.IsLocalUser
                        };
                    }

                    await SaveSessionAsync();

                    return new AuthResult
                    {
                        Success = true,
                        User = _currentUser,
                        Token = _currentToken,
                        RefreshToken = _refreshToken,
                        ExpiresAt = _tokenExpiry
                    };
                }
            }

            return new AuthResult
            {
                Success = false,
                ErrorMessage = "Token refresh failed"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] Token refresh failed: {ex.Message}");
            return new AuthResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public Task<AuthResult> CreateLocalSessionAsync()
    {
        var previousUser = _currentUser;

        _currentUser = new AuthenticatedUser
        {
            UserId = "local",
            Username = Environment.UserName,
            Role = UserRole.Admin, // Local user has full access
            Permissions = new List<Permission>
            {
                Permission.Read,
                Permission.Write,
                Permission.Delete,
                Permission.Admin,
                Permission.ManageUsers,
                Permission.ManageEngines,
                Permission.ManageSettings,
                Permission.ExportData,
                Permission.ImportData,
                Permission.TrainModels
            },
            IsLocalUser = true
        };

        RaiseAuthStateChanged(previousUser, _currentUser);

        return Task.FromResult(new AuthResult
        {
            Success = true,
            User = _currentUser
        });
    }

    #endregion

    #region Authorization Methods

    public bool HasPermission(Permission permission)
    {
        return _currentUser?.HasPermission(permission) ?? false;
    }

    public bool HasRole(UserRole role)
    {
        return _currentUser?.HasRole(role) ?? false;
    }

    public void RequirePermission(Permission permission)
    {
        if (!IsAuthenticated)
            throw new AuthenticationRequiredException();

        if (!HasPermission(permission))
            throw new AuthorizationException(permission);
    }

    public void RequireRole(UserRole role)
    {
        if (!IsAuthenticated)
            throw new AuthenticationRequiredException();

        if (!HasRole(role))
            throw new AuthorizationException(role);
    }

    #endregion

    #region Token Management

    public string? GetCurrentToken() => _currentToken;

    public string? GetCurrentApiKey() => _currentApiKey;

    public bool IsTokenExpiringSoon(TimeSpan threshold)
    {
        if (_tokenExpiry == null)
            return false;

        return DateTime.UtcNow + threshold >= _tokenExpiry;
    }

    #endregion

    #region Private Methods

    private async Task RestoreSessionAsync()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            // Try to restore token
            if (localSettings.Values.TryGetValue(TokenStorageKey, out var tokenObj) && tokenObj is string token)
            {
                _currentToken = token;
            }

            // Try to restore refresh token
            if (localSettings.Values.TryGetValue(RefreshTokenStorageKey, out var refreshObj) && refreshObj is string refreshToken)
            {
                _refreshToken = refreshToken;
            }

            // Try to restore API key
            if (localSettings.Values.TryGetValue(ApiKeyStorageKey, out var apiKeyObj) && apiKeyObj is string apiKey)
            {
                _currentApiKey = apiKey;
            }

            // Try to restore user info
            if (localSettings.Values.TryGetValue(UserStorageKey, out var userObj) && userObj is string userJson)
            {
                var userInfo = JsonSerializer.Deserialize<StoredUserInfo>(userJson);
                if (userInfo != null)
                {
                    _currentUser = new AuthenticatedUser
                    {
                        UserId = userInfo.UserId,
                        Username = userInfo.Username,
                        Role = Enum.TryParse<UserRole>(userInfo.Role, out var role) ? role : UserRole.User,
                        Permissions = ParsePermissions(userInfo.Permissions),
                        TokenExpiry = userInfo.TokenExpiry,
                        IsLocalUser = userInfo.IsLocalUser
                    };
                }
            }

            // If we have a token but it's expired, try to refresh
            if (_currentToken != null && _currentUser?.TokenExpiry < DateTime.UtcNow)
            {
                var refreshResult = await RefreshTokenAsync();
                if (!refreshResult.Success)
                {
                    // Token refresh failed, clear session
                    await ClearSessionAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] Session restore failed: {ex.Message}");
        }
    }

    private async Task SaveSessionAsync()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            if (_currentToken != null)
                localSettings.Values[TokenStorageKey] = _currentToken;

            if (_refreshToken != null)
                localSettings.Values[RefreshTokenStorageKey] = _refreshToken;

            if (_currentApiKey != null)
                localSettings.Values[ApiKeyStorageKey] = _currentApiKey;

            if (_currentUser != null)
            {
                var userInfo = new StoredUserInfo
                {
                    UserId = _currentUser.UserId,
                    Username = _currentUser.Username,
                    Role = _currentUser.Role.ToString(),
                    Permissions = new List<string>(
                        _currentUser.Permissions is List<Permission> perms
                            ? perms.ConvertAll(p => p.ToString())
                            : Array.Empty<string>()),
                    TokenExpiry = _currentUser.TokenExpiry,
                    IsLocalUser = _currentUser.IsLocalUser
                };
                localSettings.Values[UserStorageKey] = JsonSerializer.Serialize(userInfo);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] Session save failed: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private async Task ClearSessionAsync()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values.Remove(TokenStorageKey);
            localSettings.Values.Remove(RefreshTokenStorageKey);
            localSettings.Values.Remove(ApiKeyStorageKey);
            localSettings.Values.Remove(UserStorageKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] Session clear failed: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private void RaiseAuthStateChanged(AuthenticatedUser? previous, AuthenticatedUser? current)
    {
        AuthStateChanged?.Invoke(this, new AuthStateChangedEventArgs
        {
            PreviousUser = previous,
            CurrentUser = current
        });
    }

    private static UserRole ParseRole(string? role)
    {
        if (string.IsNullOrEmpty(role))
            return UserRole.User;

        return Enum.TryParse<UserRole>(role, true, out var result) ? result : UserRole.User;
    }

    private static IReadOnlyList<Permission> ParsePermissions(IEnumerable<string>? permissions)
    {
        if (permissions == null)
            return Array.Empty<Permission>();

        var result = new List<Permission>();
        foreach (var perm in permissions)
        {
            if (Enum.TryParse<Permission>(perm, true, out var permission))
            {
                result.Add(permission);
            }
        }
        return result;
    }

    #endregion

    #region Response Models

    private class LoginResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public string? UserId { get; set; }
        public string? Username { get; set; }
        public string? Role { get; set; }
        public List<string>? Permissions { get; set; }
    }

    private class ValidateResponse
    {
        public string? UserId { get; set; }
        public string? Username { get; set; }
        public string? Role { get; set; }
        public List<string>? Permissions { get; set; }
    }

    private class StoredUserInfo
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        public DateTime? TokenExpiry { get; set; }
        public bool IsLocalUser { get; set; }
    }

    #endregion
}
