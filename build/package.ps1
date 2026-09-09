<#
.SYNOPSIS
    Builds and packages TriggerPoint into a standalone single-file Inno Setup installer.

.DESCRIPTION
    1. Publishes TriggerPoint.UI for win-x64 Release.
    2. Discovers or automatically installs Inno Setup 6 (ISCC.exe).
    3. Compiles installer/TriggerPoint.iss into artifacts/TriggerPointSetup.exe.

.PARAMETER Configuration
    Build configuration (default: "Release").

.PARAMETER AppVersion
    Application version string (default: "1.0.0").

.PARAMETER SkipPublish
    Skip the dotnet publish step if binaries are already compiled.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$AppVersion = "1.0.0",
    [string]$PublishDir = "",
    [string]$ArtifactsDir = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $rootDir

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  TriggerPoint Packaging & Installer Automation" -ForegroundColor Cyan
Write-Host "  Version: $AppVersion | Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Resolve Directories
if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $rootDir "src\TriggerPoint.UI\bin\$Configuration\net9.0-windows\win-x64\publish"
}
if ([string]::IsNullOrWhiteSpace($ArtifactsDir)) {
    $ArtifactsDir = Join-Path $rootDir "artifacts"
}

if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

# Ensure assets/TriggerPoint.ico exists
$assetsIcon = Join-Path $rootDir "assets\TriggerPoint.ico"
$uiIcon = Join-Path $rootDir "src\TriggerPoint.UI\Assets\TriggerPoint.ico"
if (-not (Test-Path $assetsIcon) -and (Test-Path $uiIcon)) {
    $assetsDir = Join-Path $rootDir "assets"
    if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null }
    Copy-Item $uiIcon -Destination $assetsIcon -Force
}

# 2. Locate or Auto-Install Inno Setup Compiler (ISCC.exe)
function Find-ISCC {
    # Check PATH
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd -and (Test-Path $cmd.Source)) {
        return $cmd.Source
    }

    # Standard candidate paths
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 5\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    # Registry lookup
    $regPaths = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($regPath in $regPaths) {
        $found = Get-ItemProperty $regPath -ErrorAction SilentlyContinue | 
            Where-Object { $_.DisplayName -like "*Inno Setup*" -and $_.InstallLocation } | 
            Select-Object -First 1
        if ($found) {
            $exe = Join-Path $found.InstallLocation "ISCC.exe"
            if (Test-Path $exe) {
                return $exe
            }
        }
    }

    return $null
}

$isccPath = Find-ISCC

if (-not $isccPath) {
    Write-Host "[*] Inno Setup compiler (ISCC.exe) not found. Attempting automatic installation via winget..." -ForegroundColor Yellow
    try {
        Start-Process winget -ArgumentList "install --id JRSoftware.InnoSetup -e --silent --accept-source-agreements --accept-package-agreements" -NoNewWindow -Wait
        $isccPath = Find-ISCC
    } catch {
        Write-Warning "winget installation failed: $_"
    }
}

if (-not $isccPath -or -not (Test-Path $isccPath)) {
    Write-Error @"
[!] Inno Setup Compiler (ISCC.exe) could not be located.
Please install Inno Setup 6 using:
    winget install --id JRSoftware.InnoSetup -e
or download it from: https://jrsoftware.org/isdl.php
"@
    exit 1
}

Write-Host "[+] Found Inno Setup Compiler: $isccPath" -ForegroundColor Green

# 3. Publish WPF Application
if (-not $SkipPublish) {
    Write-Host "`n[*] Publishing TriggerPoint.UI (win-x64, $Configuration)..." -ForegroundColor Cyan
    $projectPath = Join-Path $rootDir "src\TriggerPoint.UI\TriggerPoint.UI.csproj"
    
    $publishArgs = @(
        "publish",
        $projectPath,
        "-c", $Configuration,
        "-r", "win-x64",
        "--self-contained", "false",
        "-o", $PublishDir
    )

    Write-Host "    dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[!] dotnet publish failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    Write-Host "[+] Publish completed successfully -> $PublishDir" -ForegroundColor Green
} else {
    Write-Host "`n[*] Skipping dotnet publish (using existing binaries in $PublishDir)" -ForegroundColor DarkYellow
}

# 4. Compile Inno Setup Script
Write-Host "`n[*] Compiling Inno Setup package..." -ForegroundColor Cyan
$issPath = Join-Path $rootDir "installer\TriggerPoint.iss"

$isccArgs = @(
    "/DPublishDir=$PublishDir",
    "/DAppVersion=$AppVersion",
    "/O$ArtifactsDir",
    "/FTriggerPointSetup",
    $issPath
)

Write-Host "    & `"$isccPath`" $($isccArgs -join ' ')" -ForegroundColor DarkGray
& "$isccPath" @isccArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "[!] Inno Setup compilation failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 5. Output Verification
$setupExe = Join-Path $ArtifactsDir "TriggerPointSetup.exe"
if (Test-Path $setupExe) {
    $item = Get-Item $setupExe
    $sizeMb = [math]::Round($item.Length / 1MB, 2)
    $hash = (Get-FileHash $setupExe -Algorithm SHA256).Hash

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host "  TriggerPoint Setup Package Created Successfully!" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "  Output File : $setupExe" -ForegroundColor White
    Write-Host "  File Size   : $sizeMb MB ($($item.Length) bytes)" -ForegroundColor White
    Write-Host "  SHA256 Hash : $hash" -ForegroundColor DarkGray
    Write-Host "============================================================`n" -ForegroundColor Green
} else {
    Write-Error "[!] Expected installer file not found at: $setupExe"
    exit 1
}
