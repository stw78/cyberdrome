#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Bygger Cyberdrome Cockpit för Linux (x64): tester, fristående publicering och Velopack-paket (AppImage + uppdateringar).
# Användning (på Linux eller i WSL):  tools/build-linux.sh [version] [utmapp]
# Resultat: <utmapp>/<version>/  (CyberdromeSimulator.AppImage, uppdateringspaket i <utmapp>/uppdateringar-linux)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:-}"; [ -n "$VERSION" ] || VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
OUT="${2:-$ROOT/out}"
PUBLISH="$ROOT/publish/linux-$VERSION"
FEED="$OUT/uppdateringar-linux"
DOTNET="${DOTNET:-dotnet}"
echo ">> Version $VERSION"
"$DOTNET" test "$ROOT/tests/Cyberdrome.Tests" -c Release --nologo -v q
rm -rf "$PUBLISH"
"$DOTNET" publish "$ROOT/src/Cyberdrome.App/Cyberdrome.App.csproj" -c Release -r linux-x64 --self-contained true \
  -p:Version="$VERSION" -p:DebugType=none -o "$PUBLISH" --nologo -v q
# Produktnamn och version i våra egna filer (samma kontroll som på Windows).
for f in "$PUBLISH"/Cyberdrome*.dll; do grep -a -q "Cyberdrome Cockpit" "$f" || { echo "FEL: $f saknar produktnamnet"; exit 1; }; done
mkdir -p "$FEED" "$OUT/$VERSION"
# Samma Velopack-id som på Windows; kanalen blir "linux" så att uppdateringarna hålls isär.
vpk pack -u CyberdromeSimulator -v "$VERSION" -p "$PUBLISH" -e Cyberdrome \
  --packTitle "Cyberdrome Cockpit" --packAuthors "Jouko Broström" \
  --icon "$ROOT/assets/logo-256.png" -r linux-x64 --channel linux -o "$FEED" ${RELEASE_NOTES:+--releaseNotes "$RELEASE_NOTES"}
cp "$FEED"/*.AppImage "$OUT/$VERSION/" 2>/dev/null || true
cp "$ROOT/LICENSE" "$ROOT/legal/DISCLAIMER.md" "$ROOT/legal/THIRD-PARTY-NOTICES.txt" "$OUT/$VERSION/"
ls -la "$OUT/$VERSION"
echo ">> Klart: $OUT/$VERSION"
