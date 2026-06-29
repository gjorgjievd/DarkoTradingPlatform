#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a complete TradingAgent Windows deployment package (Docker-only target machines).

.DESCRIPTION
    Every run replaces the previous deploy-package folder and ZIP archive.
    Produces a folder deployable on Windows with only Docker Desktop installed.
#>
[CmdletBinding()]
param(
    [switch] $NoPrompt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$HelperPath = Join-Path $RepoRoot 'scripts\deployment\PackageHelpers.ps1'
if (-not (Test-Path -LiteralPath $HelperPath)) {
    throw "Missing helper module: $HelperPath"
}

. $HelperPath

$DeployDir = Join-Path $RepoRoot 'deploy-package'
$PublishDir = Join-Path $RepoRoot 'publish'
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'
$SolutionPath = Join-Path $RepoRoot 'TradingAgent.slnx'
$ProjectPath = Join-Path $RepoRoot 'src\TradingAgent\TradingAgent.csproj'
$NginxSource = Join-Path $RepoRoot 'nginx.conf'
$DevEnvPath = Join-Path $RepoRoot '.env'
$ImageName = 'tradingagent:latest'
$ImageTar = Join-Path $DeployDir 'tradingagent-image.tar'
$BuildTimestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
$ZipName = "TradingAgent_Deployment_$(Get-Date -Format 'yyyyMMdd_HHmm').zip"
$ZipPath = Join-Path $RepoRoot $ZipName
$StartedAt = Get-Date
$totalSteps = 12

function Write-Step {
    param([int]$Number, [int]$Total, [string]$Message)
    Write-Host "[$Number/$Total] $Message" -ForegroundColor Cyan
}

function Invoke-BuildStep {
    param([int]$Number, [int]$Total, [string]$Message, [scriptblock]$Action)
    Write-Step -Number $Number -Total $Total -Message $Message
    try {
        & $Action
    }
    catch {
        Write-Host ''
        Write-Host "ERROR at step $Number/$Total : $Message" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 1
    }
}

Push-Location -LiteralPath $RepoRoot
try {
    Invoke-BuildStep -Number 1 -Total $totalSteps -Message 'Stopping local Docker Compose stack...' -Action {
        if (Get-Command docker -ErrorAction SilentlyContinue) {
            $dockerOk = $false
            $previousPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                & docker info 2>&1 | Out-Null
                $dockerOk = ($LASTEXITCODE -eq 0)
            }
            finally {
                $ErrorActionPreference = $previousPreference
            }

            if ($dockerOk) {
                & docker compose down 2>&1 | Out-Null
            }
            else {
                Write-Host 'Docker engine not running — skipping compose down.' -ForegroundColor Yellow
            }
        }
        else {
            Write-Host 'Docker not found — skipping compose down.' -ForegroundColor Yellow
        }
    }

    Invoke-BuildStep -Number 2 -Total $totalSteps -Message 'Cleaning previous deployment artifacts...' -Action {
        if (Test-Path -LiteralPath $DeployDir) {
            Remove-Item -LiteralPath $DeployDir -Recurse -Force
        }

        if (Test-Path -LiteralPath $PublishDir) {
            Remove-Item -LiteralPath $PublishDir -Recurse -Force
        }

        if (Test-Path -LiteralPath $ArtifactsDir) {
            Remove-Item -LiteralPath $ArtifactsDir -Recurse -Force
        }

        Get-ChildItem -LiteralPath $RepoRoot -Filter 'TradingAgent_Deployment_*.zip' -File -ErrorAction SilentlyContinue |
            Remove-Item -Force

        if (Test-Path -LiteralPath (Join-Path $RepoRoot 'TradingAgentDeploy.zip')) {
            Remove-Item -LiteralPath (Join-Path $RepoRoot 'TradingAgentDeploy.zip') -Force
        }
    }

    Invoke-BuildStep -Number 3 -Total $totalSteps -Message 'Building .NET solution (Release)...' -Action {
        $testProjectPath = Join-Path $RepoRoot 'src\TradingAgent.Tests\TradingAgent.Tests.csproj'

        dotnet build $ProjectPath -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }

        if (Test-Path -LiteralPath $testProjectPath) {
            dotnet build $testProjectPath -c Release --no-restore
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet test project build failed with exit code $LASTEXITCODE"
            }
        }

        dotnet publish $ProjectPath -c Release -o $PublishDir
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }

    Invoke-BuildStep -Number 4 -Total $totalSteps -Message 'Building Docker image...' -Action {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw 'Docker is not installed or not on PATH.'
        }

        docker build -t $ImageName .
        if ($LASTEXITCODE -ne 0) {
            throw "docker build failed with exit code $LASTEXITCODE"
        }
    }

    Invoke-BuildStep -Number 5 -Total $totalSteps -Message 'Creating deploy-package folder...' -Action {
        New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
    }

    Invoke-BuildStep -Number 6 -Total $totalSteps -Message 'Exporting Docker image...' -Action {
        docker save $ImageName -o $ImageTar
        if ($LASTEXITCODE -ne 0) {
            throw "docker save failed with exit code $LASTEXITCODE"
        }
    }

    Invoke-BuildStep -Number 7 -Total $totalSteps -Message 'Generating deployment files and scripts...' -Action {
        if (-not (Test-Path -LiteralPath $NginxSource)) {
            throw "nginx.conf not found at $NginxSource"
        }

        Write-DeploymentPackageFiles -DeployDir $DeployDir -DevEnvPath $DevEnvPath -NginxSource $NginxSource -BuildTimestamp $BuildTimestamp
    }

    Invoke-BuildStep -Number 8 -Total $totalSteps -Message 'Creating ZIP archive...' -Action {
        Compress-Archive -Path $DeployDir -DestinationPath $ZipPath -CompressionLevel Optimal
    }

    Invoke-BuildStep -Number 9 -Total $totalSteps -Message 'Validating deployment package...' -Action {
        Test-DeploymentPackage -DeployDir $DeployDir -ImageName $ImageName
    }

    Invoke-BuildStep -Number 10 -Total $totalSteps -Message 'Running final dotnet build...' -Action {
        dotnet build $ProjectPath -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "Final dotnet build failed with exit code $LASTEXITCODE"
        }
    }

    $imageInspect = docker image inspect $ImageName | ConvertFrom-Json
    $imageId = $imageInspect[0].Id
    $imageSize = [long]$imageInspect[0].Size
    $tarSize = (Get-Item -LiteralPath $ImageTar).Length
    $zipSize = (Get-Item -LiteralPath $ZipPath).Length
    $elapsed = (Get-Date) - $StartedAt

    Write-Step -Number 11 -Total $totalSteps -Message 'Validation complete'
    Write-Step -Number 12 -Total $totalSteps -Message 'Package complete'

    Write-Host ''
    Write-Host 'Deployment package successfully created.' -ForegroundColor Green
    Write-Host ''
    Write-Host "  Deploy folder : $DeployDir"
    Write-Host "  ZIP archive   : $ZipPath"
    Write-Host ("  Image size    : {0:N2} MB (tar: {1:N2} MB)" -f ($imageSize / 1MB), ($tarSize / 1MB))
    Write-Host ("  ZIP size      : {0:N2} MB" -f ($zipSize / 1MB))
    Write-Host ("  Build time    : {0:g}" -f $elapsed)
    Write-Host "  Docker ID     : $imageId"
    Write-Host ''
    Write-Host 'Target machine: extract ZIP, edit .env API keys, run .\start.ps1' -ForegroundColor Yellow

    if (-not $NoPrompt) {
        $openChoice = Read-Host 'Open deployment folder now? [Y/N]'
        if ($openChoice -match '^[Yy]') {
            Invoke-Item -LiteralPath $DeployDir
        }
    }
}
finally {
    Pop-Location
}
