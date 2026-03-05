# Panel Load MS Verification

The perf budget runtime proof requires `startup_diagnostics.json` with `panel_load_ms > 0`.

## Generate startup_diagnostics.json

1. **Build the app**
   ```powershell
   dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
   ```

2. **Run the app once** (manually, with UI)
   ```powershell
   dotnet run --project src/VoiceStudio.App/VoiceStudio.App.csproj -c Debug -p:Platform=x64 --no-build
   ```

3. **Wait for deferred initialization** (~2–3 seconds after the main window appears)

4. **Verify the file exists**
   ```powershell
   Get-Content "$env:LOCALAPPDATA\VoiceStudio\Logs\startup_diagnostics.json"
   ```
   Confirm `panel_load_ms` is present and > 0.

5. **Run the perf proof writer**
   ```powershell
   python scripts/ci/write_perf_budget_runtime_proof.py
   ```

## First-run wizard bypass

If the first-run wizard blocks startup, set `FirstRunComplete` in app settings:
```powershell
$path = "$env:LOCALAPPDATA\VoiceStudio\appsettings.json"
@{ FirstRunComplete = $true } | ConvertTo-Json | Set-Content $path -Encoding UTF8 -Force
```
