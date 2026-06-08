# scripts/build-windows.ps1
# One-command Windows build: ensure .env -> fetch FFmpeg -> publish win-x64 --self-contained -> ISCC
#
# Usage (from recontrol_desktop/):
#   powershell -ExecutionPolicy Bypass -File scripts\build-windows.ps1
#
# Prerequisites:
#   - dotnet SDK 10+ installed
#   - Inno Setup 6 installed at default location (or in ProgramFiles(x86))
#   - Internet access for FFmpeg fetch (if ffmpeg/ DLLs not already present)
#
# Output: dist\ReControl-Setup-x64.exe

$ErrorActionPreference = 'Stop'

# Resolve repo root relative to this script's location (scripts/ -> recontrol_desktop/)
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

Write-Host "=== ReControl Desktop Windows Build ==="
Write-Host "Repo root: $RepoRoot"

# Step 1: Ensure .env exists
$envFile    = Join-Path $RepoRoot "ReControl.Desktop\.env"
$envExample = Join-Path $RepoRoot "ReControl.Desktop\.env.prod.example"

if (-not (Test-Path $envFile)) {
    if (-not (Test-Path $envExample)) {
        throw ".env.prod.example not found at $envExample -- cannot proceed without .env"
    }
    Write-Host "`n[1/5] .env not found -- copying .env.prod.example -> .env"
    Copy-Item $envExample $envFile
} else {
    Write-Host "`n[1/5] .env already present -- skipping copy"
}

# Step 2: Fetch FFmpeg DLLs (if not already present)
$ffmpegDir   = Join-Path $RepoRoot "ReControl.Desktop\ffmpeg"
$avcodecDll  = Join-Path $ffmpegDir "avcodec-61.dll"

if (-not (Test-Path $avcodecDll)) {
    Write-Host "`n[2/5] FFmpeg DLLs not found -- invoking fetch-ffmpeg-win.ps1"
    & (Join-Path $PSScriptRoot "fetch-ffmpeg-win.ps1")
} else {
    Write-Host "`n[2/5] FFmpeg DLLs already present (avcodec-61.dll found) -- skipping fetch"
}

# Step 3: dotnet publish (self-contained, win-x64, Release)
$publishDir = Join-Path $RepoRoot "publish-win"
$csprojPath = Join-Path $RepoRoot "ReControl.Desktop\ReControl.Desktop.csproj"

Write-Host "`n[3/5] Publishing self-contained win-x64 release to: $publishDir"
dotnet publish $csprojPath -c Release -r win-x64 --self-contained -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Step 4: Verify publish output is complete
Write-Host "`n[4/5] Verifying publish output..."

$requiredFiles = @(
    (Join-Path $publishDir "ReControl.Desktop.exe"),
    (Join-Path $publishDir "coreclr.dll"),
    (Join-Path $publishDir "ffmpeg\avcodec-61.dll"),
    (Join-Path $publishDir ".env")
)

$allGood = $true
foreach ($f in $requiredFiles) {
    if (Test-Path $f) {
        Write-Host "  [OK] $f"
    } else {
        Write-Host "  [MISSING] $f"
        $allGood = $false
    }
}
if (-not $allGood) {
    throw "Publish output is incomplete -- see MISSING entries above. Ensure the csproj has ffmpeg ItemGroup and .env None Update."
}

# Step 5: Run ISCC to produce the installer
Write-Host "`n[5/5] Running Inno Setup compiler (ISCC)..."

# Locate ISCC.exe defensively
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isdl.php"
}
Write-Host "  ISCC: $iscc"

$issFile = Join-Path $RepoRoot "installer\windows\installer.iss"
$distDir = Join-Path $RepoRoot "dist"

# Ensure dist/ exists
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# Run ISCC from recontrol_desktop/ so relative paths in .iss resolve correctly
Push-Location $RepoRoot
try {
    & $iscc $issFile
    if ($LASTEXITCODE -ne 0) { throw "ISCC.exe exited with code $LASTEXITCODE" }
} finally {
    Pop-Location
}

# Confirm artifact
$installerPath = Join-Path $distDir "ReControl-Setup-x64.exe"
if (-not (Test-Path $installerPath)) {
    throw "Expected installer not found at $installerPath after ISCC run"
}

$hash = (Get-FileHash $installerPath -Algorithm SHA256).Hash
$sizeKB = [math]::Round((Get-Item $installerPath).Length / 1024)

Write-Host "`n=== Build Complete ==="
Write-Host "Installer : $installerPath"
Write-Host "Size      : ${sizeKB} KB"
Write-Host "SHA-256   : $hash"
Write-Host ""
Write-Host "SmartScreen note (D-10 / unsigned installer):"
Write-Host "  If Windows blocks the installer, click 'More info' -> 'Run anyway'."
Write-Host "  Verify the SHA-256 above before distributing."
