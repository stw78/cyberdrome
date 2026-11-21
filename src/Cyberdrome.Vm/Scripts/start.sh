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
# --enable_sandbox=false: crosvm:s sandlåda (minijail) fungerar inte i WSL2 – utan den kraschar crosvm direkt med
#   "failed to create a PCI root hub: failed to create proxy device".
# Grafik: gfxstream ritar med datorns grafikkort via WSL2:s /dev/dxg och Mesa D3D12 (GALLIUM_DRIVER=d3d12).
#   Går det inte används guest_swiftshader (mjukvara, långsamt men fungerar alltid).
EXTRA=""
# Skärmstorleken från Inställningar (bilens bilder ignorerar --x_res/--y_res; --display0 fungerar).
[ -n "${X_RES:-}" ] && EXTRA="$EXTRA --display0=width=$X_RES,height=$Y_RES,dpi=$DPI"
MODES="guest_swiftshader"
if [ -e /dev/dxg ] && [ "${GPU:-host}" != "software" ]; then MODES="gfxstream guest_swiftshader"; fi
code=1
for MODE in $MODES; do
  echo ">> Startar Android (${X_RES:-?}x${Y_RES:-?}, ${CPUS} kärnor, ${MEM} MB, grafik $MODE)"
  GALLIUM_DRIVER=d3d12 HOME=$CF timeout "${BOOT_TIMEOUT:-300}" ./bin/launch_cvd --daemon --start_webrtc=true --cpus="$CPUS" --memory_mb="$MEM" \
    --enable_sandbox=false --gpu_mode=$MODE --report_anonymous_usage_stats=n $EXTRA
  code=$?
  [ $code -eq 0 ] && break
  [ $code -eq 124 ] && break
  echo ">> Grafikläget $MODE fungerade inte (kod $code) – provar nästa."
  HOME=$CF cvd stop >/dev/null 2>&1 || HOME=$CF ./bin/stop_cvd >/dev/null 2>&1 || true
done
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
