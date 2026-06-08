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
# Retry-with-backoff rides out transient GitHub/CDN 5xx (e.g. 504 Gateway Time-out).
# Windows PowerShell 5.1 Invoke-WebRequest has no -MaximumRetryCount, so retry manually.
$maxAttempts = 5
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    try {
        Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
        break
    } catch {
        if ($attempt -eq $maxAttempts) { throw "Download failed after $maxAttempts attempts: $($_.Exception.Message)" }
        $delay = 3 * $attempt
        Write-Host "  Attempt $attempt failed ($($_.Exception.Message)); retrying in $delay s..."
        Start-Sleep -Seconds $delay
    }
}

# Sanity-check the archive before extracting: guards against a saved HTML error page.
if (-not (Test-Path $archive) -or (Get-Item $archive).Length -lt 102400) {
    throw "Downloaded file is not a valid archive (got $((Get-Item $archive -ErrorAction SilentlyContinue).Length) bytes) -- aborting"
}

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
