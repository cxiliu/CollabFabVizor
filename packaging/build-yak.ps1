<#
.SYNOPSIS
    Builds Vizor in Release and packages it into a .yak file.

.DESCRIPTION
    One command to go from source to a shippable yak package. Works on a dev
    machine with Rhino installed and on a bare CI runner with no Rhino at all
    (the Grasshopper/RhinoCommon NuGet packages supply the build-time refs, and
    yak.exe is downloaded on demand).

    The version is read from <VizorVersion> in Directory.Build.props. That is
    the single source of truth - this script never invents a version number.

.PARAMETER OutputDir
    Where the staged package tree and the .yak file are written.
    Defaults to <repo>/Release/vizor-<version>.

.PARAMETER SkipBuild
    Reuse whatever is already in Vizor/bin/Release instead of rebuilding.

.EXAMPLE
    pwsh packaging/build-yak.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDir,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Packaging = $PSScriptRoot

# --- Version: read the one and only source of truth -------------------------
$propsPath = Join-Path $RepoRoot 'Directory.Build.props'
[xml] $props = Get-Content -LiteralPath $propsPath -Raw
$node = $props.SelectSingleNode('/Project/PropertyGroup/VizorVersion')
if (-not $node) {
    throw "Could not read <VizorVersion> from $propsPath"
}
$version = $node.InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VizorVersion '$version' is not in major.minor.patch form."
}
Write-Host "[yak] Version: $version" -ForegroundColor Cyan

# --- Build ------------------------------------------------------------------
$binRelease = Join-Path $RepoRoot 'Vizor/bin/Release'
if (-not $SkipBuild) {
    Write-Host "[yak] Building Release (all target frameworks)..." -ForegroundColor Cyan
    & dotnet build (Join-Path $RepoRoot 'Vizor/Vizor.csproj') -c Release -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}
if (-not (Test-Path $binRelease)) {
    throw "No build output at $binRelease. Run without -SkipBuild."
}

# --- Stage ------------------------------------------------------------------
if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "Release/vizor-$version"
}
if (Test-Path $OutputDir) { Remove-Item -LiteralPath $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path

# The yak layout is: one folder per target framework, plus manifest + icon at
# the root. Mirrors exactly what `cp -r Vizor/bin/Release/ build/` used to do.
$tfms = @('net48', 'net7.0', 'net7.0-windows')
foreach ($tfm in $tfms) {
    $src = Join-Path $binRelease $tfm
    if (-not (Test-Path $src)) { throw "Missing build output for $tfm at $src" }
    Copy-Item -LiteralPath $src -Destination (Join-Path $OutputDir $tfm) -Recurse
    Write-Host "[yak] Staged $tfm"
}

Copy-Item -LiteralPath (Join-Path $Packaging 'Logo_64x64.png') -Destination $OutputDir

# Stamp the real version into the staged manifest. The committed manifest keeps
# its 0.0.0 placeholder so it can never ship a stale hand-edited number.
$manifest = Get-Content -LiteralPath (Join-Path $Packaging 'manifest.yml') -Raw
$manifest = $manifest -replace '(?m)^version:\s*.*$', "version: $version"
Set-Content -LiteralPath (Join-Path $OutputDir 'manifest.yml') -Value $manifest -Encoding utf8 -NoNewline
Write-Host "[yak] Staged manifest.yml at version $version"

# --- Locate yak.exe ---------------------------------------------------------
function Resolve-Yak {
    if ($env:YAK_EXE -and (Test-Path $env:YAK_EXE)) { return $env:YAK_EXE }

    $candidates = @(
        'C:\Program Files\Rhino 8\System\Yak.exe',
        'C:\Program Files\Rhino 7\System\Yak.exe',
        '/Applications/Rhino 8.app/Contents/Resources/bin/yak'
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    # No Rhino on this machine (the CI case) - fetch McNeel's standalone yak.
    $cache = Join-Path $RepoRoot '.yak/yak.exe'
    if (Test-Path $cache) { return $cache }
    New-Item -ItemType Directory -Path (Split-Path $cache) -Force | Out-Null
    Write-Host "[yak] No Rhino found; downloading yak.exe..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri 'https://files.mcneel.com/yak/tools/latest/yak.exe' -OutFile $cache
    return $cache
}
$yak = Resolve-Yak
Write-Host "[yak] Using: $yak" -ForegroundColor Cyan

# --- Build the package ------------------------------------------------------
Push-Location $OutputDir
try {
    & $yak build
    if ($LASTEXITCODE -ne 0) { throw "yak build failed with exit code $LASTEXITCODE" }
}
finally { Pop-Location }

$pkg = Get-ChildItem -LiteralPath $OutputDir -Filter '*.yak' | Select-Object -First 1
if (-not $pkg) { throw "yak build produced no .yak file in $OutputDir" }

Write-Host ""
Write-Host "[yak] Package: $($pkg.FullName)" -ForegroundColor Green
Write-Host "[yak] Version: $version" -ForegroundColor Green

# Hand the results to the CI job if we are running in one.
if ($env:GITHUB_OUTPUT) {
    "yak_path=$($pkg.FullName)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "yak_name=$($pkg.Name)"     | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "version=$version"          | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
