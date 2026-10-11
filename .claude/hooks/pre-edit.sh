#!/bin/bash
# PreToolUse (Edit|Write): generierte EF-Dateien nie von Hand ändern.
f=$(jq -r '.tool_input.file_path // empty')
case "$f" in
  */Migrations/*.Designer.cs)
    echo "Blockiert: $f ist von EF generiert. Modell in Models/ bzw. Data/Configurations/ ändern und 'dotnet ef migrations add' laufen lassen (.claude/rules/migrations.md)." >&2
    exit 2 ;;
esac
exit 0
