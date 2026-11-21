# Körs som användaren cvd. Startar Android Automotive. $CPUS och $MEM sätts av programmet.
set -euo pipefail
CF=$HOME/cf
cd $CF
if pgrep -f run_cvd >/dev/null 2>&1; then echo ">> Android är redan igång"; exit 0; fi
echo ">> Startar Android"
HOME=$CF ./bin/launch_cvd --daemon --start_webrtc=true --cpus="$CPUS" --memory_mb="$MEM" \
  --report_anonymous_usage_stats=n
echo ">> Android startat"
