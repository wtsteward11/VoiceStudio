using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
[TestCategory("Services")]
public sealed class ProjectSessionDirtyStateTests
{
    [TestMethod]
    public void MarkProjectDirty_RaisesChanged_WhenTransitioningToDirty()
    {
        var dirty = new ProjectSessionDirtyState();
        var raised = 0;
        dirty.DirtyStateChanged += (_, _) => raised++;

        dirty.MarkProjectDirty("test");

        Assert.IsTrue(dirty.IsProjectDirty);
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void MarkProjectClean_RaisesChanged_WhenTransitioningToClean()
    {
        var dirty = new ProjectSessionDirtyState();
        dirty.MarkProjectDirty("x");
        var raised = 0;
        dirty.DirtyStateChanged += (_, _) => raised++;

        dirty.MarkProjectClean();

        Assert.IsFalse(dirty.IsProjectDirty);
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void Suppress_PreventsMarkDirty()
    {
        var dirty = new ProjectSessionDirtyState();
        var raised = 0;
        dirty.DirtyStateChanged += (_, _) => raised++;

        dirty.EnterSuppressDirtyNotifications();
        try
        {
            dirty.MarkProjectDirty("during suppress");
            Assert.IsFalse(dirty.IsProjectDirty);
            Assert.AreEqual(0, raised);
        }
        finally
        {
            dirty.ExitSuppressDirtyNotifications();
        }

        dirty.MarkProjectDirty("after");
        Assert.IsTrue(dirty.IsProjectDirty);
    }

    [TestMethod]
    public void MarkProjectDirty_IdempotentWhileDirty_DoesNotSpamEvents()
    {
        var dirty = new ProjectSessionDirtyState();
        var raised = 0;
        dirty.DirtyStateChanged += (_, _) => raised++;

        dirty.MarkProjectDirty("a");
        dirty.MarkProjectDirty("b");

        Assert.IsTrue(dirty.IsProjectDirty);
        Assert.AreEqual(1, raised);
    }
}
