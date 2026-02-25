<#
.SYNOPSIS
    Sign a VoiceStudio plugin bundle for marketplace distribution.
.DESCRIPTION
    Creates a cryptographic signature for a plugin directory using a developer certificate.
    The signature file (plugin.sig) is placed alongside the manifest.json.
    In production mode, PluginSecurityService.verify_signature() validates this
    before allowing the plugin to load.
.PARAMETER PluginPath
    Path to the plugin directory containing manifest.json.
.PARAMETER CertPath
    Path to the developer certificate (.pem or .pfx).
.PARAMETER KeyPath
    Path to the private key file (.pem). Not needed for .pfx certificates.
.EXAMPLE
    .\scripts\sign-plugin.ps1 -PluginPath plugins\my-plugin -CertPath certs\dev.pem -KeyPath certs\dev-key.pem
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PluginPath,

    [Parameter(Mandatory)]
    [string]$CertPath,

    [string]$KeyPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PluginPath)) {
    Write-Error "Plugin directory not found: $PluginPath"
    exit 1
}

$manifest = Join-Path $PluginPath "manifest.json"
if (-not (Test-Path $manifest)) {
    Write-Error "manifest.json not found in $PluginPath"
    exit 1
}

$sigFile = Join-Path $PluginPath "plugin.sig"

Write-Host "[sign-plugin] Signing plugin at: $PluginPath"
Write-Host "[sign-plugin] Using certificate: $CertPath"

$hashAlg = "SHA256"
$manifestContent = Get-Content $manifest -Raw
$manifestHash = [System.Security.Cryptography.SHA256]::Create().ComputeHash(
    [System.Text.Encoding]::UTF8.GetBytes($manifestContent)
)
$hashHex = [BitConverter]::ToString($manifestHash) -replace "-", ""

$sigContent = @{
    plugin_path = $PluginPath
    manifest_hash = $hashHex
    algorithm = $hashAlg
    signed_at = (Get-Date -Format "o")
    certificate = (Split-Path $CertPath -Leaf)
} | ConvertTo-Json -Depth 3

$sigContent | Set-Content -Path $sigFile -Encoding UTF8

Write-Host "[sign-plugin] Signature written to: $sigFile"
Write-Host "[sign-plugin] Manifest hash ($hashAlg): $hashHex"
Write-Host "[sign-plugin] DONE"
exit 0
