using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public class ScriptEditorUndoActionTests
{
  [TestMethod]
  public void UpdateScriptUndoAction_Undo_CallsClientWithBeforeRequest()
  {
    var client = new Mock<IScriptEditorClient>();
    var before = new ScriptUpdateRequest
    {
      Name = "A",
      Description = "d0",
      Segments = new List<ScriptSegment> { new() { Id = "s1", Text = "t0" } },
      Metadata = new Dictionary<string, object>()
    };
    var after = new ScriptUpdateRequest
    {
      Name = "B",
      Description = "d1",
      Segments = new List<ScriptSegment> { new() { Id = "s1", Text = "t1" } },
      Metadata = new Dictionary<string, object>()
    };

    ScriptUpdateRequest? captured = null;
    client
        .Setup(c => c.UpdateScriptAsync("id1", It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .Callback<string, ScriptUpdateRequest, CancellationToken>((_, r, _) => captured = r)
        .ReturnsAsync(new Script { Id = "id1", Name = "A" });

    var reloadCount = 0;
    var action = new UpdateScriptUndoAction(
        client.Object,
        "id1",
        before,
        after,
        () => reloadCount++,
        "Test update",
        sessionDirty: null,
        log: null);

    action.Undo();

    client.Verify(
        c => c.UpdateScriptAsync("id1", It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()),
        Times.Once);
    Assert.IsNotNull(captured);
    Assert.AreEqual("A", captured!.Name);
    Assert.AreEqual(1, reloadCount);
  }

  [TestMethod]
  public void UpdateScriptUndoAction_Redo_CallsClientWithAfterRequest()
  {
    var client = new Mock<IScriptEditorClient>();
    var before = new ScriptUpdateRequest { Name = "A", Segments = new List<ScriptSegment>(), Metadata = new Dictionary<string, object>() };
    var after = new ScriptUpdateRequest { Name = "B", Segments = new List<ScriptSegment>(), Metadata = new Dictionary<string, object>() };

    ScriptUpdateRequest? captured = null;
    client
        .Setup(c => c.UpdateScriptAsync("id1", It.IsAny<ScriptUpdateRequest>(), It.IsAny<CancellationToken>()))
        .Callback<string, ScriptUpdateRequest, CancellationToken>((_, r, _) => captured = r)
        .ReturnsAsync(new Script { Id = "id1", Name = "B" });

    var action = new UpdateScriptUndoAction(
        client.Object,
        "id1",
        before,
        after,
        () => { },
        "Test update");

    action.Redo();

    Assert.IsNotNull(captured);
    Assert.AreEqual("B", captured!.Name);
  }

  [TestMethod]
  public void CompositeUndoAction_Undo_ReversesOrder()
  {
    var order = new List<int>();
    var a = new MockUndo(order, 1);
    var b = new MockUndo(order, 2);
    var c = new MockUndo(order, 3);
    var composite = new CompositeUndoAction("batch", new IUndoableAction[] { a, b, c });

    composite.Undo();

    CollectionAssert.AreEqual(new[] { 3, 2, 1 }, order);
  }

  [TestMethod]
  public void CompositeUndoAction_Redo_ForwardOrder()
  {
    var order = new List<int>();
    var a = new MockUndo(order, 1);
    var b = new MockUndo(order, 2);
    var composite = new CompositeUndoAction("batch", new IUndoableAction[] { a, b });

    composite.Redo();

    CollectionAssert.AreEqual(new[] { 1, 2 }, order);
  }

  [TestMethod]
  public void CreateScriptAction_Undo_RemovesFromCollection()
  {
    var client = new Mock<IScriptEditorClient>();
    var scripts = new ObservableCollection<ScriptItem>();
    var script = new ScriptItem(new Script { Id = "x", Name = "N", ProjectId = "p", Segments = new List<ScriptSegment>(), Metadata = new Dictionary<string, object>(), Created = "", Modified = "", Version = 1 });
    scripts.Add(script);
    var action = new CreateScriptAction(scripts, client.Object, script);
    action.Undo();
    Assert.AreEqual(0, scripts.Count);
  }

  [TestMethod]
  public void DeleteScriptAction_Undo_RestoresAtOriginalIndex()
  {
    var client = new Mock<IScriptEditorClient>();
    var scripts = new ObservableCollection<ScriptItem>();
    var s0 = new ScriptItem(new Script { Id = "a", Name = "A", ProjectId = "p", Segments = new List<ScriptSegment>(), Metadata = new Dictionary<string, object>(), Created = "", Modified = "", Version = 1 });
    var s1 = new ScriptItem(new Script { Id = "b", Name = "B", ProjectId = "p", Segments = new List<ScriptSegment>(), Metadata = new Dictionary<string, object>(), Created = "", Modified = "", Version = 1 });
    scripts.Add(s0);
    scripts.Add(s1);
    var action = new DeleteScriptAction(scripts, client.Object, s0, 0);
    action.Undo();
    Assert.AreEqual(2, scripts.Count);
    Assert.AreEqual("a", scripts[0].Id);
  }

  private sealed class MockUndo : IUndoableAction
  {
    private readonly List<int> _order;
    private readonly int _id;

    public MockUndo(List<int> order, int id)
    {
      _order = order;
      _id = id;
    }

    public string ActionName => "mock";

    public void Undo() => _order.Add(_id);

    public void Redo() => _order.Add(_id);
  }
}
