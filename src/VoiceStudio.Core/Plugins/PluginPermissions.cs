// VoiceStudio Plugin Permission System
// Phase 1: Defines capability-based permissions for plugin security

using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceStudio.Core.Plugins
{
    /// <summary>
    /// Defines all available plugin permissions following the capability-based security model.
    /// Permissions are grouped by resource type and use least-privilege principle.
    /// </summary>
    public static class PluginPermissions
    {
        // =============================================
        // FILE SYSTEM PERMISSIONS
        // =============================================
        
        /// <summary>Read files from the plugin's own directory only.</summary>
        public const string FileReadSelf = "filesystem.read.self";
        
        /// <summary>Write files to the plugin's own directory only.</summary>
        public const string FileWriteSelf = "filesystem.write.self";
        
        /// <summary>Read files from user-selected directories (with picker dialog).</summary>
        public const string FileReadUserSelected = "filesystem.read.user_selected";
        
        /// <summary>Write files to user-selected directories (with picker dialog).</summary>
        public const string FileWriteUserSelected = "filesystem.write.user_selected";
        
        /// <summary>Read files from the project workspace directory.</summary>
        public const string FileReadWorkspace = "filesystem.read.workspace";
        
        /// <summary>Write files to the project workspace directory.</summary>
        public const string FileWriteWorkspace = "filesystem.write.workspace";
        
        /// <summary>Execute files (scripts, binaries) - requires explicit user consent.</summary>
        public const string FileExecute = "filesystem.execute";

        // =============================================
        // NETWORK PERMISSIONS
        // =============================================
        
        /// <summary>Make HTTP/HTTPS requests to localhost only.</summary>
        public const string NetworkLocalhost = "network.localhost";
        
        /// <summary>Make HTTP/HTTPS requests to specific allowed domains.</summary>
        public const string NetworkAllowedDomains = "network.allowed_domains";
        
        /// <summary>Make HTTP/HTTPS requests to any domain - requires explicit user consent.</summary>
        public const string NetworkAny = "network.any";
        
        /// <summary>Listen on localhost ports (for local servers).</summary>
        public const string NetworkListen = "network.listen";

        // =============================================
        // AUDIO PERMISSIONS
        // =============================================
        
        /// <summary>Read audio data from the current project.</summary>
        public const string AudioRead = "audio.input";
        
        /// <summary>Write/modify audio data in the current project.</summary>
        public const string AudioWrite = "audio.output";
        
        /// <summary>Process audio in real-time (effects, transformations).</summary>
        public const string AudioProcess = "audio.process";
        
        /// <summary>Access microphone input - requires explicit user consent.</summary>
        public const string AudioCapture = "audio.capture";
        
        /// <summary>Play audio through system speakers.</summary>
        public const string AudioPlayback = "audio.playback";

        // =============================================
        // ENGINE PERMISSIONS
        // =============================================
        
        /// <summary>Invoke TTS engines for synthesis.</summary>
        public const string EngineInvokeTts = "engine.tts";
        
        /// <summary>Invoke STT engines for transcription.</summary>
        public const string EngineInvokeStt = "engine.stt";
        
        /// <summary>Invoke voice conversion engines.</summary>
        public const string EngineInvokeVc = "engine.vc";
        
        /// <summary>Access engine configuration and settings.</summary>
        public const string EngineConfig = "engine.config";
        
        /// <summary>Register new engines or engine adapters.</summary>
        public const string EngineRegister = "engine.register";

        // =============================================
        // UI PERMISSIONS
        // =============================================
        
        /// <summary>Display notifications to the user.</summary>
        public const string UiNotify = "ui.notify";
        
        /// <summary>Show dialogs and modal windows.</summary>
        public const string UiDialog = "ui.dialog";
        
        /// <summary>Add items to context menus.</summary>
        public const string UiContextMenu = "ui.context_menu";
        
        /// <summary>Add toolbar buttons or panels.</summary>
        public const string UiToolbar = "ui.toolbar";
        
        /// <summary>Register a custom panel in the workspace.</summary>
        public const string UiPanel = "ui.panel";
        
        /// <summary>Modify application theme or styling.</summary>
        public const string UiTheme = "ui.theme";

        // =============================================
        // DATA PERMISSIONS
        // =============================================
        
        /// <summary>Read project metadata and settings.</summary>
        public const string DataReadProject = "data.project.read";
        
        /// <summary>Write project metadata and settings.</summary>
        public const string DataWriteProject = "data.project.write";
        
        /// <summary>Read application settings.</summary>
        public const string DataReadSettings = "data.settings.read";
        
        /// <summary>Write application settings - requires explicit user consent.</summary>
        public const string DataWriteSettings = "data.settings.write";
        
        /// <summary>Store persistent plugin data (key-value storage).</summary>
        public const string DataStorage = "data.storage";

        // =============================================
        // SYSTEM PERMISSIONS
        // =============================================
        
        /// <summary>Access system information (CPU, memory, GPU).</summary>
        public const string SystemInfo = "system.info";
        
        /// <summary>Execute system commands - requires explicit user consent.</summary>
        public const string SystemExecute = "system.process";
        
        /// <summary>Access clipboard for read operations.</summary>
        public const string SystemClipboardRead = "system.clipboard.read";
        
        /// <summary>Access clipboard for write operations.</summary>
        public const string SystemClipboardWrite = "system.clipboard.write";

        // =============================================
        // INTEGRATION PERMISSIONS
        // =============================================
        
        /// <summary>Communicate with other plugins via events.</summary>
        public const string IntegrationPluginEvents = "integration.plugin_events";
        
        /// <summary>Call functions exposed by other plugins.</summary>
        public const string IntegrationPluginCall = "integration.plugin_call";
        
        /// <summary>Access external API integrations.</summary>
        public const string IntegrationExternalApi = "integration.external_api";

        /// <summary>
        /// Gets all defined permissions grouped by category.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<PermissionInfo>> GetPermissionsByCategory()
        {
            return new Dictionary<string, IReadOnlyList<PermissionInfo>>
            {
                ["File System"] = new[]
                {
                    new PermissionInfo(FileReadSelf, "Read Own Files", "Read files from the plugin's own directory", PermissionRisk.Low),
                    new PermissionInfo(FileWriteSelf, "Write Own Files", "Write files to the plugin's own directory", PermissionRisk.Low),
                    new PermissionInfo(FileReadUserSelected, "Read User-Selected Files", "Read files from directories you choose", PermissionRisk.Medium),
                    new PermissionInfo(FileWriteUserSelected, "Write User-Selected Files", "Write files to directories you choose", PermissionRisk.Medium),
                    new PermissionInfo(FileReadWorkspace, "Read Workspace", "Read files from project workspace", PermissionRisk.Medium),
                    new PermissionInfo(FileWriteWorkspace, "Write Workspace", "Write files to project workspace", PermissionRisk.Medium),
                    new PermissionInfo(FileExecute, "Execute Files", "Run scripts or executables", PermissionRisk.High),
                },
                ["Network"] = new[]
                {
                    new PermissionInfo(NetworkLocalhost, "Local Network", "Connect to localhost only", PermissionRisk.Low),
                    new PermissionInfo(NetworkAllowedDomains, "Specific Domains", "Connect to pre-approved domains", PermissionRisk.Medium),
                    new PermissionInfo(NetworkAny, "Any Network", "Connect to any internet address", PermissionRisk.High),
                    new PermissionInfo(NetworkListen, "Listen on Port", "Accept connections on a local port", PermissionRisk.Medium),
                },
                ["Audio"] = new[]
                {
                    new PermissionInfo(AudioRead, "Read Audio", "Access audio from current project", PermissionRisk.Low),
                    new PermissionInfo(AudioWrite, "Write Audio", "Modify audio in current project", PermissionRisk.Medium),
                    new PermissionInfo(AudioProcess, "Process Audio", "Apply real-time audio processing", PermissionRisk.Low),
                    new PermissionInfo(AudioCapture, "Capture Microphone", "Access microphone input", PermissionRisk.High),
                    new PermissionInfo(AudioPlayback, "Play Audio", "Play audio through speakers", PermissionRisk.Low),
                },
                ["Engines"] = new[]
                {
                    new PermissionInfo(EngineInvokeTts, "Use TTS Engines", "Generate speech from text", PermissionRisk.Low),
                    new PermissionInfo(EngineInvokeStt, "Use STT Engines", "Transcribe speech to text", PermissionRisk.Low),
                    new PermissionInfo(EngineInvokeVc, "Use Voice Conversion", "Transform voice characteristics", PermissionRisk.Low),
                    new PermissionInfo(EngineConfig, "Engine Configuration", "Access engine settings", PermissionRisk.Medium),
                    new PermissionInfo(EngineRegister, "Register Engines", "Add new engines to the system", PermissionRisk.High),
                },
                ["User Interface"] = new[]
                {
                    new PermissionInfo(UiNotify, "Show Notifications", "Display notification messages", PermissionRisk.Low),
                    new PermissionInfo(UiDialog, "Show Dialogs", "Display modal dialogs", PermissionRisk.Low),
                    new PermissionInfo(UiContextMenu, "Context Menus", "Add items to right-click menus", PermissionRisk.Low),
                    new PermissionInfo(UiToolbar, "Toolbar Items", "Add buttons to toolbars", PermissionRisk.Low),
                    new PermissionInfo(UiPanel, "Custom Panels", "Add panels to workspace", PermissionRisk.Medium),
                    new PermissionInfo(UiTheme, "Modify Theme", "Change application appearance", PermissionRisk.Medium),
                },
                ["Data"] = new[]
                {
                    new PermissionInfo(DataReadProject, "Read Project Data", "Access project metadata", PermissionRisk.Low),
                    new PermissionInfo(DataWriteProject, "Write Project Data", "Modify project metadata", PermissionRisk.Medium),
                    new PermissionInfo(DataReadSettings, "Read Settings", "Access application settings", PermissionRisk.Low),
                    new PermissionInfo(DataWriteSettings, "Write Settings", "Modify application settings", PermissionRisk.High),
                    new PermissionInfo(DataStorage, "Plugin Storage", "Store persistent plugin data", PermissionRisk.Low),
                },
                ["System"] = new[]
                {
                    new PermissionInfo(SystemInfo, "System Information", "Read CPU, memory, GPU info", PermissionRisk.Low),
                    new PermissionInfo(SystemExecute, "Execute Commands", "Run system commands", PermissionRisk.High),
                    new PermissionInfo(SystemClipboardRead, "Read Clipboard", "Access clipboard contents", PermissionRisk.Medium),
                    new PermissionInfo(SystemClipboardWrite, "Write Clipboard", "Set clipboard contents", PermissionRisk.Low),
                },
                ["Integration"] = new[]
                {
                    new PermissionInfo(IntegrationPluginEvents, "Plugin Events", "Send/receive plugin events", PermissionRisk.Low),
                    new PermissionInfo(IntegrationPluginCall, "Plugin Calls", "Call other plugin functions", PermissionRisk.Medium),
                    new PermissionInfo(IntegrationExternalApi, "External APIs", "Access external API services", PermissionRisk.Medium),
                },
            };
        }

        /// <summary>
        /// Gets all permissions that require explicit user consent (high risk).
        /// </summary>
        public static IReadOnlyList<string> GetHighRiskPermissions()
        {
            return new[]
            {
                FileExecute,
                NetworkAny,
                AudioCapture,
                EngineRegister,
                DataWriteSettings,
                SystemExecute,
            };
        }

        /// <summary>
        /// Validates a permission string format (dot-notation: category.action or category.action.scope).
        /// </summary>
        public static bool IsValidPermission(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
                return false;

            var parts = permission.Split('.');
            return parts.Length >= 2 && parts.Length <= 4 &&
                   parts.All(p => !string.IsNullOrWhiteSpace(p) && p.All(c => char.IsLetterOrDigit(c) || c == '_'));
        }

        /// <summary>
        /// Gets the risk level for a permission.
        /// </summary>
        public static PermissionRisk GetPermissionRisk(string permission)
        {
            var allPermissions = GetPermissionsByCategory()
                .Values
                .SelectMany(x => x)
                .ToDictionary(p => p.Id, p => p.Risk);

            return allPermissions.TryGetValue(permission, out var risk) ? risk : PermissionRisk.Unknown;
        }

        /// <summary>
        /// Gets detailed information about a permission.
        /// </summary>
        public static PermissionInfo? GetPermissionInfo(string permission)
        {
            return GetPermissionsByCategory()
                .Values
                .SelectMany(x => x)
                .FirstOrDefault(p => p.Id == permission);
        }
    }

    /// <summary>
    /// Risk level classification for permissions.
    /// </summary>
    public enum PermissionRisk
    {
        /// <summary>Unknown risk level.</summary>
        Unknown = 0,

        /// <summary>Low risk - minimal security impact.</summary>
        Low = 1,

        /// <summary>Medium risk - requires user awareness.</summary>
        Medium = 2,

        /// <summary>High risk - requires explicit user consent.</summary>
        High = 3,
    }

    /// <summary>
    /// Information about a single permission.
    /// </summary>
    public sealed record PermissionInfo(
        string Id,
        string DisplayName,
        string Description,
        PermissionRisk Risk)
    {
        /// <summary>
        /// Whether this permission requires explicit user consent.
        /// </summary>
        public bool RequiresExplicitConsent => Risk == PermissionRisk.High;

        /// <summary>
        /// Whether the caller should show a notification when this permission is auto-granted (Medium or High).
        /// </summary>
        public bool RequiresNotification => Risk == PermissionRisk.Medium || Risk == PermissionRisk.High;
    }

    /// <summary>
    /// Permission grant status for a plugin.
    /// </summary>
    public enum PermissionStatus
    {
        /// <summary>Permission not yet requested.</summary>
        NotRequested = 0,

        /// <summary>Permission requested but pending user decision.</summary>
        Pending = 1,

        /// <summary>Permission granted by user.</summary>
        Granted = 2,

        /// <summary>Permission denied by user.</summary>
        Denied = 3,

        /// <summary>Permission revoked after being previously granted.</summary>
        Revoked = 4,
    }

    /// <summary>
    /// Represents a permission grant for a specific plugin.
    /// </summary>
    public sealed record PermissionGrant
    {
        /// <summary>The plugin ID this grant applies to.</summary>
        public string PluginId { get; init; } = string.Empty;

        /// <summary>The permission ID being granted/denied.</summary>
        public string PermissionId { get; init; } = string.Empty;

        /// <summary>Current status of this permission.</summary>
        public PermissionStatus Status { get; init; } = PermissionStatus.NotRequested;

        /// <summary>When the permission was granted/denied.</summary>
        public DateTime? DecisionTime { get; init; }

        /// <summary>When the grant expires (null = permanent).</summary>
        public DateTime? ExpiresAt { get; init; }

        /// <summary>Additional scope constraints (e.g., allowed domains for network).</summary>
        public IReadOnlyList<string>? ScopeConstraints { get; init; }

        /// <summary>Whether this grant is currently valid.</summary>
        public bool IsValid => Status == PermissionStatus.Granted &&
                               (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
    }
}
