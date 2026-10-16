#!/usr/bin/env bash
###############################################################################
# Test fuer scripts/release-notes.mjs (Release-Text je Versions-Tag, docker.yml
# Job `github-release`) — gegen einen kleinen Changelog im selben Format wie
# changelog-data.ts, ohne Netz.
#
#   ./scripts/tests/test_release_notes.sh
###############################################################################
set -u
here="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="$here/../release-notes.mjs"
dir="$(mktemp -d)"; trap 'rm -rf "$dir"' EXIT
fails=0
fail() { echo "FAIL: $*"; fails=$((fails+1)); }
ok()   { echo "ok:   $*"; }

cat > "$dir/changelog-data.ts" <<'TS'
import { ChangelogEntry } from './changelog';

export const CHANGELOG: ChangelogEntry[] = [
  { version: "1.2.10", date: "2026-10-16", changes: [
    { en: "Ten `code`", de: "Zehn „Zitat“" },
  ] },
  { version: "1.2.9", date: "2026-10-15", changes: [
    { en: "Nine", de: "Neun" },
    { en: "Nine b", de: "Neun b" },
  ] },
  { version: "1.2.8", date: "2026-10-14", changes: [
    { en: "Eight", de: "Acht" },
  ] },
];
TS

out="$(node "$SCRIPT" v1.2.10 v1.2.8 "$dir/changelog-data.ts")" || fail "Aufruf mit vorigem Tag scheitert"
grep -q '^### 1.2.10 (2026-10-16)$' <<<"$out" && grep -q '^### 1.2.9 ' <<<"$out" \
  && ok "Eintraege nach dem vorigen Tag bis einschliesslich Tag" || fail "1.2.10/1.2.9 fehlen"
grep -q '1.2.8' <<<"$out" && fail "der vorige Tag selbst gehoert NICHT dazu" || ok "voriger Tag ausgenommen"
# Zahlen vergleichen, nicht Zeichenketten: 1.2.10 > 1.2.9
grep -q '^- Zehn „Zitat“$' <<<"$out" && grep -q '^- Ten `code`$' <<<"$out" \
  && ok "deutsch + englisch, Sonderzeichen unveraendert" || fail "Texte fehlen oder sind verfaelscht"

out="$(node "$SCRIPT" v1.2.9 "" "$dir/changelog-data.ts")" || fail "Aufruf ohne vorigen Tag scheitert"
[ "$(grep -c '^### ' <<<"$out")" = 1 ] && grep -q '^- Neun b$' <<<"$out" \
  && ok "ohne vorigen Tag nur der eigene Eintrag" || fail "ohne vorigen Tag falsche Auswahl"

if node "$SCRIPT" v9.9.9 v1.2.8 "$dir/changelog-data.ts" >/dev/null 2>&1; then
  fail "Tag ohne Changelog-Eintrag muss scheitern"
else
  ok "Tag ohne Changelog-Eintrag scheitert"
fi

# Und der echte Changelog laesst sich lesen (Format-Aenderung an changelog-data.ts faellt hier auf).
ver="$(grep -oE "APP_VERSION = '[0-9.]+'" "$here/../../src/frontend/app/src/environments/changelog.ts" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')"
node "$SCRIPT" "v$ver" >/dev/null && ok "echter Changelog fuer v$ver lesbar" || fail "echter Changelog nicht lesbar"

[ "$fails" -eq 0 ] || { echo "$fails Fehler"; exit 1; }
echo "alle ok"
