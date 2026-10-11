#!/bin/bash
# PostToolUse (Edit|Write): billige Syntaxprüfung der Dateien, an denen schon CI-Läufe gestorben sind.
# Exit 2 = Meldung an Claude (die Änderung ist schon geschrieben und muss repariert werden).
f=$(jq -r '.tool_input.file_path // empty')
[ -f "$f" ] || exit 0
here=$(cd "$(dirname "$0")" && pwd)
case "$f" in
  */environments/changelog.ts|*/environments/changelog-data.ts)
    out=$(node "$here/check-ts-syntax.cjs" "$f" 2>&1) || {
      echo "Syntaxfehler in $f — meist ein gerades \" im Prosatext (nur „…“ verwenden):" >&2
      echo "$out" >&2; exit 2; } ;;
  */i18n/*.json)
    out=$(node -e 'JSON.parse(require("fs").readFileSync(process.argv[1],"utf8"))' "$f" 2>&1) || {
      echo "Ungültiges JSON in $f:" >&2; echo "$out" | head -5 >&2; exit 2; } ;;
esac
exit 0
