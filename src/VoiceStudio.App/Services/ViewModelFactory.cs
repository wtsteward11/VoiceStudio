using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Factory for creating ViewModel instances with dependency injection.
  /// Implements ViewModel-first instantiation pattern.
  /// </summary>
  public class ViewModelFactory : IViewModelFactory
  {
    private readonly IServiceProvider _serviceProvider;

    public ViewModelFactory(IServiceProvider serviceProvider)
    {
      _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public TViewModel Create<TViewModel>() where TViewModel : class
    {
      var existing = _serviceProvider.GetService<TViewModel>();
      if (existing != null)
        return existing;
      try
      {
        return ActivatorUtilities.CreateInstance<TViewModel>(_serviceProvider);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[ViewModelFactory] Create failed for VM type {typeof(TViewModel).FullName}: {ex.Message}");
        throw;
      }
    }

    /// <inheritdoc />
    public object Create(Type viewModelType)
    {
      if (viewModelType == null)
      {
        throw new ArgumentNullException(nameof(viewModelType));
      }

      var existing = _serviceProvider.GetService(viewModelType);
      if (existing != null)
        return existing;
      try
      {
        return ActivatorUtilities.CreateInstance(_serviceProvider, viewModelType);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[ViewModelFactory] Create failed for VM type {viewModelType.FullName}: {ex.Message}");
        throw;
      }
    }

    /// <inheritdoc />
    public TViewModel CreateWithParameters<TViewModel>(params object[] parameters) where TViewModel : class
    {
      // Use ActivatorUtilities to create with additional parameters
      // This allows mixing DI-resolved services with runtime parameters
      return ActivatorUtilities.CreateInstance<TViewModel>(_serviceProvider, parameters);
    }
  }

  /// <summary>
  /// Extension methods for registering ViewModels with the DI container.
  /// </summary>
  public static class ViewModelRegistrationExtensions
  {
    /// <summary>
    /// Registers a ViewModel type with the service collection.
    /// ViewModels are registered as transient by default.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddViewModel<TViewModel>(this IServiceCollection services)
        where TViewModel : class
    {
      services.AddTransient<TViewModel>();
      return services;
    }

    /// <summary>
    /// Registers a ViewModel type with a factory function.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">The factory function.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddViewModel<TViewModel>(
        this IServiceCollection services,
        Func<IServiceProvider, TViewModel> factory)
        where TViewModel : class
    {
      services.AddTransient(factory);
      return services;
    }
  }
}