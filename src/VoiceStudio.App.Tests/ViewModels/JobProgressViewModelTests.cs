using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for JobProgressViewModel.
    /// Tests initialization, progress updates, job state transitions, and cancellation.
    /// </summary>
    [TestClass]
    public class JobProgressViewModelTests : ViewModelTestBase
    {
        private Mock<IJobProgressApiClient>? _mockJobProgressApiClient;
        private JobProgressViewModel? _viewModel;
        private IViewModelContext? _testContext;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            TestAppServicesHelper.EnsureInitialized();
            _testContext = AppServices.GetService<IViewModelContext>()
                ?? throw new InvalidOperationException("IViewModelContext not registered by TestAppServicesHelper");
            _mockJobProgressApiClient = new Mock<IJobProgressApiClient>();

            // Default: return empty jobs and summary
            _mockJobProgressApiClient
                .Setup(x => x.GetJobsAsync(null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Job>());

            _mockJobProgressApiClient
                .Setup(x => x.GetJobSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JobSummary());

            _viewModel = new JobProgressViewModel(_testContext, _mockJobProgressApiClient.Object, null);
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _viewModel?.Dispose();
            _viewModel = null;
            _testContext = null;
            _mockJobProgressApiClient = null;
            base.TestCleanup();
        }

        #region Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act - ViewModel created in TestInitialize
            // Assert
            Assert.IsNotNull(_viewModel);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullJobProgressApiClient_ThrowsArgumentNullException()
        {
            // Arrange & Act
            _ = new JobProgressViewModel(_testContext!, null!);
        }

        [TestMethod]
        public void PanelId_ReturnsJobProgress()
        {
            Assert.AreEqual(PanelIds.JobProgress, _viewModel!.PanelId);
        }

        [TestMethod]
        public void DisplayName_ReturnsLocalizedString()
        {
            Assert.IsNotNull(_viewModel!.DisplayName);
            Assert.IsTrue(_viewModel.DisplayName.Length > 0);
        }

        [TestMethod]
        public void Initialization_SetsFilterDefaults()
        {
            Assert.IsNotNull(_viewModel!.AvailableJobTypes);
            Assert.IsTrue(_viewModel.AvailableJobTypes.Count > 0);
            Assert.IsNotNull(_viewModel.AvailableStatuses);
            Assert.IsTrue(_viewModel.AvailableStatuses.Count > 0);
            Assert.IsTrue(_viewModel.AutoRefresh);
        }

        [TestMethod]
        public void Jobs_InitiallyEmpty()
        {
            Assert.IsNotNull(_viewModel!.Jobs);
            Assert.AreEqual(0, _viewModel.Jobs.Count);
        }

        #endregion

        #region Command Tests

        [TestMethod]
        public void Commands_AreInitialized()
        {
            Assert.IsNotNull(_viewModel!.LoadJobsCommand);
            Assert.IsNotNull(_viewModel.RefreshCommand);
            Assert.IsNotNull(_viewModel.CancelJobCommand);
            Assert.IsNotNull(_viewModel.PauseJobCommand);
            Assert.IsNotNull(_viewModel.ResumeJobCommand);
            Assert.IsNotNull(_viewModel.DeleteJobCommand);
            Assert.IsNotNull(_viewModel.ClearCompletedCommand);
            Assert.IsNotNull(_viewModel.LoadSummaryCommand);
        }

        [TestMethod]
        public async Task LoadJobs_WhenBackendReturnsJobs_UpdatesJobsCollection()
        {
            // Arrange
            var backendJobs = new[]
            {
                new Job
                {
                    Id = "job-1",
                    Name = "Synthesis Job",
                    Type = "synthesis",
                    Status = "running",
                    Progress = 0.5,
                    Created = "2026-02-09T10:00:00Z"
                }
            };

            _mockJobProgressApiClient!
                .Setup(x => x.GetJobsAsync(null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(backendJobs);

            // Act - execute Refresh (which calls LoadJobs + LoadSummary)
            await _viewModel!.RefreshCommand.ExecuteAsync(null);
            await Task.Delay(150); // Allow async to complete

            // Assert
            Assert.AreEqual(1, _viewModel.Jobs.Count);
            Assert.AreEqual("job-1", _viewModel.Jobs[0].Id);
            Assert.AreEqual("Synthesis Job", _viewModel.Jobs[0].Name);
            Assert.AreEqual("synthesis", _viewModel.Jobs[0].Type);
            Assert.AreEqual("running", _viewModel.Jobs[0].Status);
        }

        [TestMethod]
        public async Task CancelJob_WhenCalled_InvokesBackendAndRefreshes()
        {
            // Arrange
            var jobItem = new JobProgressViewModel.JobItem(new Job
            {
                Id = "job-to-cancel",
                Name = "Cancel Me",
                Type = "synthesis",
                Status = "running",
                Progress = 0.3
            });
            _viewModel!.Jobs.Add(jobItem);

            _mockJobProgressApiClient!
                .Setup(x => x.CancelJobAsync("job-to-cancel", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            await _viewModel.CancelJobCommand.ExecuteAsync(jobItem);
            await Task.Delay(150);

            // Assert
            _mockJobProgressApiClient.Verify(
                x => x.CancelJobAsync("job-to-cancel", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public void CancelJobCommand_CanExecute_WhenJobProvidedAndNotLoading()
        {
            var job = new JobProgressViewModel.JobItem(new Job { Id = "j1" });
            Assert.IsTrue(_viewModel!.CancelJobCommand.CanExecute(job));
        }

        [TestMethod]
        public void CancelJobCommand_CannotExecute_WhenJobNull()
        {
            Assert.IsFalse(_viewModel!.CancelJobCommand.CanExecute(null));
        }

        [TestMethod]
        public void ImplementsIPanelView()
        {
            var panelView = _viewModel as IPanelView;
            Assert.IsNotNull(panelView);
            Assert.AreEqual(PanelIds.JobProgress, panelView.PanelId);
            Assert.AreEqual(PanelRegion.Right, panelView.Region);
        }

        #endregion

        #region Job State Transition Tests

        [TestMethod]
        public async Task LoadJobs_WhenBackendReturnsMultipleStatuses_PreservesStateInJobItems()
        {
            // Arrange - jobs with different states
            var backendJobs = new[]
            {
                new Job
                {
                    Id = "job-pending",
                    Name = "Pending Job",
                    Type = "batch",
                    Status = "pending",
                    Progress = 0.0,
                    Created = "2026-02-09T10:00:00Z"
                },
                new Job
                {
                    Id = "job-completed",
                    Name = "Completed Job",
                    Type = "synthesis",
                    Status = "completed",
                    Progress = 1.0,
                    ResultId = "result-123",
                    Created = "2026-02-09T09:00:00Z"
                },
                new Job
                {
                    Id = "job-failed",
                    Name = "Failed Job",
                    Type = "training",
                    Status = "failed",
                    Progress = 0.5,
                    ErrorMessage = "Out of memory",
                    Created = "2026-02-09T08:00:00Z"
                }
            };

            _mockJobProgressApiClient!
                .Setup(x => x.GetJobsAsync(null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(backendJobs);

            // Act
            await _viewModel!.RefreshCommand.ExecuteAsync(null);
            await Task.Delay(150);

            // Assert - verify job state transitions are reflected in JobItems
            Assert.AreEqual(3, _viewModel.Jobs.Count);
            Assert.AreEqual("pending", _viewModel.Jobs[0].Status);
            Assert.AreEqual("completed", _viewModel.Jobs[1].Status);
            Assert.AreEqual("result-123", _viewModel.Jobs[1].ResultId);
            Assert.AreEqual("failed", _viewModel.Jobs[2].Status);
            Assert.AreEqual("Out of memory", _viewModel.Jobs[2].ErrorMessage);
        }

        #endregion
    }
}
