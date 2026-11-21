# Körs som root i Cyberdrome-Android. Installerar Cuttlefish-verktygen.
# @@STEP-raderna talar om för programmet hur långt vi kommit (0–1).
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
echo "@@STEP 0.05 Uppdaterar paketlistor"
echo ">> Uppdaterar paketlistor"
apt-get update -q
echo "@@STEP 0.25 Installerar grundverktyg"
apt-get install -y -q curl ca-certificates unzip
if [ ! -f /etc/apt/sources.list.d/cuttlefish.list ]; then
  echo "@@STEP 0.40 Lägger till Cuttlefish-paketkällan"
  echo ">> Lägger till Cuttlefish-paketkällan"
  curl -fsSL https://us-apt.pkg.dev/doc/repo-signing-key.gpg -o /etc/apt/trusted.gpg.d/artifact-registry.asc
  chmod a+r /etc/apt/trusted.gpg.d/artifact-registry.asc
  echo "deb https://us-apt.pkg.dev/projects/android-cuttlefish-artifacts android-cuttlefish main" > /etc/apt/sources.list.d/cuttlefish.list
  apt-get update -q
fi
echo "@@STEP 0.55 Installerar Cuttlefish"
echo ">> Installerar Cuttlefish"
apt-get install -y -q cuttlefish-base cuttlefish-user
echo "@@STEP 0.90 Ställer in användare och systemd"
id cvd >/dev/null 2>&1 || useradd -m -s /bin/bash cvd
for g in kvm cvdnetwork render; do getent group $g >/dev/null && usermod -aG $g cvd; done
# Se till att WSL kör systemd (behövs för Cuttlefish nätverk) och att cvd är standardanvändare.
if ! grep -q "systemd=true" /etc/wsl.conf 2>/dev/null; then
  printf "[boot]\nsystemd=true\n[user]\ndefault=cvd\n" > /etc/wsl.conf
fi
if [ ! -e /dev/kvm ]; then
  echo "VARNING: /dev/kvm saknas. Nästlad virtualisering är inte på i WSL."
fi
echo "@@STEP 1 Verktygen är installerade"
echo ">> Klart"
