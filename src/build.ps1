# Builds self-contained civ6-async CLI binaries for Windows and Linux.
# Output: dist/cli/{win-x64,linux-x64}/civ6-async[.exe]

$ErrorActionPreference = 'Stop'

$candidates = @(
    "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
    "$env:ProgramFiles\dotnet\dotnet.exe"
)
$dotnet = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dotnet) {
    Write-Error "dotnet not found. Install .NET 8 SDK from https://dot.net"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj      = Join-Path $scriptDir 'Civ6Async.Cli\Civ6Async.Cli.csproj'
$distRoot  = Join-Path (Split-Path -Parent $scriptDir) 'dist\cli'

foreach ($rid in @('win-x64', 'linux-x64')) {
    $out = Join-Path $distRoot $rid
    Write-Host "=== Publishing $rid -> $out ==="
    & $dotnet publish $proj -c Release -r $rid --self-contained -o $out
    if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed for $rid" }
}

Write-Host "`nBuild complete:"
Get-ChildItem $distRoot -Recurse -File | Select-Object FullName, @{n='SizeMB';e={[math]::Round($_.Length/1MB,1)}}
