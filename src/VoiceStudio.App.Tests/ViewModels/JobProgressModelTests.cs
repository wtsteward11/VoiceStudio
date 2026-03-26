using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class JobProgressModelTests
    {
        #region Job Model Tests

        [TestMethod]
        public void Job_DefaultValues()
        {
            var job = new Job();

            Assert.AreEqual(string.Empty, job.Id);
            Assert.AreEqual(string.Empty, job.Name);
            Assert.AreEqual(string.Empty, job.Type);
            Assert.AreEqual(string.Empty, job.Status);
            Assert.AreEqual(0.0, job.Progress);
            Assert.IsNull(job.CurrentStep);
            Assert.IsNull(job.TotalSteps);
            Assert.IsNull(job.CurrentStepIndex);
            Assert.AreEqual(string.Empty, job.Created);
            Assert.IsNull(job.Started);
            Assert.IsNull(job.Completed);
            Assert.IsNull(job.EstimatedTimeRemaining);
            Assert.IsNull(job.ErrorMessage);
            Assert.IsNull(job.ResultId);
            Assert.IsNotNull(job.Metadata);
            Assert.AreEqual(0, job.Metadata.Count);
        }

        [TestMethod]
        public void Job_PropertiesSetCorrectly()
        {
            var job = new Job
            {
                Id = "job123",
                Name = "Synthesis Job",
                Type = "tts",
                Status = "running",
                Progress = 0.5,
                CurrentStep = "Processing audio",
                TotalSteps = 5,
                CurrentStepIndex = 2,
                Created = "2026-01-01T00:00:00Z",
                Started = "2026-01-01T00:01:00Z",
                Completed = null,
                EstimatedTimeRemaining = 120,
                ErrorMessage = null,
                ResultId = null,
                Metadata = new Dictionary<string, object> { { "engine", "xtts" } }
            };

            Assert.AreEqual("job123", job.Id);
            Assert.AreEqual("Synthesis Job", job.Name);
            Assert.AreEqual("tts", job.Type);
            Assert.AreEqual("running", job.Status);
            Assert.AreEqual(0.5, job.Progress);
            Assert.AreEqual("Processing audio", job.CurrentStep);
            Assert.AreEqual(5, job.TotalSteps);
            Assert.AreEqual(2, job.CurrentStepIndex);
            Assert.AreEqual("2026-01-01T00:00:00Z", job.Created);
            Assert.AreEqual("2026-01-01T00:01:00Z", job.Started);
            Assert.IsNull(job.Completed);
            Assert.AreEqual(120, job.EstimatedTimeRemaining);
            Assert.IsNull(job.ErrorMessage);
            Assert.IsNull(job.ResultId);
            Assert.AreEqual(1, job.Metadata.Count);
        }

        #endregion

        #region JobSummary Model Tests

        [TestMethod]
        public void JobSummary_DefaultValues()
        {
            var summary = new JobSummary();

            Assert.AreEqual(0, summary.Total);
            Assert.AreEqual(0, summary.Pending);
            Assert.AreEqual(0, summary.Running);
            Assert.AreEqual(0, summary.Completed);
            Assert.AreEqual(0, summary.Failed);
            Assert.AreEqual(0, summary.Cancelled);
            Assert.IsNotNull(summary.ByType);
            Assert.AreEqual(0, summary.ByType.Count);
        }

        [TestMethod]
        public void JobSummary_PropertiesSetCorrectly()
        {
            var summary = new JobSummary
            {
                Total = 100,
                Pending = 10,
                Running = 5,
                Completed = 80,
                Failed = 3,
                Cancelled = 2,
                ByType = new Dictionary<string, int>
                {
                    { "tts", 60 },
                    { "stt", 30 },
                    { "clone", 10 }
                }
            };

            Assert.AreEqual(100, summary.Total);
            Assert.AreEqual(10, summary.Pending);
            Assert.AreEqual(5, summary.Running);
            Assert.AreEqual(80, summary.Completed);
            Assert.AreEqual(3, summary.Failed);
            Assert.AreEqual(2, summary.Cancelled);
            Assert.AreEqual(3, summary.ByType.Count);
        }

        #endregion

        #region JobItem Model Tests

        [TestMethod]
        public void JobItem_CreatedFromJob()
        {
            var job = new Job
            {
                Id = "j1",
                Name = "Test Job",
                Type = "synthesis",
                Status = "running",
                Progress = 0.75,
                CurrentStep = "Generating",
                TotalSteps = 4,
                CurrentStepIndex = 2,
                Created = "2026-01-01",
                Started = "2026-01-01T00:01:00Z",
                Completed = null,
                EstimatedTimeRemaining = 90,
                ErrorMessage = null,
                ResultId = "result123"
            };

            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("j1", item.Id);
            Assert.AreEqual("Test Job", item.Name);
            Assert.AreEqual("synthesis", item.Type);
            Assert.AreEqual("running", item.Status);
            Assert.AreEqual(0.75, item.Progress);
            Assert.AreEqual("Generating", item.CurrentStep);
            Assert.AreEqual("2026-01-01", item.Created);
            Assert.AreEqual("2026-01-01T00:01:00Z", item.Started);
            Assert.IsNull(item.Completed);
            Assert.AreEqual("result123", item.ResultId);
            Assert.IsNull(item.ErrorMessage);
        }

        [TestMethod]
        public void JobItem_ProgressDisplay_FormatsAsPercent()
        {
            var job = new Job { Progress = 0.5 };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("50.0%", item.ProgressDisplay);
        }

        [TestMethod]
        public void JobItem_ProgressDisplay_ZeroPercent()
        {
            var job = new Job { Progress = 0.0 };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("0.0%", item.ProgressDisplay);
        }

        [TestMethod]
        public void JobItem_ProgressDisplay_HundredPercent()
        {
            var job = new Job { Progress = 1.0 };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("100.0%", item.ProgressDisplay);
        }

        [TestMethod]
        public void JobItem_StepDisplay_WithStepInfo()
        {
            var job = new Job
            {
                TotalSteps = 5,
                CurrentStepIndex = 2
            };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("3/5", item.StepDisplay);
        }

        [TestMethod]
        public void JobItem_StepDisplay_NullWhenNoStepInfo()
        {
            var job = new Job
            {
                TotalSteps = null,
                CurrentStepIndex = null
            };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.IsNull(item.StepDisplay);
        }

        [TestMethod]
        public void JobItem_EstimatedTimeRemaining_Seconds()
        {
            var job = new Job { EstimatedTimeRemaining = 45 };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("45s", item.EstimatedTimeRemaining);
        }

        [TestMethod]
        public void JobItem_EstimatedTimeRemaining_Minutes()
        {
            var job = new Job { EstimatedTimeRemaining = 150 };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("2m 30s", item.EstimatedTimeRemaining);
        }

        [TestMethod]
        public void JobItem_EstimatedTimeRemaining_Hours()
        {
            var job = new Job { EstimatedTimeRemaining = 3750 };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("1h 2m", item.EstimatedTimeRemaining);
        }

        [TestMethod]
        public void JobItem_EstimatedTimeRemaining_NullWhenNoEstimate()
        {
            var job = new Job { EstimatedTimeRemaining = null };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.IsNull(item.EstimatedTimeRemaining);
        }

        [TestMethod]
        public void JobItem_ErrorMessage_FromJob()
        {
            var job = new Job { ErrorMessage = "Connection failed" };
            var item = new JobProgressViewModel.JobItem(job);

            Assert.AreEqual("Connection failed", item.ErrorMessage);
        }

        #endregion
    }
}
