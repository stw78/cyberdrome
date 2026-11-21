# Körs som användaren cvd. Startar Android Automotive. $CPUS och $MEM sätts av programmet.
set -euo pipefail
CF=$HOME/cf
cd $CF
if pgrep -f run_cvd >/dev/null 2>&1; then echo ">> Android är redan igång"; exit 0; fi
echo ">> Startar Android"
if [ ! -x ./bin/launch_cvd ]; then
  echo "FEL: $CF/bin/launch_cvd saknas. Android-avbilden är inte komplett; tryck Installera Android för att hämta den igen."
  rm -f .cyberdrome-ready
  exit 3
fi
HOME=$CF ./bin/launch_cvd --daemon --start_webrtc=true --cpus="$CPUS" --memory_mb="$MEM" \
  --report_anonymous_usage_stats=n
echo ">> Android startat"
