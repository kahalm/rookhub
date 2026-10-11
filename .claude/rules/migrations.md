---
paths:
  - "**/Migrations/**"
  - "**/Data/AppDbContext.cs"
  - "**/Data/Configurations/**"
---

# EF-Core-Migrationen

- `*.Designer.cs` und `AppDbContextModelSnapshot.cs` sind generiert (je ~9 600 Zeilen) — nicht lesen, nicht von Hand ändern.
  Der Modellstand steht in `Models/` + `Data/Configurations/`.
- Anlegen: `cd src/api/RookHub.Api && dotnet ef migrations add <Name>` (PATH: `export PATH="$HOME/.dotnet:$PATH"`).
- **Nach JEDEM `migrations add` in den Rumpf schauen**: `sed -n '/void Up/,/^        }/p' Migrations/*_<Name>.cs` —
  steht dort kein `migrationBuilder.`, ist die Migration leer. Typischer Fall: nach einem Rebase neu erzeugt, während
  der Schnappschuss die Änderung schon enthält → vorher den Schnappschuss auf den Stand der letzten Migration zurücksetzen
  (`dotnet ef migrations remove`). `MigrationsInventoryTests` macht jedes leere `Up()` rot (Allowlist mit Begründung).
- Eine schon angewendete Migration nie nachträglich ändern — neue hinterherschieben (MariaDB: `IF NOT EXISTS`-SQL).
- InMemory-Tests prüfen die MariaDB-SQL-Übersetzung NICHT — Schemaänderungen und handgebaute Abfragen auf Dev verifizieren.
