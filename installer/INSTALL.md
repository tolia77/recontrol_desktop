# ReControl Desktop — Install & Smoke-Test Guide

This document covers building the installers from source, installing each artifact on a
clean machine, verifying integrity via SHA-256, and performing the INST-06 smoke-test
checklist on clean VMs.

---

## Contents

1. [Build prerequisites](#build-prerequisites)
2. [Building the installers](#building-the-installers)
3. [Artifact SHA-256 hashes](#artifact-sha-256-hashes)
4. [Installing each artifact](#installing-each-artifact)
5. [INST-06 smoke-test checklist](#inst-06-smoke-test-checklist)
6. [Pass/fail results table](#passfail-results-table)
7. [Troubleshooting](#troubleshooting)

---

## Build prerequisites

### Windows (produces `dist/ReControl-Setup-x64.exe`)

| Prerequisite | Version | Notes |
|---|---|---|
| .NET SDK | 10.x | `dotnet --version` must report `10.x.xxx` |
| Inno Setup 6 | 6.x | Default install path: `C:\Program Files (x86)\Inno Setup 6\ISCC.exe` |
| PowerShell | 5.1+ | Included in Windows 10/11 |
| Internet access | — | `build-windows.ps1` downloads FFmpeg 7.1 LGPL shared DLLs on first run |

The script fetches FFmpeg from the BtbN stable permalink
(`ffmpeg-n7.1-latest-win64-lgpl-shared-7.1.zip`) automatically. On subsequent runs it
skips the download if `avcodec-61.dll` is already present in `ReControl.Desktop/ffmpeg/`.

### Linux (produces `.deb`, `.rpm`, `.tar.gz`)

> **Platform requirement:** `build-linux.sh` must run on a **native Linux x64 host**.
> It cannot cross-compile from Windows. A Docker container, WSL2 with native filesystem
> (not a Windows-mounted drive), or a bare-metal/VM Linux x64 machine all work.
> WSL2 on a Windows-mounted drive (`/mnt/c/...`) is known to break ELF execute bits.

| Prerequisite | Version | Install command |
|---|---|---|
| .NET SDK | 10.x | See [Microsoft docs](https://learn.microsoft.com/dotnet/core/install/linux) |
| Ruby | 2.6+ | `sudo apt install ruby` or `sudo dnf install ruby` |
| fpm | 1.17.0+ | `gem install fpm` |
| rpmbuild | — | `sudo apt install rpm` (Ubuntu/Debian) or `sudo dnf install rpm-build` (Fedora/RHEL) |
| curl | — | Included in most distros |
| Internet access | — | `build-linux.sh` downloads FFmpeg 7.1 LGPL shared `.so` files on first run |

---

## Building the installers

### Windows — one command

Run from a PowerShell prompt inside `recontrol_desktop/`:

```powershell
.\scripts\build-windows.ps1
```

The script:
1. Ensures `.env` exists (copies `.env.prod.example` if absent).
2. Downloads FFmpeg 7.1 LGPL shared DLLs into `ReControl.Desktop/ffmpeg/` (skip if already present).
3. Runs `dotnet publish -c Release -r win-x64 --self-contained -o publish-win`.
4. Verifies self-containment (`coreclr.dll`, `ffmpeg/avcodec-61.dll`, `.env` all present).
5. Compiles the Inno Setup script → `dist/ReControl-Setup-x64.exe`.
6. Prints the path, size, and SHA-256 of the produced `.exe`.

### Linux — one command

Run from a Bash prompt inside `recontrol_desktop/` on a native Linux x64 host:

```bash
bash scripts/build-linux.sh
```

The script:
1. Ensures `.env` exists (copies `.env.prod.example` if absent).
2. Downloads FFmpeg 7.1 LGPL shared `.so` files into `ReControl.Desktop/ffmpeg/` (skip if already present).
3. Runs `dotnet publish -c Release -r linux-x64 --self-contained -o publish-linux`.
4. Applies defensive guards: `chmod +x publish-linux/ReControl.Desktop` (A2); flattens
   `runtimes/linux-x64/native/*.so*` into the publish root (A3 — Skia/HarfBuzz).
5. Verifies `ffmpeg/libavcodec.so*` is present in the publish output.
6. Stages the file tree under `staging/usr/...`.
7. Packages with `fpm -s dir -t deb` → `dist/recontrol-desktop_1.0.0_amd64.deb`
8. Packages with `fpm -s dir -t rpm` → `dist/recontrol-desktop-1.0.0-1.x86_64.rpm`
9. Archives with `tar` → `dist/recontrol-desktop-linux-x64.tar.gz`

---

## Artifact SHA-256 hashes

Verify the hash of each downloaded artifact **before installing** it, especially on
shared or untrusted machines. This is the only integrity check for these unsigned,
admin-elevated installers (threat model T-39-11) — it catches a corrupted/truncated
transfer or tampering before you run the package.

> **These hashes are per-build — they change every time you re-publish.** The values
> below are a snapshot of one specific build; the *durable* part is the verify command
> (below). After any rebuild, regenerate the hashes (`sha256sum dist/*` /
> `Get-FileHash`) and publish the new values alongside that release.

### v1.0.0 — build 2026-06-08

| Artifact | SHA-256 |
|---|---|
| `dist/ReControl-Setup-x64.exe` | `7789EC8E02247A15D085E85965E19AFFC230F5AA299955765E22F68ABB87F82E` |
| `dist/recontrol-desktop_1.0.0_amd64.deb` | `33b18cf06339e2719a3bae55b316b6fc56014d74b68a70f416049e646e6ec1a0` |
| `dist/recontrol-desktop-1.0.0-1.x86_64.rpm` | `ff78f5377755083b6418584f83130ee146bebb536cab19903c5eccdac8498bf0` |
| `dist/recontrol-desktop-linux-x64.tar.gz` | `83b9b8b0d35b25e9ddb61e6a21da704b99c2ed6a2922f193d73dcfc2c30f3863` |

The `.deb`/`.rpm`/`.tar.gz` were built on a native Linux x64 host (Debian trixie) with
.NET 10 SDK + `fpm` + `rpm`; each is ~212 MB (self-contained .NET runtime + FFmpeg `.so`
+ Avalonia native libs). The publish landed Skia/HarfBuzz flat in the app root (no
flatten needed) and produced a valid ELF apphost.

### Verifying on Windows

```powershell
Get-FileHash .\dist\ReControl-Setup-x64.exe -Algorithm SHA256
```

### Verifying on Linux / macOS

```bash
sha256sum dist/recontrol-desktop_1.0.0_amd64.deb
sha256sum dist/recontrol-desktop-1.0.0-1.x86_64.rpm
sha256sum dist/recontrol-desktop-linux-x64.tar.gz
```

---

## Installing each artifact

### Windows — `ReControl-Setup-x64.exe`

1. **Verify the SHA-256** (see table above) before proceeding.
2. Double-click `ReControl-Setup-x64.exe`.
3. **SmartScreen warning:** Because the installer is unsigned, Windows may display
   "Windows protected your PC."
   - Click **"More info"**.
   - Click **"Run anyway"**.
   - This is expected for coursework/unsigned builds. The SHA-256 hash above lets you
     verify the file was not tampered with.
4. Approve the UAC elevation prompt.
5. Follow the Inno Setup wizard (default install directory: `C:\Program Files\ReControl Desktop`).
6. A **"ReControl Desktop"** shortcut appears in the Start Menu and optionally launches
   the app at the end of the wizard.

**Uninstall:** Settings → Apps → "ReControl Desktop" → Uninstall, or use the
Start Menu group "ReControl Desktop → Uninstall ReControl Desktop".

---

### Debian / Ubuntu — `.deb`

```bash
sudo apt install ./recontrol-desktop_1.0.0_amd64.deb
```

This installs:
- App binary and bundled runtime: `/usr/lib/recontrol-desktop/`
- Command-line wrapper: `/usr/bin/recontrol-desktop`
- Desktop launcher: `/usr/share/applications/recontrol-desktop.desktop`
- Icon set: `/usr/share/icons/hicolor/*/apps/recontrol-desktop.png`

Launch from the application menu ("ReControl Desktop") or from a terminal:

```bash
recontrol-desktop
```

**Uninstall:**

```bash
sudo apt remove recontrol-desktop
```

---

### Fedora / RHEL / RPM-based — `.rpm`

```bash
sudo dnf install ./recontrol-desktop-1.0.0-1.x86_64.rpm
```

Or with `rpm` directly:

```bash
sudo rpm -i recontrol-desktop-1.0.0-1.x86_64.rpm
```

Launch from the application menu or from a terminal:

```bash
recontrol-desktop
```

**Uninstall:**

```bash
sudo dnf remove recontrol-desktop
# or
sudo rpm -e recontrol-desktop
```

---

### Tarball fallback — `.tar.gz` (any Linux x64)

Use this if the `.deb`/`.rpm` is not appropriate for your distribution.

```bash
tar -xzf recontrol-desktop-linux-x64.tar.gz
./ReControl.Desktop
```

No install step. The archive is the self-contained publish directory. The binary
requires standard X11/font libraries (`libX11`, `libICE`, `libSM`, `fontconfig`) but
no .NET runtime and no system FFmpeg.

---

## INST-06 smoke-test checklist

Perform these steps on a **clean VM** — one with **no .NET runtime** and **no system
FFmpeg** installed — to verify the self-containment assertion (INST-06).

The bundled backend is: `https://port3003.kokhan.me` (API) and
`wss://port3003.kokhan.me/cable` (WebSocket). Use a test account on that server.

### Per-artifact steps

Each of the four artifacts follows the same five-step flow after installation:

| Step | Action | Pass condition |
|---|---|---|
| 1 | Install the artifact (per instructions above) | Installer completes without error |
| 2 | Launch the app (Start Menu shortcut / `.desktop` entry / terminal command / `./ReControl.Desktop`) | Login screen renders — proves .NET runtime and FFmpeg are bundled and `.env` was loaded |
| 3 | Log in with a test account | Home / device list screen appears |
| 4 | Confirm device registration | The device appears in the device list in the web UI at `https://port3003.kokhan.me` |
| 5 | Start a screen stream from the web UI | At least one video frame renders in the browser (proves WebRTC / FFmpeg pipeline works end-to-end) |

### Windows-specific additional steps

After step 5:

- Open **Settings → Apps**, find "ReControl Desktop", click **Uninstall**.
- Confirm the Start Menu shortcut is removed and the `C:\Program Files\ReControl Desktop`
  directory is deleted (clean uninstall verification).

### Linux .deb / .rpm additional notes

- Verify the `.desktop` entry appears in the application menu **before** manually launching.
- If step 2 fails with a missing shared-library error (`error while loading shared libraries`),
  the fpm `--depends` list may be incomplete — note the missing library name and route back
  to Plan 04 (`build-linux.sh`, fpm `--depends` list) for a fix-and-rebuild.

### Linux .tar.gz additional notes

- No `.desktop` entry is installed — launch via `./ReControl.Desktop` from the extracted
  directory only.
- A missing-library error here indicates a system dep that is absent from the clean VM.
  Note it for the `.deb`/`.rpm` `--depends` list.

---

## Pass/fail results table

Fill in the table below after running the smoke tests on clean VMs.

> **Note:** The three Linux artifacts require `build-linux.sh` to be run first on a
> native Linux x64 host (see [Linux build prerequisites](#linux-produces-deb-rpm-targz)).
> Their rows are marked BUILD DEFERRED until that run is completed.

| Artifact | Clean VM OS | Build status | Install | Launch / Login screen | Device registers | Screen stream | Overall |
|---|---|---|---|---|---|---|---|
| `ReControl-Setup-x64.exe` | Windows 10/11 (no .NET, no FFmpeg) | BUILT | [ ] | [ ] | [ ] | [ ] | PENDING |
| `recontrol-desktop_1.0.0_amd64.deb` | Debian 12 / Ubuntu 24.04 (no .NET, no FFmpeg) | BUILT | [ ] | [ ] | [ ] | [ ] | PENDING |
| `recontrol-desktop-1.0.0-1.x86_64.rpm` | Fedora 40 / RHEL 9 (no .NET, no FFmpeg) | BUILT | [ ] | [ ] | [ ] | [ ] | PENDING |
| `recontrol-desktop-linux-x64.tar.gz` | Any Linux x64 (no .NET, no FFmpeg) | BUILT | [ ] | [ ] | [ ] | [ ] | PENDING |

Mark each cell with **PASS**, **FAIL (description)**, or **SKIP (reason)**.

### On failure

- **No login UI (blank window or crash at startup):** `.env` not loaded or FFmpeg not
  found. Check that `AppContext.BaseDirectory` points to the install dir
  (non-single-file publish). Verify `.env` and `ffmpeg/` are present inside the install
  dir. Rebuild via `build-windows.ps1` / `build-linux.sh` (which verify both).
- **Missing X11/font library on Linux (deb/rpm):** Note the library name and add it to
  fpm `--depends` in `scripts/build-linux.sh`, then rebuild. Route back to Plan 04.
- **Device does not register:** Confirm `port3003.kokhan.me` is reachable from the VM.
  The bundled `.env` contains the prod URLs; no configuration change is needed.
- **No video frame on stream start:** FFmpeg init may have failed. Check the app logs.
  The installed `ffmpeg/` dir must contain all seven libs
  (`avcodec`, `avdevice`, `avfilter`, `avformat`, `avutil`, `swresample`, `swscale`).

---

## Troubleshooting

### SmartScreen blocks the Windows installer

Expected behavior for an unsigned installer. Click **"More info"** then **"Run anyway"**.
Verify the SHA-256 hash before proceeding (see [Artifact SHA-256 hashes](#artifact-sha-256-hashes)).

### "Unable to find FFMPEG binaries" at startup

The app launched from outside its install directory, or the `ffmpeg/` folder is missing
from the install dir. For the Windows installer, uninstall and reinstall via
`ReControl-Setup-x64.exe`. For the tar.gz, ensure you run `./ReControl.Desktop` from
the directory you extracted the archive into (not from a symlink).

### Linux: `error while loading shared libraries: libX11.so.6`

Install the missing system library:

```bash
# Debian/Ubuntu
sudo apt install libx11-6 libice6 libsm6 libfontconfig1

# Fedora/RHEL
sudo dnf install libX11 libICE libSM fontconfig
```

### Linux .deb: postinstall warnings about missing `update-desktop-database`

Not an error. The postinstall script guards both `update-desktop-database` and
`gtk-update-icon-cache` with `command -v ... || true`, so they are safely skipped on
minimal/headless installs. The app still works; the `.desktop` entry may not appear in
the app menu until you log out and back in (or restart the session).

### Build fails: "The system cannot find the path specified" (Windows)

The Inno Setup ISCC.exe was not found. Verify Inno Setup 6 is installed at
`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`. If installed elsewhere, update the path
in `scripts/build-windows.ps1`.

### Linux build: `rpmbuild command not found`

Install `rpm-build` on the Linux build host:

```bash
# Ubuntu/Debian
sudo apt install rpm

# Fedora/RHEL
sudo dnf install rpm-build
```
