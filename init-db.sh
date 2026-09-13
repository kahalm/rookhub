#!/bin/bash
set -e

# All variables must be set via environment (compose.dev.yml / compose.vpn.yml)
: "${CRAWLER_DB_NAME:?CRAWLER_DB_NAME is not set}"
: "${CRAWLER_DB_USER:?CRAWLER_DB_USER is not set}"
: "${CRAWLER_DB_PASSWORD:?CRAWLER_DB_PASSWORD is not set}"
: "${ROOKHUB_DB_NAME:?ROOKHUB_DB_NAME is not set}"
: "${ROOKHUB_DB_USER:?ROOKHUB_DB_USER is not set}"
: "${ROOKHUB_DB_PASSWORD:?ROOKHUB_DB_PASSWORD is not set}"

# FALLE: Diese Werte gehen in SQL-LITERALE. Ein einfaches Anführungszeichen im Passwort (bei
# generierten Passwörtern durchaus üblich) beendete das Literal — der Rest der Zeile wurde als SQL
# gelesen. Weil das Skript im `docker-entrypoint-initdb.d` läuft, blieb die Datenbank dann HALB
# initialisiert (Crawler-DB da, RookHub-Benutzer fehlt) und die API drehte in Access-Denied-Schleifen.
# In MySQL-Literalen maskiert `''` das Anführungszeichen und `\\` den Backslash; Identifier
# (Datenbank-/Benutzernamen) stehen in Backticks bzw. Literalen und werden entsprechend maskiert.
sql_literal() { local v="${1//\\/\\\\}"; printf '%s' "${v//\'/\'\'}"; }
sql_ident()   { printf '%s' "${1//\`/\`\`}"; }

crawler_db=$(sql_ident "$CRAWLER_DB_NAME")
crawler_user=$(sql_literal "$CRAWLER_DB_USER")
crawler_pw=$(sql_literal "$CRAWLER_DB_PASSWORD")
rookhub_db=$(sql_ident "$ROOKHUB_DB_NAME")
rookhub_user=$(sql_literal "$ROOKHUB_DB_USER")
rookhub_pw=$(sql_literal "$ROOKHUB_DB_PASSWORD")

# `CREATE USER IF NOT EXISTS … IDENTIFIED BY` setzt das Passwort eines BESTEHENDEN Benutzers NICHT
# — eine Rotation in der .env wirkte auf einem vorhandenen Volume also still nicht. Das nachgestellte
# `ALTER USER` zieht es nach, sobald das Skript läuft (bei einem frischen Volume ohnehin, sonst
# beim manuellen Aufruf).
docker_process_sql <<-EOSQL
CREATE DATABASE IF NOT EXISTS \`${crawler_db}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${crawler_user}'@'%' IDENTIFIED BY '${crawler_pw}';
ALTER USER '${crawler_user}'@'%' IDENTIFIED BY '${crawler_pw}';
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES ON \`${crawler_db}\`.* TO '${crawler_user}'@'%';

CREATE DATABASE IF NOT EXISTS \`${rookhub_db}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${rookhub_user}'@'%' IDENTIFIED BY '${rookhub_pw}';
ALTER USER '${rookhub_user}'@'%' IDENTIFIED BY '${rookhub_pw}';
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES ON \`${rookhub_db}\`.* TO '${rookhub_user}'@'%';

FLUSH PRIVILEGES;
EOSQL
