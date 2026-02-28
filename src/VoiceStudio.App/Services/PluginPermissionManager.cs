// VoiceStudio Plugin Permission Manager
// Phase 1: Manages permission lifecycle for plugins

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Plugins;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Manages plugin permissions including requesting, granting, revoking, and persisting.
    /// </summary>
    public sealed class PluginPermissionManager : IDisposable
    {
        private readonly Dictionary<string, Dictionary<string, PermissionGrant>> _grants = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly string _persistencePath;
        private bool _loaded;
        private bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Event raised when permission status changes.
        /// </summary>
        public event EventHandler<PermissionChangedEventArgs>? PermissionChanged;

        public PluginPermissionManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _persistencePath = Path.Combine(appData, "VoiceStudio", "plugin_permissions.json");
        }

        /// <summary>
        /// Load persisted permissions from disk.
        /// </summary>
        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            if (_loaded)
                return;

            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_loaded)
                    return;

                if (File.Exists(_persistencePath))
                {
                    var json = await File.ReadAllTextAsync(_persistencePath, cancellationToken);
                    var data = JsonSerializer.Deserialize<PermissionPersistenceData>(json, JsonOptions);

                    if (data?.Grants != null)
                    {
                        foreach (var grant in data.Grants)
                        {
                            var permissionId = MigratePermissionIdToDotNotation(grant.PermissionId);
                            if (!_grants.TryGetValue(grant.PluginId, out var pluginGrants))
                            {
                                pluginGrants = new Dictionary<string, PermissionGrant>();
                                _grants[grant.PluginId] = pluginGrants;
                            }
                            pluginGrants[permissionId] = grant with { PermissionId = permissionId };
                        }
                    }
                }

                _loaded = true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Save permissions to disk.
        /// </summary>
        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var allGrants = _grants.Values
                    .SelectMany(x => x.Values)
                    .ToList();

                var data = new PermissionPersistenceData { Grants = allGrants };
                var json = JsonSerializer.Serialize(data, JsonOptions);

                var directory = Path.GetDirectoryName(_persistencePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(_persistencePath, json, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Gets all permissions for a plugin.
        /// </summary>
        public IReadOnlyDictionary<string, PermissionGrant> GetPluginPermissions(string pluginId)
        {
            if (_grants.TryGetValue(pluginId, out var permissions))
            {
                return permissions;
            }
            return new Dictionary<string, PermissionGrant>();
        }

        /// <summary>
        /// Gets the status of a specific permission for a plugin.
        /// </summary>
        public PermissionStatus GetPermissionStatus(string pluginId, string permissionId)
        {
            if (_grants.TryGetValue(pluginId, out var permissions) &&
                permissions.TryGetValue(permissionId, out var grant))
            {
                // Check expiration
                if (grant.ExpiresAt.HasValue && grant.ExpiresAt < DateTime.UtcNow)
                {
                    return PermissionStatus.NotRequested;
                }
                return grant.Status;
            }
            return PermissionStatus.NotRequested;
        }

        /// <summary>
        /// Checks if a plugin has a specific permission granted.
        /// </summary>
        public bool HasPermission(string pluginId, string permissionId)
        {
            var status = GetPermissionStatus(pluginId, permissionId);
            return status == PermissionStatus.Granted;
        }

        /// <summary>
        /// Checks if a plugin has all of the specified permissions.
        /// </summary>
        public bool HasAllPermissions(string pluginId, IEnumerable<string> permissionIds)
        {
            return permissionIds.All(p => HasPermission(pluginId, p));
        }

        /// <summary>
        /// Checks if a plugin has any of the specified permissions.
        /// </summary>
        public bool HasAnyPermission(string pluginId, IEnumerable<string> permissionIds)
        {
            return permissionIds.Any(p => HasPermission(pluginId, p));
        }

        /// <summary>
        /// Requests permissions for a plugin. Returns permissions that need user consent.
        /// </summary>
        public async Task<PermissionRequestResult> RequestPermissionsAsync(
            string pluginId,
            IEnumerable<string> permissionIds,
            CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var needsConsent = new List<string>();
                var alreadyGranted = new List<string>();
                var alreadyDenied = new List<string>();
                var autoGranted = new List<string>();
                var notifiedPermissions = new List<string>();

                foreach (var permissionId in permissionIds)
                {
                    if (!PluginPermissions.IsValidPermission(permissionId))
                        continue;

                    var currentStatus = GetPermissionStatus(pluginId, permissionId);

                    switch (currentStatus)
                    {
                        case PermissionStatus.Granted:
                            alreadyGranted.Add(permissionId);
                            break;

                        case PermissionStatus.Denied:
                        case PermissionStatus.Revoked:
                            alreadyDenied.Add(permissionId);
                            break;

                        default:
                            var risk = PluginPermissions.GetPermissionRisk(permissionId);
                            if (risk == PermissionRisk.High)
                            {
                                needsConsent.Add(permissionId);
                                SetPermissionStatus(pluginId, permissionId, PermissionStatus.Pending);
                            }
                            else if (risk == PermissionRisk.Medium)
                            {
                                SetPermissionStatus(pluginId, permissionId, PermissionStatus.Granted);
                                autoGranted.Add(permissionId);
                                notifiedPermissions.Add(permissionId);
                            }
                            else
                            {
                                SetPermissionStatus(pluginId, permissionId, PermissionStatus.Granted);
                                autoGranted.Add(permissionId);
                            }
                            break;
                    }
                }

                return new PermissionRequestResult
                {
                    PluginId = pluginId,
                    NeedsUserConsent = needsConsent,
                    AlreadyGranted = alreadyGranted,
                    AlreadyDenied = alreadyDenied,
                    AutoGranted = autoGranted,
                    NotifiedPermissions = notifiedPermissions
                };
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Grants a permission to a plugin.
        /// </summary>
        public async Task GrantPermissionAsync(
            string pluginId,
            string permissionId,
            TimeSpan? duration = null,
            IReadOnlyList<string>? scopeConstraints = null,
            CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var grant = new PermissionGrant
                {
                    PluginId = pluginId,
                    PermissionId = permissionId,
                    Status = PermissionStatus.Granted,
                    DecisionTime = DateTime.UtcNow,
                    ExpiresAt = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null,
                    ScopeConstraints = scopeConstraints
                };

                SetGrant(pluginId, permissionId, grant);
            }
            finally
            {
                _lock.Release();
            }

            OnPermissionChanged(new PermissionChangedEventArgs
            {
                PluginId = pluginId,
                PermissionId = permissionId,
                NewStatus = PermissionStatus.Granted
            });

            await SaveAsync(cancellationToken);
        }

        /// <summary>
        /// Denies a permission to a plugin.
        /// </summary>
        public async Task DenyPermissionAsync(
            string pluginId,
            string permissionId,
            CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var grant = new PermissionGrant
                {
                    PluginId = pluginId,
                    PermissionId = permissionId,
                    Status = PermissionStatus.Denied,
                    DecisionTime = DateTime.UtcNow
                };

                SetGrant(pluginId, permissionId, grant);
            }
            finally
            {
                _lock.Release();
            }

            OnPermissionChanged(new PermissionChangedEventArgs
            {
                PluginId = pluginId,
                PermissionId = permissionId,
                NewStatus = PermissionStatus.Denied
            });

            await SaveAsync(cancellationToken);
        }

        /// <summary>
        /// Revokes a previously granted permission.
        /// </summary>
        public async Task RevokePermissionAsync(
            string pluginId,
            string permissionId,
            CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_grants.TryGetValue(pluginId, out var permissions) &&
                    permissions.TryGetValue(permissionId, out var existing))
                {
                    var grant = existing with
                    {
                        Status = PermissionStatus.Revoked,
                        DecisionTime = DateTime.UtcNow
                    };

                    permissions[permissionId] = grant;
                }
            }
            finally
            {
                _lock.Release();
            }

            OnPermissionChanged(new PermissionChangedEventArgs
            {
                PluginId = pluginId,
                PermissionId = permissionId,
                NewStatus = PermissionStatus.Revoked
            });

            await SaveAsync(cancellationToken);
        }

        /// <summary>
        /// Revokes all permissions for a plugin.
        /// </summary>
        public async Task RevokeAllPermissionsAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            List<string> revokedPermissions;

            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (!_grants.TryGetValue(pluginId, out var permissions))
                    return;

                revokedPermissions = permissions.Keys.ToList();

                foreach (var permissionId in revokedPermissions)
                {
                    var existing = permissions[permissionId];
                    permissions[permissionId] = existing with
                    {
                        Status = PermissionStatus.Revoked,
                        DecisionTime = DateTime.UtcNow
                    };
                }
            }
            finally
            {
                _lock.Release();
            }

            foreach (var permissionId in revokedPermissions)
            {
                OnPermissionChanged(new PermissionChangedEventArgs
                {
                    PluginId = pluginId,
                    PermissionId = permissionId,
                    NewStatus = PermissionStatus.Revoked
                });
            }

            await SaveAsync(cancellationToken);
        }

        /// <summary>
        /// Gets all plugins that have any permissions.
        /// </summary>
        public IReadOnlyList<string> GetPluginsWithPermissions()
        {
            return _grants.Keys.ToList();
        }

        /// <summary>
        /// Cleans up expired permissions.
        /// </summary>
        public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var toRemove = new List<(string pluginId, string permissionId)>();

                foreach (var (pluginId, permissions) in _grants)
                {
                    foreach (var (permissionId, grant) in permissions)
                    {
                        if (grant.ExpiresAt.HasValue && grant.ExpiresAt < now)
                        {
                            toRemove.Add((pluginId, permissionId));
                        }
                    }
                }

                foreach (var (pluginId, permissionId) in toRemove)
                {
                    if (_grants.TryGetValue(pluginId, out var permissions))
                    {
                        permissions.Remove(permissionId);
                    }
                }
            }
            finally
            {
                _lock.Release();
            }

            await SaveAsync(cancellationToken);
        }

        private void SetPermissionStatus(string pluginId, string permissionId, PermissionStatus status)
        {
            var grant = new PermissionGrant
            {
                PluginId = pluginId,
                PermissionId = permissionId,
                Status = status,
                DecisionTime = DateTime.UtcNow
            };

            SetGrant(pluginId, permissionId, grant);
        }

        private void SetGrant(string pluginId, string permissionId, PermissionGrant grant)
        {
            if (!_grants.TryGetValue(pluginId, out var permissions))
            {
                permissions = new Dictionary<string, PermissionGrant>();
                _grants[pluginId] = permissions;
            }
            permissions[permissionId] = grant;
        }

        private void OnPermissionChanged(PermissionChangedEventArgs e)
        {
            PermissionChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Migrates persisted permission IDs from colon-notation to dot-notation.
        /// </summary>
        private static string MigratePermissionIdToDotNotation(string permissionId)
        {
            if (string.IsNullOrEmpty(permissionId) || !permissionId.Contains(':', StringComparison.Ordinal))
                return permissionId;

            return permissionId switch
            {
                "file:read:self" => "filesystem.read.self",
                "file:write:self" => "filesystem.write.self",
                "file:read:user_selected" => "filesystem.read.user_selected",
                "file:write:user_selected" => "filesystem.write.user_selected",
                "file:read:workspace" => "filesystem.read.workspace",
                "file:write:workspace" => "filesystem.write.workspace",
                "file:execute" => "filesystem.execute",
                "network:localhost" => "network.localhost",
                "network:allowed_domains" => "network.allowed_domains",
                "network:any" => "network.any",
                "network:listen" => "network.listen",
                "audio:read" => "audio.input",
                "audio:write" => "audio.output",
                "audio:process" => "audio.process",
                "audio:capture" => "audio.input",
                "audio:playback" => "audio.output",
                "engine:invoke:tts" => "engine.tts",
                "engine:invoke:stt" => "engine.stt",
                "engine:invoke:vc" => "engine.vc",
                "engine:config" => "engine.config",
                "engine:register" => "engine.register",
                "ui:notify" => "ui.notify",
                "ui:dialog" => "ui.dialog",
                "ui:context_menu" => "ui.context_menu",
                "ui:toolbar" => "ui.toolbar",
                "ui:panel" => "ui.panel",
                "ui:theme" => "ui.theme",
                "data:read:project" => "data.project.read",
                "data:write:project" => "data.project.write",
                "data:read:settings" => "data.settings.read",
                "data:write:settings" => "data.settings.write",
                "data:storage" => "data.storage",
                "system:info" => "system.info",
                "system:execute" => "system.process",
                "system:clipboard:read" => "system.clipboard.read",
                "system:clipboard:write" => "system.clipboard.write",
                "integration:plugin_events" => "integration.plugin_events",
                "integration:plugin_call" => "integration.plugin_call",
                "integration:external_api" => "integration.external_api",
                _ => permissionId.Replace(':', '.'),
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lock.Dispose();
        }

        private sealed class PermissionPersistenceData
        {
            public int Version { get; set; } = 1;
            public List<PermissionGrant> Grants { get; set; } = new();
        }
    }

    /// <summary>
    /// Result of a permission request operation.
    /// </summary>
    public sealed record PermissionRequestResult
    {
        public string PluginId { get; init; } = string.Empty;
        public IReadOnlyList<string> NeedsUserConsent { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AlreadyGranted { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AlreadyDenied { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AutoGranted { get; init; } = Array.Empty<string>();
        /// <summary>Medium-risk permissions that were auto-granted; caller should show a non-blocking notification.</summary>
        public IReadOnlyList<string> NotifiedPermissions { get; init; } = Array.Empty<string>();

        public bool RequiresUserInteraction => NeedsUserConsent.Count > 0;
        public bool AllGranted => NeedsUserConsent.Count == 0 && AlreadyDenied.Count == 0;
    }

    /// <summary>
    /// Event args for permission changes.
    /// </summary>
    public sealed class PermissionChangedEventArgs : EventArgs
    {
        public string PluginId { get; init; } = string.Empty;
        public string PermissionId { get; init; } = string.Empty;
        public PermissionStatus NewStatus { get; init; }
    }
}
