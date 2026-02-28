using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Commands;
using VoiceStudio.Core.Modules;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Orchestrates loading, initialization, and shutdown of UI modules.
  /// Modules are registered at startup and initialized in priority order.
  /// </summary>
  public class ModuleLoader
  {
    private readonly List<IUIModule> _modules = new();
    private bool _initialized;
    private IServiceProvider? _provider;

    /// <summary>
    /// Gets the list of registered modules (read-only).
    /// </summary>
    public IReadOnlyList<IUIModule> Modules => _modules.AsReadOnly();

    /// <summary>
    /// Registers a module type. The module is instantiated immediately.
    /// Call this before ConfigureServices.
    /// </summary>
    /// <typeparam name="T">The module type implementing IUIModule.</typeparam>
    public void RegisterModule<T>() where T : IUIModule, new()
    {
      if (_initialized)
      {
        throw new InvalidOperationException("Cannot register modules after initialization.");
      }

      var module = new T();

      // Check for duplicate module IDs
      if (_modules.Any(m => m.ModuleId == module.ModuleId))
      {
        throw new InvalidOperationException($"Module with ID '{module.ModuleId}' is already registered.");
      }

      _modules.Add(module);
    }

    /// <summary>
    /// Registers a module instance. Useful for modules that require constructor parameters.
    /// Call this before ConfigureServices.
    /// </summary>
    /// <param name="module">The module instance to register.</param>
    public void RegisterModule(IUIModule module)
    {
      if (_initialized)
      {
        throw new InvalidOperationException("Cannot register modules after initialization.");
      }

      if (module == null)
      {
        throw new ArgumentNullException(nameof(module));
      }

      if (_modules.Any(m => m.ModuleId == module.ModuleId))
      {
        throw new InvalidOperationException($"Module with ID '{module.ModuleId}' is already registered.");
      }

      _modules.Add(module);
    }

    /// <summary>
    /// Configures services for all registered modules.
    /// Modules are processed in priority order (lower priority values first).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public void ConfigureServices(IServiceCollection services)
    {
      // Sort by priority before registration
      var sortedModules = _modules.OrderBy(m => m.Priority).ToList();
      _modules.Clear();
      _modules.AddRange(sortedModules);

      foreach (var module in _modules)
      {
        try
        {
          module.RegisterServices(services);
        }
        catch (Exception ex)
        {
          throw new InvalidOperationException(
              $"Failed to register services for module '{module.ModuleId}': {ex.Message}", ex);
        }
      }

      // Register the ModuleLoader itself so modules can access it
      services.AddSingleton(this);
    }

    /// <summary>
    /// Initializes all registered modules after the service provider is built.
    /// Modules are initialized in priority order.
    /// </summary>
    /// <param name="provider">The built service provider.</param>
    public void InitializeAll(IServiceProvider provider)
    {
      if (_initialized)
      {
        throw new InvalidOperationException("Modules have already been initialized.");
      }

      _provider = provider;

      foreach (var module in _modules)
      {
        try
        {
          module.OnInitialized(provider);
        }
        catch (Exception ex)
        {
          throw new InvalidOperationException(
              $"Failed to initialize module '{module.ModuleId}': {ex.Message}", ex);
        }
      }

      _initialized = true;
    }

    /// <summary>
    /// Shuts down all modules in reverse priority order.
    /// Called on application exit.
    /// </summary>
    public void ShutdownAll()
    {
      // Shutdown in reverse order
      foreach (var module in _modules.AsEnumerable().Reverse())
      {
        try
        {
          module.OnShutdown();
        }
        catch (Exception ex)
        {
          // Log shutdown errors but don't throw - allow other modules to shutdown
          System.Diagnostics.Debug.WriteLine($"[ModuleLoader] Error during module shutdown '{module.ModuleId}': {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Gets a module by its ID.
    /// </summary>
    /// <param name="moduleId">The module identifier.</param>
    /// <returns>The module, or null if not found.</returns>
    public IUIModule? GetModule(string moduleId)
    {
      return _modules.FirstOrDefault(m => m.ModuleId == moduleId);
    }

    /// <summary>
    /// Gets a module by its type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    /// <returns>The module, or null if not found.</returns>
    public T? GetModule<T>() where T : class, IUIModule
    {
      return _modules.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Gets all commands from all registered modules.
    /// </summary>
    /// <returns>Aggregated collection of all module commands.</returns>
    public IEnumerable<CommandDescriptor> GetAllCommands()
    {
      return _modules.SelectMany(m => m.GetCommands());
    }

    /// <summary>
    /// Gets all resource dictionary URIs from all registered modules.
    /// </summary>
    /// <returns>Aggregated collection of all module resource dictionary URIs.</returns>
    public IEnumerable<string> GetAllResourceDictionaryUris()
    {
      return _modules.SelectMany(m => m.GetResourceDictionaryUris());
    }

    /// <summary>
    /// Gets the service provider used during initialization.
    /// </summary>
    public IServiceProvider? ServiceProvider => _provider;

    /// <summary>
    /// Returns true if modules have been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Merges resource dictionaries from all registered modules into the application resources.
    /// Should be called after modules are initialized, typically in App.OnLaunched.
    /// </summary>
    /// <param name="appResources">The application's ResourceDictionary (Application.Current.Resources).</param>
    /// <returns>Number of resource dictionaries merged.</returns>
    public int MergeModuleResources(ResourceDictionary appResources)
    {
      if (appResources == null)
      {
        throw new ArgumentNullException(nameof(appResources));
      }

      var count = 0;
      foreach (var uri in GetAllResourceDictionaryUris())
      {
        try
        {
          var dict = new ResourceDictionary
          {
            Source = new Uri(uri)
          };
          appResources.MergedDictionaries.Add(dict);
          count++;
        }
        catch (Exception ex)
        {
          // Log but don't fail - module resources are optional
          System.Diagnostics.Debug.WriteLine($"[ModuleLoader] Failed to merge resource dictionary '{uri}': {ex.Message}");
        }
      }

      return count;
    }
  }
}