#!/bin/sh
# Holt das Maia-3-Modell fuer das Sparring im Analysebrett (siehe README.md daneben) und prueft es
# per Pruefsumme. Laeuft beim Docker-Build (nur fuer das Projekt `app`, siehe ../../Dockerfile) und
# lokal von Hand: `sh maia-model/fetch.sh` im Ordner src/frontend/app.
#
# Der Pin steht NUR HIER: COMMIT, SHA256 und BYTES. `maia-model.ts` (features/analysis/maia) traegt
# die ersten acht Zeichen der Pruefsumme als `version` und die Groesse als `bytes` — der Browser
# erkennt das Modell an der GROESSE, weil der SPA-Fallback eine fehlende Datei mit 200 und der
# index.html beantwortet. DeploymentConfigTests.Maia_ModelPin_AndDelivery_StayConsistent haelt
# beide Stellen zusammen: wer hier den Pin aendert, aendert dort mit.
#
# POSIX-sh, weil das Build-Image (node:24-alpine) nur busybox hat: kein bash, kein curl — dort
# laedt busybox-wget.
set -eu

COMMIT=a6e52f5c811ee18863cb2f0e81f2433a5b9905de
SHA256=405bf76c15727dad8728b352c06a8f3c1b80fb2760e8d666b32485c63d75b856
BYTES=45683686

NAME=maia3_simplified.onnx
URL="https://raw.githubusercontent.com/CSSLab/maia-platform-frontend/$COMMIT/public/maia3/$NAME"
DIR=$(cd "$(dirname "$0")" && pwd)
TARGET="$DIR/$NAME"
# Erst unter einem anderen Namen laden und nach der Pruefung umbenennen: ein abgebrochener Download
# laege sonst als halbe Datei mit dem richtigen Namen da — und der Angular-Build lieferte sie aus.
PART="$TARGET.part"

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | cut -d ' ' -f 1
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | cut -d ' ' -f 1
  else
    echo "maia-model: weder sha256sum noch shasum gefunden" >&2
    exit 1
  fi
}

size_of() {
  wc -c < "$1" | tr -d ' '
}

if [ -f "$TARGET" ]; then
  if [ "$(sha256_of "$TARGET")" = "$SHA256" ]; then
    echo "maia-model: $NAME liegt schon da (Pruefsumme stimmt), nichts zu tun."
    exit 0
  fi
  echo "maia-model: $NAME hat eine falsche Pruefsumme — wird neu geholt." >&2
  rm -f "$TARGET"
fi

# Der Zwischenstand geht in JEDEM Fall weg (Fehler, falsche Datei, Strg+C). Ein Signal allein
# beendet ein sh-Skript mit gesetztem trap nicht — deshalb dort ausdruecklich `exit`, das loest
# dann den EXIT-trap aus.
trap 'rm -f "$PART"' EXIT
trap 'exit 130' INT TERM
rm -f "$PART"

echo "maia-model: lade $NAME ($BYTES Bytes) von Commit $COMMIT ..."
if command -v curl >/dev/null 2>&1; then
  curl -fsSL --retry 3 -o "$PART" "$URL"
elif command -v wget >/dev/null 2>&1; then
  wget -q -O "$PART" "$URL"
else
  echo "maia-model: weder curl noch wget gefunden" >&2
  exit 1
fi

GOT_BYTES=$(size_of "$PART")
GOT_SHA=$(sha256_of "$PART")
if [ "$GOT_BYTES" != "$BYTES" ] || [ "$GOT_SHA" != "$SHA256" ]; then
  echo "maia-model: FALSCHE DATEI — erwartet $BYTES Bytes / sha256 $SHA256," >&2
  echo "maia-model:                 bekommen $GOT_BYTES Bytes / sha256 $GOT_SHA. Verworfen." >&2
  exit 1
fi

mv "$PART" "$TARGET"
echo "maia-model: $NAME geholt und geprueft."
