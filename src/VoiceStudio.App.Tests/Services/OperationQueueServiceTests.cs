using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services
{
  [TestClass]
  public class OperationQueueServiceTests
  {
    [TestMethod]
    public async Task QueueOperation_WithPriority_SortsCorrectly()
    {
      // Arrange
      var service = new OperationQueueService();
      var executionOrder = new System.Collections.Generic.List<string>();

      // Act
      // Queue low priority first
      service.QueueOperation("Low", async (ct) => { executionOrder.Add("Low"); await Task.CompletedTask; }, priority: OperationPriority.Low);

      // Queue high priority second
      service.QueueOperation("High", async (ct) => { executionOrder.Add("High"); await Task.CompletedTask; }, priority: OperationPriority.High);

      // Queue normal priority third
      service.QueueOperation("Normal", async (ct) => { executionOrder.Add("Normal"); await Task.CompletedTask; }, priority: OperationPriority.Normal);

      await service.ProcessQueueAsync();

      // Assert
      Assert.AreEqual(3, executionOrder.Count);
      Assert.AreEqual("High", executionOrder[0]);
      Assert.AreEqual("Normal", executionOrder[1]);
      Assert.AreEqual("Low", executionOrder[2]);
    }

    [TestMethod]
    public async Task CancelOperation_PreventsExecution()
    {
      // Arrange
      var service = new OperationQueueService();
      var executed = false;

      // Act
      var id = service.QueueOperation("CancelledOp", async (ct) =>
      {
        executed = true;
        await Task.CompletedTask;
      });

      var cancelled = service.CancelOperation(id);
      await service.ProcessQueueAsync();

      // Assert
      Assert.IsTrue(cancelled);
      Assert.IsFalse(executed);
      Assert.AreEqual(0, service.QueueCount);
    }

    [TestMethod]
    public async Task Operation_ReceivesCancellationToken()
    {
      // Arrange
      var service = new OperationQueueService();
      var tcs = new TaskCompletionSource<bool>();
      var wasCancelled = false;

      // Act
      var id = service.QueueOperation("LongOp", async (ct) =>
      {
        try
        {
          await Task.Delay(500, ct);
        }
        catch (OperationCanceledException)
        {
          wasCancelled = true;
        }
        tcs.SetResult(true);
      });

      // Start processing in background (simulated by not awaiting immediately if we could, 
      // but ProcessQueueAsync awaits sequentially. So we can't test mid-flight cancellation 
      // easily with the current simple ProcessQueueAsync implementation which awaits each op).

      // However, we can test that the token IS passed.
      // But wait, ProcessQueueAsync IS async. 
      // The current implementation of ProcessQueueAsync awaits sequentially.
      // To test cancellation *during* execution, we'd need ProcessQueueAsync to run concurrently or test logic differently.

      // Let's test that CancelOperation triggers the token.
      // We can't easily test "mid-flight" cancellation with the current synchronous-loop ProcessQueueAsync 
      // unless we run ProcessQueueAsync in a separate task.

      var processTask = Task.Run(() => service.ProcessQueueAsync());

      // Allow it to start
      await Task.Delay(50);

      // Cancel
      service.CancelOperation(id);

      await processTask;
      await tcs.Task;

      // Assert
      Assert.IsTrue(wasCancelled);
    }

    [TestMethod]
    public void QueueOperation_GeneratesCorrelationId()
    {
      // Arrange
      var service = new OperationQueueService();

      // Act
      var id = service.QueueOperation("Test", async () => await Task.CompletedTask);
      var op = service.GetQueuedOperations()[0];

      // Assert
      Assert.IsNotNull(op.CorrelationId);
      Assert.AreNotEqual(string.Empty, op.CorrelationId);
    }
  }
}