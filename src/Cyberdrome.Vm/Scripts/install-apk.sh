# Körs som användaren cvd. $APK = sökväg i Linux-format, $ADB_PORT = adb-port.
set -euo pipefail
CF=$HOME/cf
ADB=$CF/bin/adb
[ -x "$ADB" ] || ADB=adb
"$ADB" connect "127.0.0.1:$ADB_PORT" >/dev/null
echo ">> Installerar $(basename "$APK")"
"$ADB" -s "127.0.0.1:$ADB_PORT" install -r -g "$APK"
echo ">> Klart"
