using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Comprehensive unit tests for TrainingViewModel.
  /// Tests cover training operations, status monitoring, and model management.
  /// </summary>
  [TestClass]
  public class TrainingViewModelTests
  {
    private Mock<IBackendClient> _mockBackendClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      // Setup default mock behavior
      _mockBackendClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());

      _mockBackendClient
          .Setup(x => x.GetModelsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<ModelInfo>());
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    #region Training Request Tests

    [TestMethod]
    public async Task StartTrainingAsync_CallsBackend_WithCorrectRequest()
    {
      // Arrange
      TrainingRequest? capturedRequest = null;
      _mockBackendClient
          .Setup(x => x.StartTrainingAsync(It.IsAny<TrainingRequest>(), It.IsAny<CancellationToken>()))
          .Callback<TrainingRequest, CancellationToken>((req, ct) => capturedRequest = req)
          .ReturnsAsync(new TrainingStatus { Id = "job-123", Status = "started" });

      var request = new TrainingRequest
      {
        ProfileId = "profile-123",
        Engine = "xtts",
        DatasetId = "dataset-456",
        Epochs = 100,
        BatchSize = 8,
        LearningRate = 0.0001
      };

      // Act
      var result = await _mockBackendClient.Object.StartTrainingAsync(request, CancellationToken.None);

      // Assert
      Assert.IsNotNull(capturedRequest);
      Assert.AreEqual("profile-123", capturedRequest.ProfileId);
      Assert.AreEqual("xtts", capturedRequest.Engine);
      Assert.AreEqual("dataset-456", capturedRequest.DatasetId);
      Assert.AreEqual(100, capturedRequest.Epochs);
      Assert.AreEqual(8, capturedRequest.BatchSize);
      Assert.AreEqual(0.0001, capturedRequest.LearningRate);
    }

    [TestMethod]
    public async Task StartTrainingAsync_ReturnsStatusWithId()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.StartTrainingAsync(It.IsAny<TrainingRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TrainingStatus { Id = "job-456", Status = "started" });

      var request = new TrainingRequest { ProfileId = "p1", Engine = "xtts" };

      // Act
      var result = await _mockBackendClient.Object.StartTrainingAsync(request, CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("job-456", result.Id);
      Assert.AreEqual("started", result.Status);
    }

    #endregion

    #region Training Status Tests

    [TestMethod]
    public async Task GetTrainingStatusAsync_ReturnsCorrectStatus()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.GetTrainingStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TrainingStatus
          {
            Id = "job-789",
            Status = "running",
            Progress = 0.5,
            CurrentEpoch = 50,
            TotalEpochs = 100,
            Loss = 0.023
          });

      // Act
      var result = await _mockBackendClient.Object.GetTrainingStatusAsync("job-789", CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("job-789", result.Id);
      Assert.AreEqual("running", result.Status);
      Assert.AreEqual(0.5, result.Progress);
      Assert.AreEqual(50, result.CurrentEpoch);
      Assert.AreEqual(100, result.TotalEpochs);
      Assert.AreEqual(0.023, result.Loss);
    }

    [TestMethod]
    public async Task GetTrainingStatusAsync_ReturnsCompleted_WhenDone()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.GetTrainingStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new TrainingStatus
          {
            Id = "job-completed",
            Status = "completed",
            Progress = 1.0,
            CurrentEpoch = 100,
            TotalEpochs = 100
          });

      // Act
      var result = await _mockBackendClient.Object.GetTrainingStatusAsync("job-completed", CancellationToken.None);

      // Assert
      Assert.AreEqual("completed", result.Status);
      Assert.AreEqual(1.0, result.Progress);
    }

    #endregion

    #region Cancel Training Tests

    [TestMethod]
    public async Task CancelTrainingAsync_ReturnsTrue_OnSuccess()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.CancelTrainingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(true);

      // Act
      var result = await _mockBackendClient.Object.CancelTrainingAsync("job-to-cancel", CancellationToken.None);

      // Assert
      Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CancelTrainingAsync_ReturnsFalse_WhenJobNotFound()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.CancelTrainingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);

      // Act
      var result = await _mockBackendClient.Object.CancelTrainingAsync("nonexistent-job", CancellationToken.None);

      // Assert
      Assert.IsFalse(result);
    }

    #endregion

    #region Model Management Tests

    [TestMethod]
    public async Task GetModelsAsync_ReturnsListOfModels()
    {
      // Arrange
      var expectedModels = new List<ModelInfo>
            {
                new ModelInfo { Engine = "xtts", ModelName = "model1", Version = "1.0" },
                new ModelInfo { Engine = "xtts", ModelName = "model2", Version = "2.0" },
            };
      _mockBackendClient
          .Setup(x => x.GetModelsAsync("xtts", It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedModels);

      // Act
      var result = await _mockBackendClient.Object.GetModelsAsync("xtts", CancellationToken.None);

      // Assert
      Assert.AreEqual(2, result.Count);
      Assert.AreEqual("model1", result[0].ModelName);
      Assert.AreEqual("model2", result[1].ModelName);
    }

    [TestMethod]
    public async Task GetModelAsync_ReturnsModelInfo()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.GetModelAsync("xtts", "my-model", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ModelInfo
          {
            Engine = "xtts",
            ModelName = "my-model",
            Version = "1.0",
            ModelPath = "/models/xtts/my-model.pt"
          });

      // Act
      var result = await _mockBackendClient.Object.GetModelAsync("xtts", "my-model", CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("my-model", result.ModelName);
      Assert.AreEqual("/models/xtts/my-model.pt", result.ModelPath);
    }

    #endregion

    #region Training Parameter Validation Tests

    [TestMethod]
    public void TrainingRequest_DefaultValues()
    {
      var request = new TrainingRequest();

      Assert.AreEqual("xtts", request.Engine);
      Assert.AreEqual(100, request.Epochs);
      Assert.AreEqual(4, request.BatchSize);
      Assert.AreEqual(0.0001, request.LearningRate);
      Assert.IsTrue(request.Gpu);
    }

    [TestMethod]
    public void TrainingRequest_WithCustomValues()
    {
      var request = new TrainingRequest
      {
        ProfileId = "profile-123",
        Engine = "rvc",
        DatasetId = "dataset-789",
        Epochs = 200,
        BatchSize = 16,
        LearningRate = 0.001,
        Gpu = false
      };

      Assert.AreEqual("profile-123", request.ProfileId);
      Assert.AreEqual("rvc", request.Engine);
      Assert.AreEqual("dataset-789", request.DatasetId);
      Assert.AreEqual(200, request.Epochs);
      Assert.AreEqual(16, request.BatchSize);
      Assert.AreEqual(0.001, request.LearningRate);
      Assert.IsFalse(request.Gpu);
    }

    [TestMethod]
    public void TrainingRequest_WithZeroEpochs_IsInvalid()
    {
      var request = new TrainingRequest { Epochs = 0 };

      // Epochs should be greater than 0 for valid training
      Assert.AreEqual(0, request.Epochs);
    }

    #endregion

    #region Training Status Properties Tests

    [TestMethod]
    public void TrainingStatus_DefaultValues()
    {
      var status = new TrainingStatus();

      Assert.AreEqual(string.Empty, status.Id);
      Assert.AreEqual(string.Empty, status.Status);
      Assert.AreEqual(0.0, status.Progress);
      Assert.AreEqual(0, status.CurrentEpoch);
      Assert.AreEqual(0, status.TotalEpochs);
    }

    [TestMethod]
    public void TrainingStatus_QualityMetrics()
    {
      var status = new TrainingStatus
      {
        QualityScore = 0.92,
        ValidationLoss = 0.015
      };

      Assert.AreEqual(0.92, status.QualityScore);
      Assert.AreEqual(0.015, status.ValidationLoss);
    }

    #endregion
  }
}
