<#
.SYNOPSIS
    Builds the Release single-file binaries and packages them into an Inno Setup installer.
#>

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $ScriptDir

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Building Local Social Archive ($Configuration) " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# 1. Publish Single-File Release (Self-Contained for zero external dependencies)
$PublishDir = Join-Path $ScriptDir "dist\LocalSocialArchive-Light"
Write-Host "`n[1/2] Publishing .NET 8 Win-x64 Self-Contained Single-File to: $PublishDir..." -ForegroundColor Yellow
dotnet publish -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed!"
    exit $LASTEXITCODE
}

# 2. Locate Inno Setup Compiler
Write-Host "`n[2/2] Compiling Inno Setup Installer..." -ForegroundColor Yellow
$IsccPaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$IsccExe = $IsccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $IsccExe) {
    Write-Warning "Inno Setup compiler (ISCC.exe) was not found in standard paths."
    Write-Warning "Please install Inno Setup 6 from https://jrsoftware.org/isdl.php"
    exit 1
}

Write-Host "Found ISCC at: $IsccExe" -ForegroundColor DarkGray
$InstallerScript = Join-Path $ScriptDir "installer.iss"
& $IsccExe $InstallerScript

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed!"
    exit $LASTEXITCODE
}

$SetupExe = Join-Path $ScriptDir "dist\installer\LocalSocialArchive-Setup.exe"
if (Test-Path $SetupExe) {
    $SizeMB = (Get-Item $SetupExe).Length / 1MB
    Write-Host "`nInstaller created successfully!" -ForegroundColor Green
    Write-Host "Path: $SetupExe ($("{0:F2}" -f $SizeMB) MB)`n" -ForegroundColor Green
}

# 3. Create Portable Zip
Write-Host "`n[3/3] Creating Portable Zip package..." -ForegroundColor Yellow
$PortableZip = Join-Path $ScriptDir "dist\installer\LocalSocialArchive-Portable.zip"
if (Test-Path $PortableZip) { Remove-Item $PortableZip -Force }
$ExePath = Join-Path $PublishDir "LocalSocialArchive.exe"
Compress-Archive -Path $ExePath -DestinationPath $PortableZip -CompressionLevel Optimal
if (Test-Path $PortableZip) {
    $ZipSizeMB = (Get-Item $PortableZip).Length / 1MB
    Write-Host "Portable Zip created: $PortableZip ($("{0:F2}" -f $ZipSizeMB) MB)`n" -ForegroundColor Green
}
