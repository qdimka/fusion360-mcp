# Install the Fusion360MCP C# add-in into Fusion 360's AddIns folder (Windows).
# Builds first, stages and validates the package, then replaces the old folder.
# Usage: .\scripts\install-addon.ps1 [-Symlink]
[CmdletBinding()]
param(
    [switch]$Symlink
)

$ErrorActionPreference = "Stop"

$RepoDir   = Split-Path -Parent $PSScriptRoot
$Project   = Join-Path $RepoDir "src\Fusion360.Mcp.AddIn\Fusion360.Mcp.AddIn.csproj"
$BuildOut  = Join-Path $RepoDir "src\Fusion360.Mcp.AddIn\bin\Debug\net48"
$AddInsDir = Join-Path $env:APPDATA "Autodesk\Autodesk Fusion 360\API\AddIns"
$Target    = Join-Path $AddInsDir "Fusion360MCP"
$Stage     = Join-Path $AddInsDir ".Fusion360MCP-stage-$PID"
$Backup    = Join-Path $AddInsDir ".Fusion360MCP-backup-$PID"

Write-Host "Building add-in..."
dotnet build $Project -c Debug --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$ManifestPath = Join-Path $BuildOut "Fusion360MCP.manifest"
$AssemblyPath = Join-Path $BuildOut "Fusion360.Mcp.AddIn.dll"
if (-not (Test-Path $ManifestPath) -or -not (Test-Path $AssemblyPath)) {
    throw "Build output is incomplete: $BuildOut"
}
# Incremental builds do not delete outputs from removed package references.
# Remove Roslyn artifacts left by pre-removal builds before copy or junction.
Get-ChildItem $BuildOut -Filter "Microsoft.CodeAnalysis*.dll" -ErrorAction SilentlyContinue |
    Remove-Item -Force
Get-ChildItem $BuildOut -Filter "Microsoft.CodeAnalysis*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force
$Manifest = Get-Content -Raw $ManifestPath | ConvertFrom-Json
if ($Manifest.autodeskProduct -ne "Fusion360" -or $Manifest.type -ne "addin" -or
    $Manifest.supportedOS -ne "windows" -or -not $Manifest.runOnStartup) {
    throw "Add-in manifest failed validation: $ManifestPath"
}

New-Item -ItemType Directory -Force $AddInsDir | Out-Null
Remove-Item $Stage, $Backup -Recurse -Force -ErrorAction SilentlyContinue
try {
    if ($Symlink) {
        New-Item -ItemType Junction -Path $Stage -Target $BuildOut | Out-Null
        if (Test-Path (Join-Path $BuildOut "adsknet.dll")) {
            throw "The build output contains the compile-time adsknet stub; refusing symlink install."
        }
    }
    else {
        New-Item -ItemType Directory $Stage | Out-Null
        Copy-Item -Path (Join-Path $BuildOut "*") -Destination $Stage -Recurse
        Remove-Item (Join-Path $Stage "adsknet.dll") -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $Stage "adsknet.pdb") -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path (Join-Path $Stage "Fusion360.Mcp.AddIn.dll")) -or
        -not (Test-Path (Join-Path $Stage "Fusion360MCP.manifest"))) {
        throw "Staged add-in package is incomplete."
    }
    if (Test-Path (Join-Path $Stage "adsknet.dll")) {
        throw "The compile-time adsknet stub must not be installed."
    }

    if (Test-Path $Target) { Move-Item $Target $Backup }
    try {
        Move-Item $Stage $Target
    }
    catch {
        if (Test-Path $Backup) { Move-Item $Backup $Target }
        throw
    }
    Remove-Item $Backup -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
    throw
}

if ($Symlink) {
    Write-Host "Installed via junction (development mode)."
}
else {
    Write-Host "Installed add-in at $Target"
}
Write-Host "The manifest enables runOnStartup. Start Fusion to load the add-in."
Write-Host "Verify the connection with the MCP ping tool; listener default: localhost:9876."
