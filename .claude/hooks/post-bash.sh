#!/bin/bash
# PostToolUse (Bash): nach `dotnet ef migrations add <Name>` prüfen, ob Up() leer ist
# (typisch nach einem Rebase, wenn der Schnappschuss die Änderung schon enthält).
cmd=$(jq -r '.tool_input.command // empty')
name=$(grep -oP 'migrations add\s+\K[A-Za-z0-9_]+' <<<"$cmd") || exit 0
repo=$(cd "$(dirname "$0")/../.." && pwd)
for m in $(find "$repo/src/api/RookHub.Api/Migrations" -maxdepth 1 -name "*_${name}.cs" ! -name '*.Designer.cs' -mmin -15 2>/dev/null); do
  if ! sed -n '/void Up(/,/void Down(/p' "$m" | grep -q 'migrationBuilder\.'; then
    echo "Migration $(basename "$m") hat ein LEERES Up() — vermutlich enthielt der Schnappschuss die Änderung schon (nach Rebase). Entfernen, Schnappschuss zurücksetzen, neu erzeugen (.claude/rules/migrations.md)." >&2
    exit 2
  fi
done
exit 0
