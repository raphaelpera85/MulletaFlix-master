# Syncs the three MulletaFlix repos, rebuilds the local stage, and optionally
# restarts the Windows service that hosts the server.

[CmdletBinding()]
param(
    [string]$Branch = 'master',
    [switch]$Force,
    [switch]$SkipBuild,
    [switch]$RestartService,
    [string]$ServiceName = 'MulletaFlix',
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$serverRepoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $serverRepoRoot
$repos = @(
    [PSCustomObject]@{ Name = 'server'; Path = $serverRepoRoot },
    [PSCustomObject]@{ Name = 'web'; Path = (Join-Path $workspaceRoot 'MulletaFlix-web-master') },
    [PSCustomObject]@{ Name = 'packaging'; Path = (Join-Path $workspaceRoot 'MulletaFlix-packaging-master') }
)

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Git {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    & git -C $Path @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in $Path with exit code $LASTEXITCODE"
    }
}

function Assert-RepoClean {
    param([string]$Path)

    $status = & git -C $Path status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read git status for $Path"
    }

    if ($status -and -not $Force) {
        throw "Working tree is dirty in $Path. Commit or stash changes, or rerun with -Force."
    }
}

foreach ($repo in $repos) {
    if (-not (Test-Path -LiteralPath $repo.Path)) {
        throw "Repository not found: $($repo.Path)"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $repo.Path '.git'))) {
        Write-Host "Skipping $($repo.Name); not a git checkout." -ForegroundColor Yellow
        continue
    }

    Write-Step "Updating $($repo.Name)"
    Assert-RepoClean -Path $repo.Path
    Invoke-Git -Path $repo.Path -Arguments @('fetch', 'origin', $Branch)
    Invoke-Git -Path $repo.Path -Arguments @('reset', '--hard', "origin/$Branch")
}

if (-not $SkipBuild) {
    Write-Step 'Rebuilding stage'
    $buildScript = Join-Path $workspaceRoot 'MulletaFlix-packaging-master\build-stage-and-installer.ps1'
    if (-not (Test-Path -LiteralPath $buildScript)) {
        throw "Build script not found: $buildScript"
    }

    & $buildScript -SkipInstaller -SkipTrayBuild -NoPause
    if ($LASTEXITCODE -ne 0) {
        throw "Stage rebuild failed with exit code $LASTEXITCODE"
    }
}

if ($RestartService) {
    Write-Step "Restarting service $ServiceName"
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Write-Warning "Service not found: $ServiceName"
    }
    else {
        Restart-Service -Name $ServiceName -Force
    }
}

Write-Host ''
Write-Host 'Update finished.' -ForegroundColor Green

if (-not $NoPause) {
    Write-Host ''
    Read-Host 'Pressione ENTER para fechar'
}
