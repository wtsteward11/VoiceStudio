using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for WorkflowAutomationViewModel.
  /// Instantiates ViewModel with mocked IWorkflowAutomationClient.
  /// Supports "WorkflowAutomationViewModel migrated to IWorkflowAutomationClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class WorkflowAutomationViewModelSeamTests
  {
    private Mock<IWorkflowAutomationClient> _mockClient = null!;

    [TestInitialize]
    public void TestInitialize()
    {
      _mockClient = new Mock<IWorkflowAutomationClient>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new WorkflowAutomationViewModel(_mockClient.Object);
      _mockClient.Verify(x => x.CreateWorkflowAsync(It.IsAny<WorkflowCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.UpdateWorkflowAsync(It.IsAny<string>(), It.IsAny<WorkflowUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.ExecuteWorkflowAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new WorkflowAutomationViewModel(_mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.CreateWorkflowCommand);
      Assert.IsNotNull(vm.SaveWorkflowCommand);
      Assert.IsNotNull(vm.TestWorkflowCommand);
      Assert.IsNotNull(vm.RunWorkflowCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new WorkflowAutomationViewModel(null!);
    }
  }
}
