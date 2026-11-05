# Install the checked-in Python add-in on Windows. -Symlink is for development.
[CmdletBinding()]
param([switch]$Symlink)

$ErrorActionPreference = 'Stop'
$Source = if (Test-Path (Join-Path $PSScriptRoot 'Fusion360MCP.py')) {
    $PSScriptRoot  # release zip layout
} else {
    Join-Path (Split-Path -Parent $PSScriptRoot) 'addon\Fusion360MCP'
}
$AddInsDir = if ($env:FUSION_MCP_ADDINS_DIR) {
    $env:FUSION_MCP_ADDINS_DIR
} else {
    Join-Path $env:APPDATA 'Autodesk\Autodesk Fusion 360\API\AddIns'
}
$Target = Join-Path $AddInsDir 'Fusion360MCP'
$StageDir = Join-Path $AddInsDir ".Fusion360MCP-stage-$PID-$([guid]::NewGuid().ToString('N'))"
$Staged = Join-Path $StageDir 'Fusion360MCP'
$Backup = Join-Path $AddInsDir ".Fusion360MCP-backup-$PID-$([guid]::NewGuid().ToString('N'))"

if (-not (Test-Path (Join-Path $Source 'Fusion360MCP.py')) -or
    -not (Test-Path (Join-Path $Source 'Fusion360MCP.manifest'))) {
    throw "Python add-in package is incomplete: $Source"
}

New-Item -ItemType Directory -Force -Path $AddInsDir | Out-Null
New-Item -ItemType Directory -Path $StageDir | Out-Null
try {
    if ($Symlink) {
        New-Item -ItemType SymbolicLink -Path $Staged -Target $Source | Out-Null
    } else {
        Copy-Item -Path $Source -Destination $Staged -Recurse
    }
    $ManifestPath = Join-Path $Staged 'Fusion360MCP.manifest'
    $ManifestJson = Get-Content -Raw $ManifestPath
    $JsonCommand = Get-Command ConvertFrom-Json
    if ($JsonCommand.Parameters.ContainsKey('AsHashtable')) {
        # PowerShell 7 rejects valid JSON properties with an empty name unless
        # it creates a hashtable. Autodesk manifests use description[""] as
        # the default locale, so retain that valid key while checking fields.
        $Manifest = $ManifestJson | ConvertFrom-Json -AsHashtable
    } else {
        # Windows PowerShell 5.1 predates -AsHashtable and accepts the manifest
        # through its PSCustomObject conversion path.
        $Manifest = $ManifestJson | ConvertFrom-Json
    }
    if ($Manifest.autodeskProduct -ne 'Fusion360' -or $Manifest.type -ne 'addin' -or
        $Manifest.supportedOS -ne 'windows|mac' -or
        -not (Test-Path (Join-Path $Staged 'Fusion360MCP.py'))) {
        throw "Staged Python add-in failed validation: $Staged"
    }

    if (Test-Path $Target) { Move-Item $Target $Backup }
    try {
        Move-Item $Staged $Target
    } catch {
        if (Test-Path $Backup) { Move-Item $Backup $Target }
        throw
    }
    if (Test-Path $Backup) { Remove-Item $Backup -Recurse -Force }
} finally {
    if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
}

Write-Host "Installed Python Fusion360MCP add-in at $Target"
Write-Host 'Start or restart Fusion, run the add-in if needed, then call ping and get_scene_info.'
