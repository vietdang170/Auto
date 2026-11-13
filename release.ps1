# Publishes a new version: sets version.txt, builds and tests Auto, then prepares the two files to upload to
# a GitHub release: release\vX.Y.Z\Auto.exe and release\vX.Y.Z\Auto.exe.sha256.
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version must look like 1.1.0 (got '$Version')." }
$versionFile = Join-Path $PSScriptRoot 'version.txt'
$previous = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ([version]$Version -le [version]$previous) { throw "The new version ($Version) must be higher than the current one ($previous)." }

Set-Content -LiteralPath $versionFile -Value $Version -Encoding ASCII -NoNewline
try { & (Join-Path $PSScriptRoot 'build.ps1') -Test }
catch {
    Set-Content -LiteralPath $versionFile -Value $previous -Encoding ASCII -NoNewline
    throw
}

$out = Join-Path $PSScriptRoot "release\v$Version"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe = Join-Path $out 'Auto.exe'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Auto.exe') -Destination $exe -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $out 'Auto.exe.sha256') -Value "$hash  Auto.exe" -Encoding ASCII

Write-Host ""
Write-Host "Version $Version is ready in: $out"
Write-Host "On GitHub: Releases > Draft a new release > tag v$Version, attach Auto.exe from that folder (Auto.exe.sha256 is optional), Publish."
