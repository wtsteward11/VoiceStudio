using System;
using System.Collections.ObjectModel;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Services.UndoableActions
{
  /// <summary>
  /// Undoable action for creating a voice profile.
  /// </summary>
  public class CreateProfileAction : IUndoableAction
  {
    private readonly ObservableCollection<VoiceProfile> _profiles;
    private readonly VoiceProfile _profile;
    private readonly Action<VoiceProfile>? _onUndo;
    private readonly Action<VoiceProfile>? _onRedo;

    public string ActionName => $"Create Profile '{_profile.Name ?? "Unnamed"}'";

    public CreateProfileAction(
        ObservableCollection<VoiceProfile> profiles,
        IProfilesClient profilesClient,
        VoiceProfile profile,
        Action<VoiceProfile>? onUndo = null,
        Action<VoiceProfile>? onRedo = null)
    {
      _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
      _ = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _profile = profile ?? throw new ArgumentNullException(nameof(profile));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Remove the profile from the collection
      var profileToRemove = _profiles.FirstOrDefault(p => p.Id == _profile.Id);
      if (profileToRemove != null)
      {
        _profiles.Remove(profileToRemove);
        _onUndo?.Invoke(profileToRemove);
      }
    }

    public void Redo()
    {
      // Re-add the profile to the collection if not already present
      if (!_profiles.Any(p => p.Id == _profile.Id))
      {
        _profiles.Add(_profile);
        _onRedo?.Invoke(_profile);
      }
    }
  }

  /// <summary>
  /// Undoable action for deleting a voice profile.
  /// </summary>
  public class DeleteProfileAction : IUndoableAction
  {
    private readonly ObservableCollection<VoiceProfile> _profiles;
    private readonly VoiceProfile _profile;
    private readonly int _originalIndex;
    private readonly Action<VoiceProfile>? _onUndo;
    private readonly Action<VoiceProfile>? _onRedo;

    public string ActionName => $"Delete Profile '{_profile.Name ?? "Unnamed"}'";

    public DeleteProfileAction(
        ObservableCollection<VoiceProfile> profiles,
        IProfilesClient profilesClient,
        VoiceProfile profile,
        Action<VoiceProfile>? onUndo = null,
        Action<VoiceProfile>? onRedo = null)
    {
      _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
      _ = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _profile = profile ?? throw new ArgumentNullException(nameof(profile));
      _originalIndex = profiles.IndexOf(profile);
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Re-add the profile at its original position
      if (!_profiles.Any(p => p.Id == _profile.Id))
      {
        if (_originalIndex >= 0 && _originalIndex <= _profiles.Count)
        {
          _profiles.Insert(_originalIndex, _profile);
        }
        else
        {
          _profiles.Add(_profile);
        }
        _onUndo?.Invoke(_profile);
      }
    }

    public void Redo()
    {
      // Remove the profile from the collection
      var profileToRemove = _profiles.FirstOrDefault(p => p.Id == _profile.Id);
      if (profileToRemove != null)
      {
        _profiles.Remove(profileToRemove);
        _onRedo?.Invoke(profileToRemove);
      }
    }
  }

  /// <summary>
  /// Undoable action for batch deleting multiple voice profiles.
  /// </summary>
  public class BatchDeleteProfilesAction : IUndoableAction
  {
    private readonly ObservableCollection<VoiceProfile> _profiles;
    private readonly System.Collections.Generic.List<(VoiceProfile Profile, int Index)> _deletedProfiles;
    private readonly Action<System.Collections.Generic.IEnumerable<VoiceProfile>>? _onUndo;
    private readonly Action<System.Collections.Generic.IEnumerable<VoiceProfile>>? _onRedo;

    public string ActionName => $"Delete {_deletedProfiles.Count} Profile(s)";

    public BatchDeleteProfilesAction(
        ObservableCollection<VoiceProfile> profiles,
        IProfilesClient profilesClient,
        System.Collections.Generic.IEnumerable<VoiceProfile> profilesToDelete,
        Action<System.Collections.Generic.IEnumerable<VoiceProfile>>? onUndo = null,
        Action<System.Collections.Generic.IEnumerable<VoiceProfile>>? onRedo = null)
    {
      _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
      _ = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _deletedProfiles = profilesToDelete?.Select(p => (p, profiles.IndexOf(p))).ToList()
          ?? throw new ArgumentNullException(nameof(profilesToDelete));
      _onUndo = onUndo;
      _onRedo = onRedo;
    }

    public void Undo()
    {
      // Re-add all profiles at their original positions (in reverse order to maintain indices)
      var profilesToRestore = _deletedProfiles.OrderByDescending(x => x.Index).ToList();
      foreach (var (profile, originalIndex) in profilesToRestore)
      {
        if (!_profiles.Any(p => p.Id == profile.Id))
        {
          if (originalIndex >= 0 && originalIndex <= _profiles.Count)
          {
            _profiles.Insert(originalIndex, profile);
          }
          else
          {
            _profiles.Add(profile);
          }
        }
      }
      _onUndo?.Invoke(_deletedProfiles.Select(x => x.Profile));
    }

    public void Redo()
    {
      // Remove all profiles from the collection
      foreach (var profile in _profiles.Where(p => _deletedProfiles.Any(d => d.Profile.Id == p.Id)).ToList())
      {
        _profiles.Remove(profile);
      }
      _onRedo?.Invoke(_deletedProfiles.Select(x => x.Profile));
    }
  }
}