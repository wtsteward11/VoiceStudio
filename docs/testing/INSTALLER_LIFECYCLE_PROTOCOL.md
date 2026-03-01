# Gate H: Installer Lifecycle Test Protocol

**Purpose:** Prove install, launch, upgrade, rollback, and uninstall on clean VMs.

## Prerequisites

1. Build the installer from Gate C publish output:
   ```powershell
   .\installer\build-installer.ps1 -InstallerType InnoSetup -Configuration Release -Version 1.0.0
   ```
2. Build a second version for upgrade testing:
   ```powershell
   .\installer\build-installer.ps1 -InstallerType InnoSetup -Configuration Release -Version 1.0.1
   ```
3. Verify both installers exist:
   ```powershell
   .\installer\verify-installer.ps1
   ```

## Automated Lifecycle Test

Run on a clean Windows VM:

```powershell
.\installer\test-installer-lifecycle.ps1 `
    -InstallerV1Path "installer\Output\VoiceStudio-Setup-v1.0.0.exe" `
    -InstallerV2Path "installer\Output\VoiceStudio-Setup-v1.0.1.exe" `
    -LogDir "C:\logs"
```

This script handles:
- Install v1.0.0 with logging
- Launch and verify
- Upgrade to v1.0.1 with logging
- Verify upgrade preserved data
- Rollback to v1.0.0 (via VM snapshot)
- Uninstall with logging
- Verify no leftover files

## Evidence Packet

Capture and store in `docs/release/evidence/`:
- `C:\logs\voicestudio_install_*.log`
- `C:\logs\voicestudio_lifecycle_*.log`
- `C:\logs\voicestudio_uninstall_*.log`
- Screenshot of successful launch on clean VM

## PASS Criteria

- `test-installer-lifecycle.ps1` exits 0
- All log files show no errors
- App launches and renders on clean VM
- Uninstall leaves no files in Program Files
