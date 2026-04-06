using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using CoreSettingsData = VoiceStudio.Core.Models.SettingsData;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  [TestClass]
  public class SettingsViewModelEnginePriorityTests
  {
    private Mock<ISettingsService> _mockSettingsService = null!;
    private Mock<ISettingsClient> _mockSettingsClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;
    private SettingsViewModel _vm = null!;

    [TestInitialize]
    public void Setup()
    {
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      _context = new ViewModelContext(NullLogger.Instance, _dispatcherController.DispatcherQueue);
      _mockSettingsService = new Mock<ISettingsService>();
      _mockSettingsClient = new Mock<ISettingsClient>();
      _mockSettingsClient
          .Setup(x => x.GetEffectiveEnginePriorityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EffectiveEnginePriorityResponse
          {
            Source = "default",
            Order = new List<string> { "xtts_v2", "openvoice", "piper", "espeak" }
          });
      _mockSettingsClient
          .Setup(x => x.GetTorchVenvStatusAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync((TorchVenvStatusResponse?)null);
      _vm = new SettingsViewModel(_context, _mockSettingsService.Object, _mockSettingsClient.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestMethod]
    public void MoveUp_SwapsWithPrevious()
    {
      _vm.EnginePriorityOrder.Clear();
      _vm.EnginePriorityOrder.Add("a");
      _vm.EnginePriorityOrder.Add("b");
      _vm.SelectedEnginePriorityItem = "b";
      _vm.MoveEnginePriorityUpCommand.Execute(null);
      CollectionAssert.AreEqual(new[] { "b", "a" }, _vm.EnginePriorityOrder.ToArray());
    }

    [TestMethod]
    public void MoveDown_SwapsWithNext()
    {
      _vm.EnginePriorityOrder.Clear();
      _vm.EnginePriorityOrder.Add("a");
      _vm.EnginePriorityOrder.Add("b");
      _vm.SelectedEnginePriorityItem = "a";
      _vm.MoveEnginePriorityDownCommand.Execute(null);
      CollectionAssert.AreEqual(new[] { "b", "a" }, _vm.EnginePriorityOrder.ToArray());
    }

    [TestMethod]
    public void MoveUp_AtTop_NoOp()
    {
      _vm.EnginePriorityOrder.Clear();
      _vm.EnginePriorityOrder.Add("only");
      _vm.SelectedEnginePriorityItem = "only";
      _vm.MoveEnginePriorityUpCommand.Execute(null);
      Assert.AreEqual(1, _vm.EnginePriorityOrder.Count);
      Assert.AreEqual("only", _vm.EnginePriorityOrder[0]);
    }

    [TestMethod]
    public void MoveDown_AtBottom_NoOp()
    {
      _vm.EnginePriorityOrder.Clear();
      _vm.EnginePriorityOrder.Add("only");
      _vm.SelectedEnginePriorityItem = "only";
      _vm.MoveEnginePriorityDownCommand.Execute(null);
      Assert.AreEqual(1, _vm.EnginePriorityOrder.Count);
    }

    [TestMethod]
    public async Task Reset_RestoresDefaultOrder_FromEffectiveEndpointAsync()
    {
      _mockSettingsClient
          .Setup(x => x.GetEffectiveEnginePriorityAsync("tts", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EffectiveEnginePriorityResponse
          {
            Source = "yaml",
            Order = new List<string> { "piper", "xtts_v2" }
          });

      _vm.EnginePriorityOrder.Clear();
      _vm.EnginePriorityOrder.Add("z");
      var cmd = (IAsyncRelayCommand)_vm.ResetEnginePriorityOrderCommand;
      await cmd.ExecuteAsync(default);
      CollectionAssert.AreEqual(new[] { "piper", "xtts_v2" }, _vm.EnginePriorityOrder.ToArray());
      Assert.AreEqual("Config file", _vm.EnginePrioritySourceLabel);
    }

    [TestMethod]
    public void Save_IncludesPriorityOrder_WhenCustom()
    {
      _vm.EnginePriorityOrder.Clear();
      _vm.EnginePriorityOrder.Add("a");
      _vm.EnginePriorityOrder.Add("b");
      var hydrate = _vm.GetType().GetMethod(
          "HydrateEnginePriorityDisplayAsync",
          BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.IsNotNull(hydrate);
      var settings = new CoreSettingsData
      {
        Engine = new VoiceStudio.Core.Models.EngineSettings
        {
          EnginePriorityOrder = new List<string> { "a", "b" }
        }
      };
      ((Task)hydrate!.Invoke(_vm, new object[] { settings, CancellationToken.None })!).GetAwaiter().GetResult();

      var getData = _vm.GetType().GetMethod(
          "GetSettingsData",
          BindingFlags.Instance | BindingFlags.NonPublic);
      var data = (CoreSettingsData)getData!.Invoke(_vm, Array.Empty<object>())!;
      Assert.IsNotNull(data.Engine);
      CollectionAssert.AreEqual(new[] { "a", "b" }, data.Engine!.EnginePriorityOrder!.ToArray());
    }

    [TestMethod]
    public async Task Load_PopulatesFromSettings_PriorityListAsync()
    {
      _mockSettingsService
          .Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CoreSettingsData
          {
            General = new VoiceStudio.Core.Models.GeneralSettings(),
            Engine = new VoiceStudio.Core.Models.EngineSettings
            {
              DefaultAudioEngine = "xtts",
              EnginePriorityOrder = new List<string> { "piper", "espeak" }
            }
          });

      var cmd = (IAsyncRelayCommand)_vm.LoadSettingsCommand;
      await cmd.ExecuteAsync(default);
      CollectionAssert.AreEqual(new[] { "piper", "espeak" }, _vm.EnginePriorityOrder.ToArray());
      Assert.AreEqual("Custom", _vm.EnginePrioritySourceLabel);
    }
  }
}
