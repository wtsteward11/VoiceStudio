using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptEditHistoryServiceTests
{
  [TestMethod]
  public void AddEntry_InsertsNewestFirst()
  {
    var svc = new TranscriptEditHistoryService(10);
    svc.AddEntry(new TranscriptEditHistoryEntry { TranscriptionId = "a", MessageSummary = "1" });
    svc.AddEntry(new TranscriptEditHistoryEntry { TranscriptionId = "b", MessageSummary = "2" });
    Assert.AreEqual("b", svc.Entries[0].TranscriptionId);
    Assert.AreEqual("a", svc.Entries[1].TranscriptionId);
  }

  [TestMethod]
  public void AddEntry_TrimsAtMaxEntries()
  {
    var svc = new TranscriptEditHistoryService(2);
    svc.AddEntry(new TranscriptEditHistoryEntry { TranscriptionId = "1" });
    svc.AddEntry(new TranscriptEditHistoryEntry { TranscriptionId = "2" });
    svc.AddEntry(new TranscriptEditHistoryEntry { TranscriptionId = "3" });
    Assert.AreEqual(2, svc.Entries.Count);
    Assert.AreEqual("3", svc.Entries[0].TranscriptionId);
    Assert.AreEqual("2", svc.Entries[1].TranscriptionId);
  }

  [TestMethod]
  public void ClearSession_RemovesAll()
  {
    var svc = new TranscriptEditHistoryService();
    svc.AddEntry(new TranscriptEditHistoryEntry { TranscriptionId = "x" });
    svc.ClearSession();
    Assert.AreEqual(0, svc.Entries.Count);
  }
}
