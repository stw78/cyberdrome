# Körs som root i Cyberdrome-Android. Skriver nyckel=värde-rader.
CF=/home/cvd/cf
echo "kvm=$([ -e /dev/kvm ] && echo yes || echo no)"
echo "tools=$(command -v launch_cvd >/dev/null 2>&1 || [ -x /usr/lib/cuttlefish-common/bin/launch_cvd ] && echo yes || echo no)"
echo "image=$([ -f $CF/.cyberdrome-ready ] && echo yes || echo no)"
echo "running=$(pgrep -f run_cvd >/dev/null 2>&1 && echo yes || echo no)"
