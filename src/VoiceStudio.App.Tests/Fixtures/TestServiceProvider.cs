using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Fixtures
{
    /// <summary>
    /// Provides a pre-configured DI container for integration testing.
    /// Registers mock services and allows overriding specific services.
    /// </summary>
    public class TestServiceProvider : IDisposable
    {
        private readonly ServiceCollection _services;
        private Microsoft.Extensions.DependencyInjection.ServiceProvider? _provider;
        private bool _disposed;

        public TestServiceProvider()
        {
            _services = new ServiceCollection();
            RegisterDefaultServices();
        }

        /// <summary>
        /// Gets the built service provider. Builds on first access.
        /// </summary>
        public IServiceProvider Services => _provider ??= _services.BuildServiceProvider();

        /// <summary>
        /// Gets a service from the container.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <returns>The service instance.</returns>
        public T GetService<T>() where T : class
        {
            return Services.GetRequiredService<T>();
        }

        /// <summary>
        /// Gets a service from the container, or null if not registered.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <returns>The service instance or null.</returns>
        public T? GetOptionalService<T>() where T : class
        {
            return Services.GetService<T>();
        }

        /// <summary>
        /// Replaces a service registration with a mock.
        /// Must be called before accessing Services.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="mock">The mock to use.</param>
        /// <returns>This instance for chaining.</returns>
        public TestServiceProvider WithMock<T>(Mock<T> mock) where T : class
        {
            EnsureNotBuilt();
            RemoveService<T>();
            _services.AddSingleton(mock.Object);
            return this;
        }

        /// <summary>
        /// Replaces a service registration with an instance.
        /// Must be called before accessing Services.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="instance">The instance to use.</param>
        /// <returns>This instance for chaining.</returns>
        public TestServiceProvider WithService<T>(T instance) where T : class
        {
            EnsureNotBuilt();
            RemoveService<T>();
            _services.AddSingleton(instance);
            return this;
        }

        /// <summary>
        /// Adds a transient service registration.
        /// Must be called before accessing Services.
        /// </summary>
        /// <typeparam name="TService">The service type.</typeparam>
        /// <typeparam name="TImplementation">The implementation type.</typeparam>
        /// <returns>This instance for chaining.</returns>
        public TestServiceProvider AddTransient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            EnsureNotBuilt();
            _services.AddTransient<TService, TImplementation>();
            return this;
        }

        private void RegisterDefaultServices()
        {
            // Core infrastructure
            _services.AddSingleton<IViewModelContext, MockViewModelContext>();
            _services.AddSingleton<ILogger>(NullLogger.Instance);
            _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            // Mock services
            _services.AddSingleton<ISettingsService, MockSettingsService>();
            _services.AddSingleton<IAnalyticsService, MockAnalyticsService>();
            _services.AddSingleton<INavigationService, MockNavigationService>();

            // Backend client (mock)
            _services.AddSingleton<IBackendClient>(sp =>
            {
                var mock = new Mock<IBackendClient>();
                ConfigureDefaultBackendClientMock(mock);
                return mock.Object;
            });
        }

        private static void ConfigureDefaultBackendClientMock(Mock<IBackendClient> mock)
        {
            // IBackendClient no longer has IsConnected (PR-8: extracted to IConnectionStatusClient)
        }

        private void RemoveService<T>() where T : class
        {
            var descriptor = _services.FirstOrDefault(d => d.ServiceType == typeof(T));
            if (descriptor != null)
            {
                _services.Remove(descriptor);
            }
        }

        private void EnsureNotBuilt()
        {
            if (_provider != null)
            {
                throw new InvalidOperationException(
                    "Cannot modify services after the provider has been built. " +
                    "Call WithMock/WithService before accessing Services.");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _provider?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Extension methods for TestServiceProvider.
    /// </summary>
    public static class TestServiceProviderExtensions
    {
        /// <summary>
        /// Creates a TestServiceProvider with common ViewModel test configuration.
        /// </summary>
        public static TestServiceProvider ForViewModelTests()
        {
            return new TestServiceProvider();
        }

        /// <summary>
        /// Creates a TestServiceProvider with common Service test configuration.
        /// </summary>
        public static TestServiceProvider ForServiceTests()
        {
            return new TestServiceProvider();
        }
    }
}
