# Excluded Module Directories

The following directories under `src/` contain **future work** and are **not** part of the current solution (`VoiceStudio.sln`):

- `VoiceStudio.Common.UI`
- `VoiceStudio.Module.Analysis`
- `VoiceStudio.Module.Media`
- `VoiceStudio.Module.Voice`
- `VoiceStudio.Module.Workflow`

**Do not add these projects to `VoiceStudio.sln` without Tyler's approval.** The solution is intentionally limited to three projects: `VoiceStudio.Core`, `VoiceStudio.App`, and `VoiceStudio.App.Tests`. Adding excluded modules would change protected build configuration and is blocked by the build-config lockfile and pre-commit hooks.
