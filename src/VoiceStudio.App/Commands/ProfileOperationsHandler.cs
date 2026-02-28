using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Commands
{
    /// <summary>
    /// Handles all voice profile-related commands: create, edit, delete, save, load.
    /// </summary>
    public sealed class ProfileOperationsHandler
    {
        private readonly IUnifiedCommandRegistry _registry;
        private readonly IProfilesUseCase _profilesUseCase;
        private readonly IDialogService _dialogService;
        private readonly ToastNotificationService? _toastService;

        private VoiceProfile? _selectedProfile;

        public event EventHandler<VoiceProfile?>? SelectedProfileChanged;
        public event EventHandler? ProfilesChanged;

        public ProfileOperationsHandler(
            IUnifiedCommandRegistry registry,
            IProfilesUseCase profilesUseCase,
            IDialogService dialogService,
            ToastNotificationService? toastService = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _profilesUseCase = profilesUseCase ?? throw new ArgumentNullException(nameof(profilesUseCase));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _toastService = toastService;

            RegisterCommands();
        }

        public VoiceProfile? SelectedProfile => _selectedProfile;

        private void RegisterCommands()
        {
            // profile.create
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "profile.create",
                    Title = "Create Profile",
                    Description = "Create a new voice profile",
                    Category = "Profile",
                    Icon = "➕"
                },
                async (param, ct) => await CreateProfileAsync(ct),
                _ => true
            );

            // profile.edit
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "profile.edit",
                    Title = "Edit Profile",
                    Description = "Edit the selected voice profile",
                    Category = "Profile",
                    Icon = "✏️"
                },
                async (param, ct) => await EditProfileAsync(param as string ?? _selectedProfile?.Id, ct),
                param => !string.IsNullOrEmpty(param as string) || _selectedProfile != null
            );

            // profile.delete
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "profile.delete",
                    Title = "Delete Profile",
                    Description = "Delete the selected voice profile",
                    Category = "Profile",
                    Icon = "🗑️"
                },
                async (param, ct) => await DeleteProfileAsync(param as string ?? _selectedProfile?.Id, ct),
                param => !string.IsNullOrEmpty(param as string) || _selectedProfile != null
            );

            // profile.save
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "profile.save",
                    Title = "Save Profile",
                    Description = "Save the current profile changes",
                    Category = "Profile",
                    Icon = "💾"
                },
                async (param, ct) => await SaveProfileAsync(ct),
                _ => _selectedProfile != null
            );

            // profile.load
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "profile.load",
                    Title = "Load Profile",
                    Description = "Load a voice profile",
                    Category = "Profile",
                    Icon = "📂"
                },
                async (param, ct) => await LoadProfileAsync(param as string, ct),
                _ => true
            );

            // profile.clone
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "profile.clone",
                    Title = "Clone Profile",
                    Description = "Clone the selected voice profile",
                    Category = "Profile",
                    Icon = "📋"
                },
                async (param, ct) => await CloneProfileAsync(param as string ?? _selectedProfile?.Id, ct),
                param => !string.IsNullOrEmpty(param as string) || _selectedProfile != null
            );

            // profile.select
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "profile.select",
                    Title = "Select Profile",
                    Description = "Select a voice profile",
                    Category = "Profile",
                    Icon = "👆"
                },
                async (param, ct) =>
                {
                    if (param is VoiceProfile profile)
                    {
                        SelectProfile(profile);
                    }
                    else if (param is string profileId)
                    {
                        await LoadProfileAsync(profileId, ct);
                    }
                },
                _ => true
            );

            Debug.WriteLine("[ProfileOperationsHandler] Registered 6 profile commands");
        }

        public async Task CreateProfileAsync(CancellationToken ct = default)
        {
            var name = await _dialogService.ShowInputAsync(
                "Create Voice Profile",
                "Enter profile name:",
                "New Voice Profile",
                "Profile name");

            if (string.IsNullOrWhiteSpace(name))
            {
                return; // User cancelled
            }

            try
            {
                var profile = await _profilesUseCase.CreateAsync(name, ct);
                _selectedProfile = profile;

                SelectedProfileChanged?.Invoke(this, profile);
                ProfilesChanged?.Invoke(this, EventArgs.Empty);
                _toastService?.ShowSuccess($"Created profile: {name}");

                Debug.WriteLine($"[ProfileOperationsHandler] Created profile: {name} (ID: {profile.Id})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileOperationsHandler] Create failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Create Failed",
                    $"Failed to create profile: {ex.Message}");
            }
        }

        public async Task EditProfileAsync(string? profileId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                await _dialogService.ShowMessageAsync("No Profile Selected", "Please select a profile to edit.");
                return;
            }

            // Get current profile data
            var profiles = await _profilesUseCase.ListAsync(ct);
            VoiceProfile? profile = null;
            foreach (var p in profiles)
            {
                if (p.Id == profileId)
                {
                    profile = p;
                    break;
                }
            }

            if (profile == null)
            {
                await _dialogService.ShowErrorAsync("Profile Not Found", $"Profile with ID '{profileId}' was not found.");
                return;
            }

            var newName = await _dialogService.ShowInputAsync(
                "Edit Voice Profile",
                "Enter new profile name:",
                profile.Name,
                "Profile name");

            if (string.IsNullOrWhiteSpace(newName) || newName == profile.Name)
            {
                return; // User cancelled or no change
            }

            try
            {
                var updated = await _profilesUseCase.UpdateAsync(
                    profileId,
                    newName,
                    null, null, null,
                    ct);

                if (_selectedProfile?.Id == profileId)
                {
                    _selectedProfile = updated;
                    SelectedProfileChanged?.Invoke(this, updated);
                }

                ProfilesChanged?.Invoke(this, EventArgs.Empty);
                _toastService?.ShowSuccess($"Profile updated: {newName}");

                Debug.WriteLine($"[ProfileOperationsHandler] Updated profile: {newName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileOperationsHandler] Edit failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Edit Failed",
                    $"Failed to update profile: {ex.Message}");
            }
        }

        public async Task DeleteProfileAsync(string? profileId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                await _dialogService.ShowMessageAsync("No Profile Selected", "Please select a profile to delete.");
                return;
            }

            // Get profile name for confirmation
            var profiles = await _profilesUseCase.ListAsync(ct);
            string? profileName = null;
            foreach (var p in profiles)
            {
                if (p.Id == profileId)
                {
                    profileName = p.Name;
                    break;
                }
            }

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Delete Profile",
                $"Are you sure you want to delete the profile '{profileName ?? profileId}'?\n\nThis action cannot be undone.",
                "Delete", "Cancel");

            if (!confirmed)
            {
                return;
            }

            try
            {
                var deleted = await _profilesUseCase.DeleteAsync(profileId, ct);

                if (deleted)
                {
                    if (_selectedProfile?.Id == profileId)
                    {
                        _selectedProfile = null;
                        SelectedProfileChanged?.Invoke(this, null);
                    }

                    ProfilesChanged?.Invoke(this, EventArgs.Empty);
                    _toastService?.ShowSuccess($"Profile deleted: {profileName ?? profileId}");

                    Debug.WriteLine($"[ProfileOperationsHandler] Deleted profile: {profileId}");
                }
                else
                {
                    await _dialogService.ShowErrorAsync("Delete Failed", "Profile could not be deleted. It may no longer exist.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileOperationsHandler] Delete failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Delete Failed",
                    $"Failed to delete profile: {ex.Message}");
            }
        }

        public async Task SaveProfileAsync(CancellationToken ct = default)
        {
            if (_selectedProfile == null)
            {
                await _dialogService.ShowMessageAsync("No Profile Selected", "Please select a profile to save.");
                return;
            }

            try
            {
                await _profilesUseCase.UpdateAsync(
                    _selectedProfile.Id,
                    _selectedProfile.Name,
                    _selectedProfile.Language,
                    _selectedProfile.Emotion,
                    _selectedProfile.Tags != null ? new List<string>(_selectedProfile.Tags) : null,
                    ct);

                _toastService?.ShowSuccess($"Profile saved: {_selectedProfile.Name}");
                Debug.WriteLine($"[ProfileOperationsHandler] Saved profile: {_selectedProfile.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileOperationsHandler] Save failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Save Failed",
                    $"Failed to save profile: {ex.Message}");
            }
        }

        public async Task LoadProfileAsync(string? profileId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                await _dialogService.ShowMessageAsync("No Profile ID", "No profile ID provided to load.");
                return;
            }

            try
            {
                var profiles = await _profilesUseCase.ListAsync(ct);
                VoiceProfile? profile = null;
                foreach (var p in profiles)
                {
                    if (p.Id == profileId)
                    {
                        profile = p;
                        break;
                    }
                }

                if (profile != null)
                {
                    SelectProfile(profile);
                    _toastService?.ShowInfo($"Loaded profile: {profile.Name}");
                    Debug.WriteLine($"[ProfileOperationsHandler] Loaded profile: {profile.Name}");
                }
                else
                {
                    await _dialogService.ShowErrorAsync("Profile Not Found", $"Profile '{profileId}' was not found.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileOperationsHandler] Load failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Load Failed",
                    $"Failed to load profile: {ex.Message}");
            }
        }

        public async Task CloneProfileAsync(string? profileId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                await _dialogService.ShowMessageAsync("No Profile Selected", "Please select a profile to clone.");
                return;
            }

            // Get original profile
            var profiles = await _profilesUseCase.ListAsync(ct);
            VoiceProfile? original = null;
            foreach (var p in profiles)
            {
                if (p.Id == profileId)
                {
                    original = p;
                    break;
                }
            }

            if (original == null)
            {
                await _dialogService.ShowErrorAsync("Profile Not Found", $"Profile '{profileId}' was not found.");
                return;
            }

            var newName = await _dialogService.ShowInputAsync(
                "Clone Profile",
                "Enter name for the cloned profile:",
                $"{original.Name} (Copy)",
                "Profile name");

            if (string.IsNullOrWhiteSpace(newName))
            {
                return; // User cancelled
            }

            try
            {
                var cloned = await _profilesUseCase.CreateAsync(
                    newName,
                    original.Language,
                    original.Emotion,
                    original.Tags != null ? new List<string>(original.Tags) : null,
                    ct);

                _selectedProfile = cloned;
                SelectedProfileChanged?.Invoke(this, cloned);
                ProfilesChanged?.Invoke(this, EventArgs.Empty);
                _toastService?.ShowSuccess($"Profile cloned: {newName}");

                Debug.WriteLine($"[ProfileOperationsHandler] Cloned profile '{original.Name}' to '{newName}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileOperationsHandler] Clone failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Clone Failed",
                    $"Failed to clone profile: {ex.Message}");
            }
        }

        public void SelectProfile(VoiceProfile? profile)
        {
            _selectedProfile = profile;
            SelectedProfileChanged?.Invoke(this, profile);
            Debug.WriteLine($"[ProfileOperationsHandler] Selected profile: {profile?.Name ?? "(none)"}");
        }
    }
}
