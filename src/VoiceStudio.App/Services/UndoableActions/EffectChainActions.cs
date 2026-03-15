using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating an effect chain.
  /// Migration: Before EffectsMixer seam extraction, this must accept IEffectChainClient instead of IBackendClient.
  /// See docs/design/EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md §5.
  /// </summary>
  public class CreateEffectChainAction : IUndoableAction
  {
    private readonly ObservableCollection<EffectChain> _chains;
    private readonly IEffectChainClient _effectChainClient;
    private readonly EffectChain _chain;
    private readonly Action<EffectChain>? _onUndo;
    private readonly Action<EffectChain>? _onRedo;

    public string ActionName => $"Create Effect Chain '{_chain.Name ?? "Unnamed"}'";

    public CreateEffectChainAction(
        ObservableCollection<EffectChain> chains,
        IEffectChainClient effectChainClient,
        EffectChain chain,
        Action<EffectChain>? onUndo = null,
        Action<EffectChain>? onRedo = null)
    {
      _chains = chains ?? throw new ArgumentNullException(nameof(chains));
      _effectChainClient = effectChainClient ?? throw new ArgumentNullException(nameof(effectChainClient));
      _chain = chain ?? throw new ArgumentNullException(nameof(chain));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var chainToRemove = _chains.FirstOrDefault(c => c.Id == _chain.Id);
      if (chainToRemove != null)
      {
        _chains.Remove(chainToRemove);
        _onUndo?.Invoke(chainToRemove);
      }
    }

    public void Redo()
    {
      if (!_chains.Any(c => c.Id == _chain.Id))
      {
        _chains.Insert(0, _chain); // Insert at beginning to match creation behavior
        _onRedo?.Invoke(_chain);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting an effect chain.
  /// Migration: Before EffectsMixer seam extraction, this must accept IEffectChainClient instead of IBackendClient.
  /// See docs/design/EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md §5.
  /// </summary>
  public class DeleteEffectChainAction : IUndoableAction
  {
    private readonly ObservableCollection<EffectChain> _chains;
    private readonly IEffectChainClient _effectChainClient;
    private readonly EffectChain _chain;
    private readonly int _originalIndex;
    private readonly Action<EffectChain>? _onUndo;
    private readonly Action<EffectChain>? _onRedo;

    public string ActionName => $"Delete Effect Chain '{_chain.Name ?? "Unnamed"}'";

    public DeleteEffectChainAction(
        ObservableCollection<EffectChain> chains,
        IEffectChainClient effectChainClient,
        EffectChain chain,
        int originalIndex,
        Action<EffectChain>? onUndo = null,
        Action<EffectChain>? onRedo = null)
    {
      _chains = chains ?? throw new ArgumentNullException(nameof(chains));
      _effectChainClient = effectChainClient ?? throw new ArgumentNullException(nameof(effectChainClient));
      _chain = chain ?? throw new ArgumentNullException(nameof(chain));
      _originalIndex = originalIndex;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      if (!_chains.Any(c => c.Id == _chain.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _chains.Count)
        {
          _chains.Insert(_originalIndex, _chain);
        }
        else
        {
          _chains.Add(_chain);
        }
        _onUndo?.Invoke(_chain);
      }
    }

    public void Redo()
    {
      var chainToRemove = _chains.FirstOrDefault(c => c.Id == _chain.Id);
      if (chainToRemove != null)
      {
        _chains.Remove(chainToRemove);
        _onRedo?.Invoke(chainToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for adding an effect to a chain.
  /// </summary>
  public class AddEffectAction : IUndoableAction
  {
    private readonly ObservableCollection<EffectChain> _chains;
    private readonly string _chainId;
    private readonly Effect _effect;
    private readonly int _originalOrder;
    private readonly Action<Effect>? _onUndo;
    private readonly Action<Effect>? _onRedo;

    public string ActionName => $"Add Effect '{_effect.Name ?? _effect.Type ?? "Unnamed"}'";

    public AddEffectAction(
        ObservableCollection<EffectChain> chains,
        string chainId,
        Effect effect,
        Action<Effect>? onUndo = null,
        Action<Effect>? onRedo = null)
    {
      _chains = chains ?? throw new ArgumentNullException(nameof(chains));
      _chainId = chainId ?? throw new ArgumentNullException(nameof(chainId));
      _effect = effect ?? throw new ArgumentNullException(nameof(effect));
      _originalOrder = effect.Order;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var chain = _chains.FirstOrDefault(c => c.Id == _chainId);
      if (chain?.Effects != null)
      {
        var effectToRemove = chain.Effects.FirstOrDefault(e => e.Id == _effect.Id);
        if (effectToRemove != null)
        {
          chain.Effects.Remove(effectToRemove);
          // Reorder remaining effects
          for (int i = 0; i < chain.Effects.Count; i++)
          {
            chain.Effects[i].Order = i;
          }
          _onUndo?.Invoke(effectToRemove);
        }
      }
    }

    public void Redo()
    {
      var chain = _chains.FirstOrDefault(c => c.Id == _chainId);
      if (chain?.Effects != null && !chain.Effects.Any(e => e.Id == _effect.Id))
      {
        // Restore original order if possible, otherwise add at end
        if (_originalOrder >= 0 && _originalOrder <= chain.Effects.Count)
        {
          _effect.Order = _originalOrder;
          // Shift other effects
          foreach (var e in chain.Effects.Where(e => e.Order >= _originalOrder))
          {
            e.Order++;
          }
          chain.Effects.Insert(_originalOrder, _effect);
        }
        else
        {
          _effect.Order = chain.Effects.Count;
          chain.Effects.Add(_effect);
        }
        _onRedo?.Invoke(_effect);
      }
    }
  }

  /// <summary>
  /// Undoable action for removing an effect from a chain.
  /// </summary>
  public class RemoveEffectAction : IUndoableAction
  {
    private readonly ObservableCollection<EffectChain> _chains;
    private readonly string _chainId;
    private readonly Effect _effect;
    private readonly int _originalOrder;
    private readonly Action<Effect>? _onUndo;
    private readonly Action<Effect>? _onRedo;

    public string ActionName => $"Remove Effect '{_effect.Name ?? _effect.Type ?? "Unnamed"}'";

    public RemoveEffectAction(
        ObservableCollection<EffectChain> chains,
        string chainId,
        Effect effect,
        int originalOrder,
        Action<Effect>? onUndo = null,
        Action<Effect>? onRedo = null)
    {
      _chains = chains ?? throw new ArgumentNullException(nameof(chains));
      _chainId = chainId ?? throw new ArgumentNullException(nameof(chainId));
      _effect = effect ?? throw new ArgumentNullException(nameof(effect));
      _originalOrder = originalOrder;
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      var chain = _chains.FirstOrDefault(c => c.Id == _chainId);
      if (chain?.Effects != null && !chain.Effects.Any(e => e.Id == _effect.Id))
      {
        // Restore at original position
        if (_originalOrder >= 0 && _originalOrder <= chain.Effects.Count)
        {
          _effect.Order = _originalOrder;
          // Shift other effects
          foreach (var e in chain.Effects.Where(e => e.Order >= _originalOrder))
          {
            e.Order++;
          }
          chain.Effects.Insert(_originalOrder, _effect);
        }
        else
        {
          _effect.Order = chain.Effects.Count;
          chain.Effects.Add(_effect);
        }
        _onUndo?.Invoke(_effect);
      }
    }

    public void Redo()
    {
      var chain = _chains.FirstOrDefault(c => c.Id == _chainId);
      if (chain?.Effects != null)
      {
        var effectToRemove = chain.Effects.FirstOrDefault(e => e.Id == _effect.Id);
        if (effectToRemove != null)
        {
          chain.Effects.Remove(effectToRemove);
          // Reorder remaining effects
          for (int i = 0; i < chain.Effects.Count; i++)
          {
            chain.Effects[i].Order = i;
          }
          _onRedo?.Invoke(effectToRemove);
        }
      }
    }
  }

  /// <summary>
  /// Undoable action for moving an effect in a chain (up or down).
  /// </summary>
  public class MoveEffectAction : IUndoableAction
  {
    private readonly ObservableCollection<EffectChain> _chains;
    private readonly string _chainId;
    private readonly string _effectId;
    private readonly int _oldOrder;
    private readonly int _newOrder;
    private readonly bool _isMovingUp;

    public string ActionName => $"Move Effect {(_isMovingUp ? "Up" : "Down")}";

    public MoveEffectAction(
        ObservableCollection<EffectChain> chains,
        string chainId,
        string effectId,
        int oldOrder,
        int newOrder,
        bool isMovingUp)
    {
      _chains = chains ?? throw new ArgumentNullException(nameof(chains));
      _chainId = chainId ?? throw new ArgumentNullException(nameof(chainId));
      _effectId = effectId ?? throw new ArgumentNullException(nameof(effectId));
      _oldOrder = oldOrder;
      _newOrder = newOrder;
      _isMovingUp = isMovingUp;
    }

    public void Undo()
    {
      var chain = _chains.FirstOrDefault(c => c.Id == _chainId);
      if (chain?.Effects != null)
      {
        var effect = chain.Effects.FirstOrDefault(e => e.Id == _effectId);
        var otherEffect = chain.Effects.FirstOrDefault(e => e.Order == _newOrder && e.Id != _effectId);

        if (effect != null && otherEffect != null)
        {
          // Swap back
          effect.Order = _oldOrder;
          otherEffect.Order = _newOrder;
          chain.Effects.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
      }
    }

    public void Redo()
    {
      var chain = _chains.FirstOrDefault(c => c.Id == _chainId);
      if (chain?.Effects != null)
      {
        var effect = chain.Effects.FirstOrDefault(e => e.Id == _effectId);
        var otherEffect = chain.Effects.FirstOrDefault(e => e.Order == _oldOrder && e.Id != _effectId);

        if (effect != null && otherEffect != null)
        {
          // Swap again
          effect.Order = _newOrder;
          otherEffect.Order = _oldOrder;
          chain.Effects.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
      }
    }
  }
}