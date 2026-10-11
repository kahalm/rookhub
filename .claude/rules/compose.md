---
paths:
  - "**/compose*.yml*"
  - "**/compose*.example"
  - "**/.env*.example"
---

# Variablen-Muster in den Compose-Dateien

> Lädt automatisch, sobald eine passende Datei gelesen/bearbeitet wird. Ausgelagert aus `CLAUDE.md` „Wichtige Konventionen“ (2026-10-11).

- **Variablen-Muster in den Compose-Dateien** – drei Fälle, und zwar in ALLEN fünf Dateien gleich (`compose.yml.example`, `compose.vpn.example`, `compose.vpn.yml`, `compose.dev.yml`, `compose.dev.vpn.yml`): **Pflichtwert** → `${VAR}` ohne Default (fehlt er, ist der Stack sowieso kaputt); **optionales Feature** → `${VAR:-}` (leer = Feature aus, alle betroffenen Endpoints sind fail-closed: Bot-Stats 503 `not-configured`, CI-Report 401, Webhook deaktiviert); **Wert, dessen Fehlen still Schaden anrichtet** → `${VAR:?Meldung}`, damit `docker compose` mit einer Meldung abbricht statt den Container in eine Neustartschleife zu schicken (heute: `JWT_KEY` und `ENCRYPTION_KEY` — ein leerer Encryption-Key wäre keine abgeschaltete Verschlüsselung, sondern eine Schein-Verschlüsselung mit dem öffentlich bekannten SHA256("")). Wenige echte Vorgabewerte sind bewusst gesetzt (`EMAIL_SMTP_PORT:-587`, `EMAIL_FROM_NAME:-RookHub`, `EMAIL_USE_STARTTLS:-true`, `CHESSABLE_API_URL:-…`). Die frühere Fassung dieser Regel („keine `:-`-Defaults in den Beispielen") beschrieb den Ist-Zustand nicht — es gab 16 pro Datei — und ließ den nötigen `:?`-Guard als Ausnahme unsichtbar. `DeploymentConfigTests.EveryCompose_GuardsEncryptionKey_AndPassesOptionalSecrets` nagelt das fest.
