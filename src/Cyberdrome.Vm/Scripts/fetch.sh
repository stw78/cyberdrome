# Körs som användaren cvd. Laddar ner Android-avbilden. $BUILD sätts av programmet.
set -euo pipefail
CF=$HOME/cf
mkdir -p $CF && cd $CF
rm -f .cyberdrome-ready
echo ">> Hämtar Android ($BUILD). Det kan ta lång tid första gången (flera GB)."

# Förlopp: skriv ut hur mycket som laddats ner var 2:a sekund (@@BYTES).
# Vi mäter hur mycket disken fyllts sedan start (plus det som redan låg i $CF),
# så att det fungerar oavsett var nedladdningsverktyget mellanlagrar filerna.
CF_START=$(du -sb "$CF" 2>/dev/null | cut -f1 || echo 0)
USED_START=$(df --output=used -B1 "$CF" | tail -1 | tr -d ' ')
report() {
  while true; do
    USED_NOW=$(df --output=used -B1 "$CF" | tail -1 | tr -d ' ')
    echo "@@BYTES $(( CF_START + USED_NOW - USED_START ))"
    sleep 2
  done
}
report &
REPORTER=$!
trap 'kill $REPORTER 2>/dev/null || true' EXIT

if command -v cvd >/dev/null 2>&1; then
  cvd fetch --default_build="$BUILD" --target_directory="$CF"
else
  fetch_cvd -default_build="$BUILD" -directory="$CF"
fi
kill $REPORTER 2>/dev/null || true
echo "@@BYTES $(du -sb "$CF" | cut -f1)"
touch .cyberdrome-ready
echo ">> Klart"
