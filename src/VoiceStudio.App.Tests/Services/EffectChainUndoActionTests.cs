using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public class EffectChainUndoActionTests
{
  [TestMethod]
  public void EffectChainUndoSnapshots_CloneEffectChain_IsDeepCopy()
  {
    var chain = new EffectChain
    {
      Id = "c1",
      Name = "Chain",
      ProjectId = "p",
      Effects = new System.Collections.Generic.List<Effect>
      {
        new()
        {
          Id = "e1",
          Type = "eq",
          Name = "EQ",
          Enabled = true,
          Order = 0,
          Parameters = new System.Collections.Generic.List<EffectParameter>
          {
            new() { Name = "Low", Value = 1.0 }
          }
        }
      }
    };

    var clone = EffectChainUndoSnapshots.CloneEffectChain(chain);
    clone.Effects[0].Parameters[0].Value = 99.0;

    Assert.AreEqual(1.0, chain.Effects[0].Parameters[0].Value);
  }

  [TestMethod]
  public void ToggleBypassUndoAction_Undo_RestoresPriorState()
  {
    bool? last = null;
    var action = new ToggleBypassUndoAction(false, true, v => last = v);
    action.Undo();
    Assert.AreEqual(false, last);
  }

  [TestMethod]
  public void ToggleBypassUndoAction_Redo_AppliesRedoValue()
  {
    bool? last = null;
    var action = new ToggleBypassUndoAction(false, true, v => last = v);
    action.Redo();
    Assert.AreEqual(true, last);
  }

  [TestMethod]
  public void ToggleEffectEnabledUndoAction_Undo_CallsUpdateWithDisabled()
  {
    var client = new Mock<IEffectChainClient>();
    var effect = new Effect { Id = "e1", Enabled = true, Order = 0, Type = "eq", Name = "EQ" };
    var chain = new EffectChain
    {
      Id = "c1",
      Name = "C",
      ProjectId = "p1",
      Effects = new System.Collections.Generic.List<Effect> { effect }
    };
    var chains = new ObservableCollection<EffectChain> { chain };

    client
        .Setup(c => c.UpdateEffectChainAsync("p1", "c1", It.IsAny<EffectChain>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string _, string __, EffectChain ec, CancellationToken ___) => ec);

    var action = new ToggleEffectEnabledUndoAction(
        chains,
        client.Object,
        "p1",
        "c1",
        "e1",
        undoEnabled: false,
        redoEnabled: true);

    action.Undo();

    client.Verify(
        c => c.UpdateEffectChainAsync(
            "p1",
            "c1",
            It.Is<EffectChain>(ec => ec.Effects.Any(e => e.Id == "e1" && e.Enabled == false)),
            It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public void UpdateEffectChainSnapshotUndoAction_Undo_RestoresBeforeSnapshot()
  {
    var client = new Mock<IEffectChainClient>();
    var before = new EffectChain
    {
      Id = "c1",
      Name = "Before",
      ProjectId = "p1",
      Effects = new System.Collections.Generic.List<Effect>()
    };
    var after = new EffectChain
    {
      Id = "c1",
      Name = "After",
      ProjectId = "p1",
      Effects = new System.Collections.Generic.List<Effect>()
    };
    var chains = new ObservableCollection<EffectChain> { EffectChainUndoSnapshots.CloneEffectChain(after) };

    client
        .Setup(c => c.UpdateEffectChainAsync("p1", "c1", It.IsAny<EffectChain>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string _, string __, EffectChain ec, CancellationToken ___) => ec);

    var action = new UpdateEffectChainSnapshotUndoAction(
        chains,
        client.Object,
        "p1",
        "c1",
        before,
        after);

    action.Undo();

    client.Verify(
        c => c.UpdateEffectChainAsync(
            "p1",
            "c1",
            It.Is<EffectChain>(ec => ec.Name == "Before"),
            It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public void AddEffectAction_Undo_RemovesEffectFromChain()
  {
    var client = new Mock<IEffectChainClient>();
    var effect = new Effect { Id = "e1", Name = "E", Type = "eq", Order = 0, Enabled = true };
    var chain = new EffectChain { Id = "c1", Effects = new System.Collections.Generic.List<Effect> { effect } };
    var chains = new ObservableCollection<EffectChain> { chain };
    var action = new AddEffectAction(chains, "c1", effect);
    action.Undo();
    Assert.AreEqual(0, chains[0].Effects.Count);
  }

  [TestMethod]
  public void RemoveEffectAction_Undo_RestoresEffectAtOrder()
  {
    var chains = new ObservableCollection<EffectChain>
    {
      new()
      {
        Id = "c1",
        Effects = new System.Collections.Generic.List<Effect>()
      }
    };
    var removed = new Effect { Id = "e1", Order = 0, Name = "E", Type = "eq", Enabled = true };
    var action = new RemoveEffectAction(chains, "c1", removed, originalOrder: 0);
    action.Undo();
    Assert.AreEqual(1, chains[0].Effects.Count);
    Assert.AreEqual("e1", chains[0].Effects[0].Id);
  }
}
