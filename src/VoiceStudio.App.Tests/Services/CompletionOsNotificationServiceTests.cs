using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class CompletionOsNotificationServiceTests
{
    [TestMethod]
    public void TryNotifyTerminalCompletion_Dedupes_SameCategoryOperationAndSuccess()
    {
        var titles = new List<string>();
        var bodies = new List<string>();
        var sut = new CompletionOsNotificationService((title, body) =>
        {
            titles.Add(title);
            bodies.Add(body);
        });

        const string batchTitle = "Batch complete";
        sut.TryNotifyTerminalCompletion(
            CompletionOsNotificationCategory.Batch,
            "job-1",
            true,
            batchTitle,
            "Alpha");
        sut.TryNotifyTerminalCompletion(
            CompletionOsNotificationCategory.Batch,
            "job-1",
            true,
            batchTitle,
            "Alpha duplicate");

        Assert.AreEqual(1, titles.Count);
        Assert.AreEqual(batchTitle, titles[0]);
        Assert.AreEqual("Alpha", bodies[0]);
    }

    [TestMethod]
    public void TryNotifyTerminalCompletion_AllowsSameOperationId_DifferentSuccessFlag()
    {
        var count = 0;
        var sut = new CompletionOsNotificationService((_, _) => count++);

        sut.TryNotifyTerminalCompletion(
            CompletionOsNotificationCategory.Training,
            "t1",
            true,
            "Training complete",
            "ok");
        sut.TryNotifyTerminalCompletion(
            CompletionOsNotificationCategory.Training,
            "t1",
            false,
            "Training failed",
            "fail");

        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public void TryNotifyTerminalCompletion_SkipsEmptyOperationId()
    {
        var count = 0;
        var sut = new CompletionOsNotificationService((_, _) => count++);

        sut.TryNotifyTerminalCompletion(
            CompletionOsNotificationCategory.Export,
            "   ",
            true,
            "Export complete",
            "x");

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void TryNotifyTerminalCompletion_PresenterException_IsNotThrown()
    {
        var sut = new CompletionOsNotificationService((_, _) => throw new System.InvalidOperationException("test"));

        sut.TryNotifyTerminalCompletion(
            CompletionOsNotificationCategory.Export,
            "e1",
            false,
            "Export failed",
            "err");
    }
}
