using System;
using System.Diagnostics;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Commands
{
    /// <summary>
    /// Bootstrapper that initializes all command handlers and registers them with the unified registry.
    /// Should be called during application startup after DI container is built.
    /// </summary>
    public sealed class CommandHandlerBootstrapper
    {
        private static CommandHandlerBootstrapper? _instance;

        private readonly IUnifiedCommandRegistry _registry;
        private readonly IServiceProvider _serviceProvider;

        private FileOperationsHandler? _fileHandler;
        private ProfileOperationsHandler? _profileHandler;
        private PlaybackOperationsHandler? _playbackHandler;
        private NavigationHandler? _navigationHandler;
        private SettingsOperationsHandler? _settingsHandler;

        private CommandHandlerBootstrapper(
            IUnifiedCommandRegistry registry,
            IServiceProvider serviceProvider)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Static initialization method. Creates an instance and initializes all command handlers.
        /// </summary>
        public static void Initialize()
        {
            if (_instance != null)
            {
                Debug.WriteLine("[CommandHandlerBootstrapper] Already initialized, skipping");
                return;
            }

            var registry = AppServices.TryGetCommandRegistry();
            if (registry == null)
            {
                Debug.WriteLine("[CommandHandlerBootstrapper] Registry not available");
                return;
            }

            // Create a minimal service resolver using AppServices
            var serviceProvider = new AppServicesAdapter();

            _instance = new CommandHandlerBootstrapper(registry, serviceProvider);
            _instance.InitializeHandlers();
        }

        /// <summary>
        /// Gets the singleton instance (if initialized).
        /// </summary>
        public static CommandHandlerBootstrapper? Instance => _instance;

        /// <summary>
        /// Initializes all command handlers (instance method).
        /// </summary>
        private void InitializeHandlers()
        {
            Debug.WriteLine("[CommandHandlerBootstrapper] Initializing command handlers...");

            try
            {
                InitializeFileOperationsHandler();
                InitializeProfileOperationsHandler();
                InitializePlaybackOperationsHandler();
                InitializeNavigationHandler();
                InitializeSettingsOperationsHandler();

                // Log health report after initialization
                if (_registry is UnifiedCommandRegistry unifiedRegistry)
                {
                    Debug.WriteLine(unifiedRegistry.GetHealthReportString());
                }

                Debug.WriteLine("[CommandHandlerBootstrapper] Command handler initialization complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandlerBootstrapper] Initialization failed: {ex.Message}");
                throw;
            }
        }

        private void InitializeFileOperationsHandler()
        {
            try
            {
                var projectRepo = GetService<IProjectRepository>();
                var dialogService = GetService<IDialogService>();
                var backendClient = TryGetService<IBackendClient>();
                var toastService = TryGetService<ToastNotificationService>();

                if (projectRepo != null && dialogService != null)
                {
                    _fileHandler = new FileOperationsHandler(_registry, projectRepo, dialogService, backendClient, toastService);
                    Debug.WriteLine("[CommandHandlerBootstrapper] FileOperationsHandler initialized");
                }
                else
                {
                    Debug.WriteLine("[CommandHandlerBootstrapper] FileOperationsHandler skipped: missing dependencies");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandlerBootstrapper] FileOperationsHandler failed: {ex.Message}");
            }
        }

        private void InitializeProfileOperationsHandler()
        {
            try
            {
                var profilesUseCase = GetService<IProfilesUseCase>();
                var dialogService = GetService<IDialogService>();
                var toastService = TryGetService<ToastNotificationService>();

                if (profilesUseCase != null && dialogService != null)
                {
                    _profileHandler = new ProfileOperationsHandler(_registry, profilesUseCase, dialogService, toastService);
                    Debug.WriteLine("[CommandHandlerBootstrapper] ProfileOperationsHandler initialized");
                }
                else
                {
                    Debug.WriteLine("[CommandHandlerBootstrapper] ProfileOperationsHandler skipped: missing dependencies");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandlerBootstrapper] ProfileOperationsHandler failed: {ex.Message}");
            }
        }

        private void InitializePlaybackOperationsHandler()
        {
            try
            {
                var audioPlayer = GetService<IAudioPlayerService>();
                var toastService = TryGetService<ToastNotificationService>();

                if (audioPlayer != null)
                {
                    _playbackHandler = new PlaybackOperationsHandler(_registry, audioPlayer, toastService);
                    Debug.WriteLine("[CommandHandlerBootstrapper] PlaybackOperationsHandler initialized");
                }
                else
                {
                    Debug.WriteLine("[CommandHandlerBootstrapper] PlaybackOperationsHandler skipped: missing dependencies");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandlerBootstrapper] PlaybackOperationsHandler failed: {ex.Message}");
            }
        }

        private void InitializeNavigationHandler()
        {
            try
            {
                var navigationService = GetService<INavigationService>();
                var toastService = TryGetService<ToastNotificationService>();

                if (navigationService != null)
                {
                    _navigationHandler = new NavigationHandler(_registry, navigationService, toastService);
                    Debug.WriteLine("[CommandHandlerBootstrapper] NavigationHandler initialized");
                }
                else
                {
                    Debug.WriteLine("[CommandHandlerBootstrapper] NavigationHandler skipped: missing dependencies");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandlerBootstrapper] NavigationHandler failed: {ex.Message}");
            }
        }

        private void InitializeSettingsOperationsHandler()
        {
            try
            {
                var settingsService = GetService<ISettingsService>();
                var dialogService = GetService<IDialogService>();
                var toastService = TryGetService<ToastNotificationService>();

                if (settingsService != null && dialogService != null)
                {
                    _settingsHandler = new SettingsOperationsHandler(_registry, settingsService, dialogService, toastService);
                    Debug.WriteLine("[CommandHandlerBootstrapper] SettingsOperationsHandler initialized");
                }
                else
                {
                    Debug.WriteLine("[CommandHandlerBootstrapper] SettingsOperationsHandler skipped: missing dependencies");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandlerBootstrapper] SettingsOperationsHandler failed: {ex.Message}");
            }
        }

        private T? GetService<T>() where T : class
        {
            return _serviceProvider.GetService(typeof(T)) as T;
        }

        private T? TryGetService<T>() where T : class
        {
            try
            {
                return _serviceProvider.GetService(typeof(T)) as T;
            }
            catch
            {
                return null;
            }
        }

        // Expose handlers for direct access if needed
        public FileOperationsHandler? FileHandler => _fileHandler;
        public ProfileOperationsHandler? ProfileHandler => _profileHandler;
        public PlaybackOperationsHandler? PlaybackHandler => _playbackHandler;
        public NavigationHandler? NavigationHandler => _navigationHandler;
        public SettingsOperationsHandler? SettingsHandler => _settingsHandler;
    }

    /// <summary>
    /// Adapter that implements IServiceProvider by delegating to AppServices static methods.
    /// </summary>
    internal sealed class AppServicesAdapter : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            // Map service types to AppServices methods
            if (serviceType == typeof(IProjectRepository))
                return AppServices.TryGetProjectRepository();
            if (serviceType == typeof(IDialogService))
                return AppServices.TryGetDialogService();
            if (serviceType == typeof(ToastNotificationService))
                return AppServices.TryGetToastNotificationService();
            if (serviceType == typeof(IProfilesUseCase))
                return AppServices.TryGetProfilesUseCase();
            if (serviceType == typeof(IAudioPlayerService))
                return AppServices.GetService<IAudioPlayerService>();
            if (serviceType == typeof(INavigationService))
                return AppServices.TryGetNavigationService();
            if (serviceType == typeof(ISettingsService))
                return AppServices.GetService<ISettingsService>();
            if (serviceType == typeof(IUnifiedCommandRegistry))
                return AppServices.TryGetCommandRegistry();

            // Generic fallback
            return AppServices.GetService<object>();
        }
    }
}
