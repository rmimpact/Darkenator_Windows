<#
.SYNOPSIS
    Builds Darkenator and publishes a portable single-file exe to .\dist.

.PARAMETER FrameworkDependent
    Produce a ~2 MB exe that needs the .NET 6 Desktop Runtime installed, instead of the
    default ~63 MB self-contained build that runs on any Windows 10/11 machine as-is.

.PARAMETER SkipTests
    Skip the solar-calculator and icon checks.
#>
[CmdletBinding()]
param(
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\Darkenator\Darkenator.csproj'
$dist = Join-Path $root 'dist'

Write-Host 'Generating the application icon...' -ForegroundColor Cyan
& (Join-Path $root 'tools\make-icon.ps1')

if (-not $SkipTests) {
    Write-Host 'Running checks...' -ForegroundColor Cyan
    dotnet run --project (Join-Path $root 'tests\Darkenator.Tests\Darkenator.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'checks failed' }
}

Write-Host 'Publishing...' -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

$args = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true',
    '/p:EnableCompressionInSingleFile=true',
    '/p:DebugType=None',
    '-o', $dist
)
$args += if ($FrameworkDependent) { '--self-contained:false' } else { '--self-contained:true' }

dotnet @args
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

$exe = Join-Path $dist 'Darkenator.exe'
$size = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
