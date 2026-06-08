#!/bin/sh
# installer/linux/postinstall.sh
# Update desktop and icon caches after package install.
# Uses command -v guards so this is safe on minimal/headless installs
# where update-desktop-database or gtk-update-icon-cache may not be present.
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications/ || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f /usr/share/icons/hicolor/ || true
fi
