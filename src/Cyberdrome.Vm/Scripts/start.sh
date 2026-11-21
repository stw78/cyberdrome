# Körs som användaren cvd. Startar Android Automotive.
# $CPUS, $MEM, $X_RES, $Y_RES, $DPI och $BOOT_TIMEOUT sätts av programmet.
set -uo pipefail
CF=$HOME/cf
cd $CF
if pgrep -f run_cvd >/dev/null 2>&1; then echo ">> Android är redan igång"; exit 0; fi
if [ ! -x ./bin/launch_cvd ]; then
  echo "FEL: $CF/bin/launch_cvd saknas. Android-avbilden är inte komplett; tryck Installera Android för att hämta den igen."
  rm -f .cyberdrome-ready
  exit 3
fi
if [ ! -e /dev/kvm ]; then
  echo "FEL: /dev/kvm saknas i WSL2 – nästlad virtualisering är inte påslagen. Se sidan Android för hur du slår på den."
  exit 4
fi
# Rester från en tidigare start som inte gick hela vägen.
HOME=$CF cvd stop >/dev/null 2>&1 || HOME=$CF ./bin/stop_cvd >/dev/null 2>&1 || true
echo ">> Startar Android (${X_RES:-?}x${Y_RES:-?}, ${CPUS} kärnor, ${MEM} MB)"
# --enable_sandbox=false: crosvm:s sandlåda (minijail) fungerar inte i WSL2 – utan den kraschar crosvm direkt med
#   "failed to create a PCI root hub: failed to create proxy device".
# --gpu_mode=guest_swiftshader: ingen GPU-genomföring i WSL2; Android ritar i mjukvara.
EXTRA=""
[ -n "${X_RES:-}" ] && EXTRA="$EXTRA --x_res=$X_RES --y_res=$Y_RES --dpi=$DPI"
HOME=$CF timeout "${BOOT_TIMEOUT:-300}" ./bin/launch_cvd --daemon --start_webrtc=true --cpus="$CPUS" --memory_mb="$MEM" \
  --enable_sandbox=false --gpu_mode=guest_swiftshader --report_anonymous_usage_stats=n $EXTRA
code=$?
LOG=$CF/cuttlefish_runtime/launcher.log
if [ $code -eq 124 ]; then
  echo "FEL: Android startade inte inom ${BOOT_TIMEOUT:-300} sekunder."
  HOME=$CF cvd stop >/dev/null 2>&1 || HOME=$CF ./bin/stop_cvd >/dev/null 2>&1 || true
fi
if [ $code -ne 0 ]; then
  echo "FEL: Android kunde inte starta (kod $code). Det viktigaste ur Cuttlefish-loggen:"
  [ -f "$LOG" ] && grep -aE "Caused by|ERROR crosvm|failed to|VIRTUAL_DEVICE_BOOT_FAILED|No space|Permission denied" "$LOG" | tail -8
  echo "Hela loggen: $LOG"
  exit $code
fi
# adb mot den virtuella bilen (för skärmen i programmet och appinstallation).
./bin/adb connect 127.0.0.1:${ADB_PORT:-6520} >/dev/null 2>&1 || true
echo ">> Android startat"
