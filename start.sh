#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=========================================================="
echo "  CPM Hardware-Check (Next.js + C# LibreHardwareMonitor)"
echo "=========================================================="

# Eigenes Temp-Verzeichnis setzen, damit /tmp/NuGetScratch (von sudo) nie blockiert
LOCAL_TMP="$SCRIPT_DIR/backend/artifacts/tmp"
mkdir -p "$LOCAL_TMP"

# Falls mit sudo ausgeführt wurde: alte Root-Sperrdateien in /tmp bereinigen & Rechte setzen
if [ -n "$SUDO_USER" ]; then
  rm -rf /tmp/NuGetScratch* /var/tmp/NuGetScratch* 2>/dev/null || true
  rm -rf "$SCRIPT_DIR/backend/.obj_legacy" "$SCRIPT_DIR/backend/.bin_legacy" 2>/dev/null || true
  chown -R "$SUDO_USER":staff "$SCRIPT_DIR" 2>/dev/null || true
  chown -R "$SUDO_USER":staff "/Users/$SUDO_USER/.nuget" 2>/dev/null || true
fi

# Alte Instanz beenden falls aktiv
pkill -f "HardwareCheck.Api" 2>/dev/null || true

echo "[1/2] Baue Next.js Frontend..."
cd "$SCRIPT_DIR/frontend"
if [ -n "$SUDO_USER" ]; then
  sudo -u "$SUDO_USER" npm run build
else
  npm run build
fi

echo "[2/2] Starte C# ASP.NET Core Server (lauscht auf Port 8085 & 5085)..."
echo "      -> Web-Dashboard: http://localhost:8085"
echo "      -> LHM API Baum:  http://localhost:8085/data.json"
cd "$SCRIPT_DIR/backend"

RESTORE_FLAG=""
if [ -f "$SCRIPT_DIR/backend/artifacts/obj/project.assets.json" ]; then
  RESTORE_FLAG="--no-restore"
fi

if [ -n "$SUDO_USER" ]; then
  sudo -u "$SUDO_USER" env PATH="/opt/homebrew/bin:$PATH" TMPDIR="$LOCAL_TMP" NUGET_SCRATCH="$LOCAL_TMP/nuget" dotnet run $RESTORE_FLAG
else
  env PATH="/opt/homebrew/bin:$PATH" TMPDIR="$LOCAL_TMP" NUGET_SCRATCH="$LOCAL_TMP/nuget" dotnet run $RESTORE_FLAG
fi
