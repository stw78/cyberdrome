# Körs som root i Cyberdrome-Android. Skriver nyckel=värde-rader.
CF=/home/cvd/cf
echo "kvm=$([ -e /dev/kvm ] && echo yes || echo no)"
# Nya Cuttlefish-paket (1.x) har verktyget "cvd" i stället för launch_cvd; launch_cvd följer med Android-avbilden.
has_tool() { command -v cvd || command -v launch_cvd || [ -x /usr/lib/cuttlefish-common/bin/cvd ] || [ -x /usr/lib/cuttlefish-common/bin/launch_cvd ]; }
echo "tools=$(has_tool >/dev/null 2>&1 && id cvd >/dev/null 2>&1 && echo yes || echo no)"
echo "image=$([ -f $CF/.cyberdrome-ready ] && echo yes || echo no)"
echo "running=$(pgrep -f run_cvd >/dev/null 2>&1 && echo yes || echo no)"
