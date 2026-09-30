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

# Von wo sich die App-Benutzer anmelden dürfen (Codereview 2026-09-29, I1-003). Früher '%': jeder,
# der den veröffentlichten Port erreichte — aus LAN und VPN inklusive — bekam den Passwort-Login.
# Vorgabe sind die Adressbereiche, aus denen Docker seine Bridge-Netze vergibt (172.16.0.0/12 und
# 192.168.0.0/16, in MariaDBs Netzmasken-Form). Das deckt die Dienste im Compose-Netz UND Werkzeuge
# auf dem Host: über den auf 127.0.0.1 veröffentlichten Port meldet sich der docker-proxy mit der
# Gateway-Adresse des Compose-Netzes. BEIDE Bereiche, weil Docker nach dem 172er-Vorrat auf 192.168
# ausweicht (auf dem Deploy-Host liegen neue Netze längst dort). Eigene Adress-Pools im Daemon
# (default-address-pools) → DB_APP_HOSTS passend setzen (Leerzeichenliste, je Eintrag ein Konto).
# Läuft das Skript auf einem BESTEHENDEN Volume (von Hand), bleibt ein früher angelegtes
# '<user>'@'%' stehen, bis es jemand entfernt: DROP USER '<user>'@'%';
read -r -a app_hosts <<< "${DB_APP_HOSTS:-172.16.0.0/255.240.0.0 192.168.0.0/255.255.0.0}"
[ "${#app_hosts[@]}" -gt 0 ] || { echo "DB_APP_HOSTS enthält keinen Adressbereich" >&2; exit 1; }

# `CREATE USER IF NOT EXISTS … IDENTIFIED BY` setzt das Passwort eines BESTEHENDEN Benutzers NICHT
# — eine Rotation in der .env wirkte auf einem vorhandenen Volume also still nicht. Das nachgestellte
# `ALTER USER` zieht es nach, sobald das Skript läuft (bei einem frischen Volume ohnehin, sonst
# beim manuellen Aufruf). Je Adressbereich ein Konto mit denselben Rechten.
# $1 = Datenbank (als Identifier maskiert), $2 = Benutzer, $3 = Passwort (beide als Literal maskiert).
# Die Werte gehen als printf-ARGUMENTE hinein, nie ins Format — ein % im Passwort bleibt ein %.
app_user_sql() {
  local host
  for host in "${app_hosts[@]}"; do
    host=$(sql_literal "$host")
    printf "CREATE USER IF NOT EXISTS '%s'@'%s' IDENTIFIED BY '%s';\n" "$2" "$host" "$3"
    printf "ALTER USER '%s'@'%s' IDENTIFIED BY '%s';\n" "$2" "$host" "$3"
    printf "GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES ON \`%s\`.* TO '%s'@'%s';\n" "$1" "$2" "$host"
  done
}

docker_process_sql <<-EOSQL
CREATE DATABASE IF NOT EXISTS \`${crawler_db}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
$(app_user_sql "$crawler_db" "$crawler_user" "$crawler_pw")

CREATE DATABASE IF NOT EXISTS \`${rookhub_db}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
$(app_user_sql "$rookhub_db" "$rookhub_user" "$rookhub_pw")

FLUSH PRIVILEGES;
EOSQL
