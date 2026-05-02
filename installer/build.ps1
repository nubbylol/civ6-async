# Builds dist\civ6-async-Setup-<version>.exe via Inno Setup's CLI compiler.
# Requires Inno Setup 6+ installed. https://jrsoftware.org/isdl.php

$ErrorActionPreference = 'Stop'

$candidatePaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$iscc = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Error "ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isdl.php and re-run."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$issPath   = Join-Path $scriptDir 'civ6-async.iss'

& $iscc $issPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC failed with exit code $LASTEXITCODE"
}

Write-Host "Build complete. Output is in the dist\ folder."
