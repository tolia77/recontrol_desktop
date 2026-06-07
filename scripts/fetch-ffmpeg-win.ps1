# scripts/fetch-ffmpeg-win.ps1
# Downloads BtbN FFmpeg 7.1 LGPL shared win64 and extracts DLLs to ReControl.Desktop\ffmpeg\
# Run from any directory; all paths are resolved relative to this script's location.
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\fetch-ffmpeg-win.ps1

$ErrorActionPreference = 'Stop'

$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-lgpl-shared-7.1.zip"
# Assert LGPL build: filename must contain "lgpl-shared" (Pitfall 7 guard)
if ($url -notmatch "lgpl-shared") { throw "URL does not contain lgpl-shared -- aborting to prevent GPL build" }

$archive  = Join-Path $env:TEMP "ffmpeg-win64.zip"
$expanded = Join-Path $env:TEMP "ffmpeg-win64-expanded"
$destDir  = Join-Path $PSScriptRoot "..\ReControl.Desktop\ffmpeg"
$destDir  = [System.IO.Path]::GetFullPath($destDir)

Write-Host "Downloading FFmpeg 7.1 LGPL shared (win64)..."
Write-Host "  URL: $url"
Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing

Write-Host "Extracting DLLs..."
if (Test-Path $expanded) { Remove-Item $expanded -Recurse -Force }
Expand-Archive -Path $archive -DestinationPath $expanded -Force

# DLLs are in <archive-root>/bin/*.dll
$binDir = Get-ChildItem $expanded -Recurse -Filter "bin" -Directory | Select-Object -First 1
if (-not $binDir) { throw "Could not locate bin/ directory inside the FFmpeg archive at $expanded" }
Write-Host "  Found bin/ at: $($binDir.FullName)"

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
Copy-Item "$($binDir.FullName)\*.dll" $destDir -Force

Write-Host "FFmpeg DLLs copied to $destDir"
Write-Host "  Files: $(Get-ChildItem $destDir -Filter '*.dll' | Select-Object -ExpandProperty Name | Sort-Object)"

Remove-Item $archive, $expanded -Recurse -Force
Write-Host "Cleanup complete."
