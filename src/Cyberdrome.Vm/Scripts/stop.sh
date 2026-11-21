# Körs som användaren cvd. Stänger av Android.
CF=$HOME/cf
cd $CF 2>/dev/null || exit 0
HOME=$CF ./bin/stop_cvd || true
echo ">> Android avstängt"
