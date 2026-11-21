# Körs som användaren cvd. Stänger av Android.
CF=$HOME/cf
cd $CF 2>/dev/null || exit 0
# Nya Cuttlefish (cvd) håller reda på sina instanser; "cvd stop" stänger dem rätt. Äldre: stop_cvd.
HOME=$CF cvd stop 2>/dev/null || HOME=$CF ./bin/stop_cvd || true
echo ">> Android avstängt"
