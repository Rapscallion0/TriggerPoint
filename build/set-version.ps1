<#
.SYNOPSIS
    Sets and synchronizes the TriggerPoint application version across all project files.

.DESCRIPTION
    Updates Directory.Build.props (the single source of truth for all .NET projects),
    the Inno Setup installer fallback definition, and documentation badges.

.PARAMETER Version
    The new semantic version (e.g., "2.0.2").

.EXAMPLE
    .\build\set-version.ps1 -Version "2.0.2"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $rootDir

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  TriggerPoint Version Synchronization Utility" -ForegroundColor Cyan
Write-Host "  Target Version: $Version" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Update Directory.Build.props (Canonical Source of Truth)
$propsPath = Join-Path $rootDir "Directory.Build.props"
if (Test-Path $propsPath) {
    [xml]$propsXml = Get-Content $propsPath
    $oldVersion = $propsXml.Project.PropertyGroup.Version
    $propsXml.Project.PropertyGroup.Version = $Version
    $propsXml.Project.PropertyGroup.AssemblyVersion = "$Version.0"
    $propsXml.Project.PropertyGroup.FileVersion = "$Version.0"
    $propsXml.Save($propsPath)
    Write-Host "[+] Directory.Build.props updated: $oldVersion -> $Version" -ForegroundColor Green
} else {
    Write-Error "[!] Directory.Build.props not found at $propsPath"
}

# 2. Update installer/TriggerPoint.iss fallback definition
$issPath = Join-Path $rootDir "installer\TriggerPoint.iss"
if (Test-Path $issPath) {
    $issContent = Get-Content $issPath -Raw
    $newIssContent = [System.Text.RegularExpressions.Regex]::Replace(
        $issContent,
        '(#define\s+AppVersion\s+)"[^"]+"',
        "`${1}`"$Version`""
    )
    if ($newIssContent -ne $issContent) {
        Set-Content -Path $issPath -Value $newIssContent -NoNewline
        Write-Host "[+] installer/TriggerPoint.iss fallback AppVersion updated -> $Version" -ForegroundColor Green
    }
}

# 3. Update README.md version badge and packaging snippet
$readmePath = Join-Path $rootDir "README.md"
if (Test-Path $readmePath) {
    $readmeContent = Get-Content $readmePath -Raw
    $readmeContent = [System.Text.RegularExpressions.Regex]::Replace(
        $readmeContent,
        'badge/Version-v\d+\.\d+\.\d+-blue\.svg',
        "badge/Version-v$Version-blue.svg"
    )
    $readmeContent = [System.Text.RegularExpressions.Regex]::Replace(
        $readmeContent,
        'package\.ps1\s+-AppVersion\s+"[^"]+"',
        "package.ps1 -AppVersion `"$Version`""
    )
    Set-Content -Path $readmePath -Value $readmeContent -NoNewline
    Write-Host "[+] README.md version references updated -> $Version" -ForegroundColor Green
}

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host "  Version successfully synchronized to: $Version" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
