# Backup & Restore (MariaDB)

Der Stack hatte bis dahin **kein** Backup — ein verlorener DB-Volume-Mount hätte
alle Benutzer, Kurse, Puzzle-Fortschritte und Turnierdaten mitgenommen. Dieses
Dokument beschreibt das Skript, seine Einrichtung und den Wiederherstellungsweg.

Betroffene Datenbanken (beide liegen im selben MariaDB-Container):

| Datenbank      | Inhalt |
|----------------|--------|
| `rookhub`      | Benutzer, Profile, Repertoires, Kurse/Bücher, Puzzle-Ergebnisse, Nachrichten |
| `chessresults` | Vom Crawler geholte Turnier-/Spielerdaten |
| `piratechess`  | Chessable-Rohdaten-Cache **und die AES-verschlüsselten Chessable-Zugangsdaten der Nutzer** — ohne diese DB ist ein Restore unvollständig |

## Skript

`scripts/backup-db.sh` — läuft auf dem Deploy-Host und dumpt alle drei Datenbanken
per `docker exec` aus dem laufenden MariaDB-Container (kein Client-Setup nötig).

```bash
sudo ./scripts/backup-db.sh                                 # Standardwerte
sudo BACKUP_DIR=/data/backups RETENTION_DAYS=30 ./scripts/backup-db.sh
```

| ENV | Default | Zweck |
|-----|---------|-------|
| `ENV_FILE` | `/opt/stacks/rookhub-schach/.env` | Quelle für `MARIADB_ROOT_PASSWORD` (alternativ direkt als ENV setzen) |
| `DB_CONTAINER` | `rookhub-mariadb` | Container-Name |
| `DATABASES` | `rookhub chessresults piratechess` | Leerzeichenliste. **piratechess gehört dazu** — die DB liegt im selben Container und hält die verschlüsselten Chessable-Zugangsdaten. |
| `BACKUP_DIR` | `/var/backups/rookhub` | Zielverzeichnis (Modus 700, Dumps 600) |
| `RETENTION_DAYS` | `14` | Aufbewahrung; ältere `*.sql.gz` werden gelöscht |

Ergebnis: `\<db\>-YYYYmmdd-HHMMSS.sql.gz` (`mariadb-dump --single-transaction`,
also konsistent ohne Table-Locks).

Eigenschaften, die beim Selbstbau gern fehlen und hier drin sind:

- **Fehlerhafte Dumps werden verworfen** statt als „Backup“ abgelegt: die Pipe
  `mariadb-dump | gzip` liefert sonst Exit 0, obwohl der Dump abgebrochen ist.
  Geprüft werden Exit-Status (`pipefail`), `gzip -t` und eine Mindestgröße.
- **Rotation läuft nur nach einem sauberen Durchlauf** — ein kaputter Lauf darf
  die letzten funktionierenden Backups nicht mit wegräumen.
- **Passwort per `--defaults-extra-file`**, nicht als `-p`-Argument (Prozessliste)
  und nicht als Umgebungsvariable (`MYSQL_PWD` wäre auf dem Host via
  `/proc/<pid>/environ` bzw. `ps e` einsehbar): das Skript schreibt eine
  `mktemp`-Datei (`chmod 600`) mit `[client]`-Sektion, kopiert sie per `docker cp`
  in den Container und räumt beide Kopien per `trap` wieder weg — auch bei Abbruch.
- Test dazu: `scripts/tests/test_backup_db.sh` (läuft gegen ein Fake-`docker`,
  kein Container nötig).

## Einrichtung (Zeitplan)

Beides sind **Vorlagen** — bewusst nicht installiert:

- systemd: `scripts/systemd/rookhub-backup.service.example` +
  `rookhub-backup.timer.example` (täglich 03:17, `Persistent=true` holt
  verpasste Läufe nach).
- cron: `scripts/rookhub-backup.cron.example` → `/etc/cron.d/rookhub-backup`.

Installationsbefehle stehen jeweils im Kopf der Datei. Beide brauchen root
(Docker-Socket + `.env` mit dem Root-Passwort).

**Off-Site**: Die Dumps liegen auf demselben Host wie die Datenbank. Gegen
Plattenausfall/Ransomware schützt das nicht — das Backup-Verzeichnis zusätzlich
auf ein anderes System spiegeln (rsync/restic/Cloud-Bucket).

## Restore

1. Schreibende Dienste stoppen (die API migriert beim Start und funkt sonst
   dazwischen):

   ```bash
   docker stop rookhub-api rookhub-crawler
   ```

2. Dump einspielen (Beispiel `rookhub`):

   ```bash
   # -e MYSQL_PWD OHNE Wert: die Variable kommt aus der Shell-Umgebung — mit
   # Wert stünde das Passwort in der docker-Kommandozeile (Host-Prozessliste).
   gunzip -c /var/backups/rookhub/rookhub-20260807-031700.sql.gz \
     | MYSQL_PWD="$MARIADB_ROOT_PASSWORD" docker exec -i -e MYSQL_PWD \
         rookhub-mariadb mariadb -u root --default-character-set=utf8mb4 rookhub
   ```

   Der Dump enthält `CREATE DATABASE IF NOT EXISTS` + `USE` (`--databases`), die
   Zieldatenbank muss also nicht existieren. Bestehende Tabellen werden per
   `DROP TABLE` ersetzt — der Restore **überschreibt den aktuellen Stand dieser
   Datenbank vollständig**.

3. Dienste starten und Migrationslauf prüfen:

   ```bash
   docker start rookhub-crawler rookhub-api
   docker logs -f rookhub-api | head -50
   ```

   Ist der Dump älter als der laufende Code, holen die EF-Migrationen beim Start
   den Schemastand automatisch nach. Umgekehrt (Dump neuer als das Image) fehlt
   dem Code das Schema-Wissen nicht — er migriert nur nicht zurück.

4. **Restore vorher üben.** Ein Backup, dessen Restore nie getestet wurde, ist
   kein Backup:

   ```bash
   MYSQL_PWD="$MARIADB_ROOT_PASSWORD" docker exec -i -e MYSQL_PWD rookhub-mariadb \
     mariadb -u root -e "CREATE DATABASE restore_test"
   # CREATE DATABASE/USE aus dem Dump entfernen, sonst landet er wieder in der
   # Original-Datenbank statt in restore_test.
   gunzip -c <dump>.sql.gz | sed -e '/^CREATE DATABASE/d' -e '/^USE /d' \
     | MYSQL_PWD="$MARIADB_ROOT_PASSWORD" docker exec -i -e MYSQL_PWD rookhub-mariadb \
         mariadb -u root restore_test
   ```

## Siehe auch

- [Log-Retention in Elasticsearch](log-retention.md) — Löschfrist für die Logs
  (enthalten IP/UserId/User-Agent, DSGVO-relevant).
