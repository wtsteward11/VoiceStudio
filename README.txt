VOICESTUDIO M0 STARTER (no git required)

1) Core (brain)
   - Open PowerShell
   - Run:
     cd "%USERPROFILE%\Desktop\VoiceStudio\src\Core"
     dotnet build VoiceStudio.Core.csproj -c Debug
     dotnet run --project VoiceStudio.Core.csproj

   You should see:
     SQLite journal_mode=WAL
     Core health OK (stub).

2) Worker (ML helper)
   - Open PowerShell
   - Run:
     cd "%USERPROFILE%\Desktop\VoiceStudio\workers\python\vsdml"
     powershell -ExecutionPolicy Bypass -File dev-venv.ps1
     python run_worker.py

   You should see:
     Worker starting (stub)...
     health: True

3) Plugin host test (optional)
   - Open PowerShell
   - Run:
     cd "%USERPROFILE%\Desktop\VoiceStudio\src\PluginHost"
     dotnet build VoiceStudio.PluginHost.csproj -c Debug
     dotnet run --project VoiceStudio.PluginHost.csproj

   You should see it load the sample.null manifest.

Agent A will add the real Windows UI in src/App.
