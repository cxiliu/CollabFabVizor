<#
.SYNOPSIS
    Publishes a release snapshot of this repo to the public institute repo.

.DESCRIPTION
    Copies an explicit ALLOWLIST of paths from the private dev repo into a
    clone of UniStuttgart-ICD/VizorGH, commits the result as one snapshot on
    top of the existing public history, and tags it.

    Why an allowlist and not .gitignore: an ignore rule only guards files that
    are not already tracked. An allowlist can only ever publish what is named
    below, so a stray CLAUDE.md, plan file or .3dm can never leak by accident.

    Only files that are TRACKED in the dev repo are considered, so build
    output (bin/, obj/) is excluded automatically.

    DRY RUN BY DEFAULT. Nothing is pushed unless you pass -Push.

.PARAMETER PublicRepo
    Clone URL of the public repo.

.PARAMETER WorkDir
    Scratch directory for the clone. Defaults to a temp folder.

.PARAMETER Push
    Actually push the snapshot and tag. Without this the script stages the
    commit locally and prints the diff summary for review.

.EXAMPLE
    pwsh packaging/publish-public.ps1
    pwsh packaging/publish-public.ps1 -Push
#>
[CmdletBinding()]
param(
    [string] $PublicRepo = 'https://github.com/UniStuttgart-ICD/VizorGH.git',
    [string] $WorkDir,
    [switch] $Push
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot

# --- What is allowed to become public --------------------------------------
# Top-level paths. Anything not named here is never copied.
$Allow = @(
    'CHANGELOG.md'
    'Directory.Build.props'
    'Directory.Build.targets'
    'Examples'
    'FAQ.md'
    'LICENSE'
    'README.md'
    'Virtual_robot'
    'Vizor'
    'Vizor.sln'
    'VizorLibs'
    'docs'
    'packages'
    'packaging'
)

# Belt and braces: even inside an allowed path, never publish these.
$DenyPattern = '(^|/)(CLAUDE\.md|\.claude/|\.github/|WIP/|Archived/|Icons/)|\.3dm|-plan\.md$|\.log$'

# --- Version from the single source of truth --------------------------------
[xml] $props = Get-Content -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props') -Raw
$node = $props.SelectSingleNode('/Project/PropertyGroup/VizorVersion')
if (-not $node) { throw 'Could not read <VizorVersion> from Directory.Build.props' }
$version = $node.InnerText.Trim()
$tag = "v$version"
Write-Host "[publish] Version: $version (tag $tag)" -ForegroundColor Cyan

# --- Refuse to publish a dirty tree ----------------------------------------
Push-Location $RepoRoot
try {
    $dirty = & git status --porcelain
    if ($dirty) {
        throw "Working tree is not clean. Commit or stash first:`n$($dirty -join "`n")"
    }
    $sourceSha = (& git rev-parse HEAD).Trim()

    # --- Collect the files to publish --------------------------------------
    $files = & git ls-files -- $Allow |
        Where-Object { $_ -and ($_ -notmatch $DenyPattern) }
    if (-not $files) { throw 'Allowlist matched no tracked files.' }
    Write-Host "[publish] $($files.Count) files selected" -ForegroundColor Cyan
}
finally { Pop-Location }

# --- Clone the public repo --------------------------------------------------
if (-not $WorkDir) {
    $WorkDir = Join-Path ([IO.Path]::GetTempPath()) "vizor-public-$(Get-Random)"
}
if (Test-Path $WorkDir) { Remove-Item -LiteralPath $WorkDir -Recurse -Force }
Write-Host "[publish] Cloning $PublicRepo" -ForegroundColor Cyan
& git clone --quiet $PublicRepo $WorkDir
if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE" }
$WorkDir = (Resolve-Path -LiteralPath $WorkDir).Path

# --- Replace the public tree wholesale -------------------------------------
# Delete everything the public repo currently tracks, then lay down the new
# snapshot. This makes deletions propagate, not just additions.
Push-Location $WorkDir
try {
    & git rm -r --quiet --cached . | Out-Null
    Get-ChildItem -LiteralPath $WorkDir -Force |
        Where-Object { $_.Name -ne '.git' } |
        Remove-Item -Recurse -Force
}
finally { Pop-Location }

foreach ($f in $files) {
    $src = Join-Path $RepoRoot $f
    $dst = Join-Path $WorkDir  $f
    New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force
}

# The public repo keeps its own .gitignore, which is not tracked in the dev
# repo under the same content. Carry the dev one over so builds behave the same.
Copy-Item -LiteralPath (Join-Path $RepoRoot '.gitignore') `
          -Destination (Join-Path $WorkDir '.gitignore') -Force

# --- Safety scan ------------------------------------------------------------
Push-Location $WorkDir
try {
    $leaked = Get-ChildItem -Recurse -File -Force |
        ForEach-Object { $_.FullName.Substring($WorkDir.Length + 1).Replace([char]92, [char]47) } |
        Where-Object { -not $_.StartsWith('.git/') } |
        Where-Object { $_ -match $DenyPattern }
    if ($leaked) {
        throw "Refusing to publish. Denylisted files present:`n$($leaked -join "`n")"
    }

    # --force: the allowlist above is the authority on what gets published, not
    # .gitignore. Without this the copied-in .gitignore gets a second vote and
    # silently DELETES tracked files it happens to match (it did exactly that
    # to the vendored packages/ folder).
    & git add -A --force
    $staged = & git status --porcelain
    if (-not $staged) {
        Write-Host '[publish] Public repo already matches this snapshot. Nothing to do.' -ForegroundColor Yellow
        return
    }

    & git -c user.name='Vizor Release' -c user.email='xiliu.yang@icd.uni-stuttgart.de' `
        commit --quiet -m "Release $tag" -m "Snapshot of VizorGH $version.`n`nSource commit: $sourceSha"
    & git tag -f $tag

    Write-Host ''
    Write-Host '[publish] Snapshot staged:' -ForegroundColor Green
    & git show --stat --oneline HEAD | Select-Object -First 25

    if ($Push) {
        Write-Host ''
        Write-Host "[publish] Pushing to $PublicRepo ..." -ForegroundColor Cyan
        & git push origin HEAD:master
        if ($LASTEXITCODE -ne 0) { throw "git push failed with exit code $LASTEXITCODE" }
        & git push -f origin $tag
        if ($LASTEXITCODE -ne 0) { throw "git push --tags failed with exit code $LASTEXITCODE" }
        Write-Host "[publish] Published $tag" -ForegroundColor Green
    }
    else {
        Write-Host ''
        Write-Host '[publish] DRY RUN - nothing pushed. Re-run with -Push to publish.' -ForegroundColor Yellow
        Write-Host "[publish] Review the clone at: $WorkDir" -ForegroundColor Yellow
    }
}
finally { Pop-Location }
