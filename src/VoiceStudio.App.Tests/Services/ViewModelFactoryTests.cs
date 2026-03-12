using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services
{
    [TestClass]
    public class ViewModelFactoryTests
    {
        private interface IDummyService
        {
            string Id { get; }
        }

        private sealed class DummyService : IDummyService
        {
            public string Id { get; } = Guid.NewGuid().ToString("N");
        }

        private sealed class DummyViewModel
        {
            public IDummyService Service { get; }

            public DummyViewModel(IDummyService service)
            {
                Service = service ?? throw new ArgumentNullException(nameof(service));
            }
        }

        private static ViewModelFactory CreateFactory(Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            configure?.Invoke(services);
            var provider = services.BuildServiceProvider();
            return new ViewModelFactory(provider);
        }

        [TestMethod]
        public void Create_UnregisteredViewModel_WithRegisteredDependencies_Succeeds()
        {
            var factory = CreateFactory(s =>
            {
                s.AddSingleton<IDummyService, DummyService>();
            });

            var result = factory.Create(typeof(DummyViewModel));

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(DummyViewModel));
            var vm = (DummyViewModel)result;
            Assert.IsNotNull(vm.Service);
            Assert.IsInstanceOfType(vm.Service, typeof(DummyService));
        }

        [TestMethod]
        public void Create_RegisteredViewModel_ReturnsRegisteredInstance()
        {
            var factory = CreateFactory(s =>
            {
                s.AddSingleton<IDummyService, DummyService>();
                s.AddSingleton<DummyViewModel>();
            });

            var first = factory.Create(typeof(DummyViewModel));
            var second = factory.Create(typeof(DummyViewModel));

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public void Create_Generic_UnregisteredViewModel_Succeeds()
        {
            var factory = CreateFactory(s =>
            {
                s.AddSingleton<IDummyService, DummyService>();
            });

            var result = factory.Create<DummyViewModel>();

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Service);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Create_NullType_ThrowsArgumentNullException()
        {
            var factory = CreateFactory();
            factory.Create(null!);
        }
    }
}
