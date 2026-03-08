# Stop any running VoiceStudio process so the build can replace the executable.
# Run before build when the app is open to avoid locked-file failures.
$name = "VoiceStudio.App"
$procs = Get-Process -Name $name -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Write-Host "Stopped $($procs.Count) process(es): $name"
} else {
    Write-Host "No process found: $name"
}
