# RookHub

Zentrales Webportal fuer Schachspieler: PGN-Repertoire-Verwaltung, Live-Turnierdaten von chess-results.com, Benutzerprofile mit FIDE/ChessResults-Verlinkung und Freundeslisten.

💬 **Community / Fragen?** Komm in unseren Discord: https://discord.gg/wczc4BJtMf

## Architektur

```
                         ┌──────────────────┐
                         │   Frontend        │
                         │   Angular :8085   │
                         │   (nginx)         │
                         └───────┬──────────┘
                                 │ /api/*
                         ┌───────▼──────────┐
                         │   RookHub API     │
                         │   .NET :5001      │
                         └──┬────────────┬──┘
                            │            │ Crawler__BaseUrl
                    ┌───────▼───┐  ┌─────▼──────────┐
                    │  MariaDB  │  │  Crawler API    │
                    │  :3306    │  │  .NET :8080     │
                    │           │  │  (± Gluetun VPN)│
                    └───────────┘  └─────┬──────────┘
                     chessresults         │
                     + rookhub            │ HTTP + AngleSharp
                                   ┌─────▼──────────┐
                                   │ chess-results   │
                                   │     .com        │
                                   └────────────────┘
```

| Komponente | Beschreibung | Repository |
|-----------|-------------|------------|
| **RookHub Frontend** | Angular 19 SPA mit Angular Material | `src/frontend/` |
| **RookHub API** | .NET 9 REST-API, JWT-Auth, Proxy zum Crawler | `src/api/` |
| **ChessResults Crawler** | .NET 9 Crawler fuer chess-results.com | [`chessresults_crawler`](../chessresults_crawler) |
| **MariaDB** | Shared DB-Server mit zwei Datenbanken (`rookhub` + `chessresults`) | via Docker |

## Tech Stack

| Schicht | Technologie | Version |
|---------|-------------|---------|
| Frontend | Angular + Angular Material | 19.2 |
| Backend | ASP.NET Core Web API | 9.0 |
| ORM | EF Core + Pomelo (MySQL) | 9.0 |
| Auth | JWT Bearer + BCrypt | - |
| Datenbank | MariaDB | 11 |
| Crawler | AngleSharp (HTML-Parsing) | 1.4 |
| VPN (optional) | Gluetun (WireGuard) | - |
| API Docs | Swagger / Swashbuckle | 6.9 |

## Voraussetzungen

- [Docker](https://docs.docker.com/get-docker/) + Docker Compose
- Das [chessresults_crawler](../chessresults_crawler)-Repository als Sibling-Verzeichnis:
  ```
  git/
    rookhub/              # dieses Repo
    chessresults_crawler/ # Crawler-Repo
  ```

## Schnellstart

### 1. Repository klonen

```bash
git clone <repo-url> rookhub
git clone <repo-url> chessresults_crawler
```

### 2. Environment einrichten

```bash
cd rookhub

# Fuer Development: .env.dev kann direkt verwendet werden (liegt im Repo)
# Fuer Production/VPN: .env.vpn aus Template erstellen und anpassen
cp .env.vpn.example .env.vpn
# WICHTIG: In .env.vpn echte Passwoerter und VPN-Keys eintragen!
```

### 3. Stack starten

```bash
# Development (ohne VPN):
docker compose -f compose.dev.yml --env-file .env.dev up --build

# Production (mit Gluetun VPN):
docker compose -f compose.vpn.yml --env-file .env.vpn up --build
```

### 4. Zugriff

| Dienst | URL |
|--------|-----|
| Frontend | http://localhost:8085 |
| RookHub API + Swagger | http://localhost:5001/swagger |
| Kibana (Logs-Dashboard) | http://localhost:5601 |
| Elasticsearch | http://localhost:9200 |
| MariaDB | `localhost:3307` (User/Passwoerter siehe `.env`-Datei) |

## Compose-Konfiguration

Es gibt zwei eigenstaendige Compose-Dateien — kein Overlay, jeweils vollstaendig:

| Datei | Zweck | VPN |
|-------|-------|-----|
| `compose.dev.yml` | Lokale Entwicklung | Nein |
| `compose.vpn.yml` | Produktion / VPN-Betrieb | Ja (Gluetun/WireGuard) |

### Unterschiede

| Aspekt | `compose.dev.yml` | `compose.vpn.yml` |
|--------|--------------------|--------------------|
| Crawler-Netzwerk | Direkt im Docker-Netzwerk | Via `network_mode: service:gluetun` |
| Crawler-Adresse | `http://crawler:8080` | `http://gluetun:8080` |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` |
| Zusaetzliche Services | — | Gluetun (WireGuard VPN) |

### Environment-Dateien

| Datei | Git | Zweck |
|-------|-----|-------|
| `.env.dev` | Committed | Dev-Defaults, keine echten Secrets |
| `.env.vpn.example` | Committed | Template fuer Production |
| `.env.vpn` | Gitignored | Echte Production-Secrets |

Alle Werte (DB-Credentials, JWT-Keys, Ports, VPN-Config) werden ausschliesslich ueber die `.env`-Dateien gesteuert — die Compose-Files enthalten keine Defaults.

## VPN-Betrieb (Gluetun)

Im VPN-Modus wird der Crawler-Traffic durch einen WireGuard-Tunnel geroutet:

```
Internet  <--WireGuard-->  Gluetun  <--shared network-->  Crawler  -->  chess-results.com
                              ^
                     RookHub API (http://gluetun:8080)
```

**Voraussetzungen:**
- WireGuard-Config vom VPN-Provider (z.B. AirVPN)
- Private Key und Adresse in `.env.vpn` eintragen

**VPN pruefen:**
```bash
# Zeigt die oeffentliche IP des Crawlers (sollte VPN-IP sein, nicht eigene)
curl http://localhost:5001/api/health/ip
```

## Logging & Monitoring (Elasticsearch + Kibana)

Beide Backends loggen ueber **Serilog** strukturiert nach **Elasticsearch**:

| App | Index-Muster |
|-----|--------------|
| RookHub API | `rookhub-logs-YYYY.MM` |
| ChessResults Crawler | `crawler-logs-YYYY.MM` |

Visualisierung in **Kibana** (Dashboard *"RookHub Logging Dashboard"*; Data Views `RookHub Logs`, `Crawler Logs`, `Alle Logs`).

| Dienst | URL | Auth |
|--------|-----|------|
| Kibana | http://localhost:5601 | keine (`xpack.security` deaktiviert) |
| Elasticsearch | http://localhost:9200 | keine |

**Log-Zugriff ohne Docker:** Da ES/Kibana auf die Host-Ports `9200`/`5601` gemappt sind, lassen sich die Logs direkt per HTTP abfragen — kein `docker logs` / Docker-Socket noetig:

```bash
# Welche Log-Indizes gibt es (inkl. Doc-Count)?
curl -s 'http://localhost:9200/_cat/indices/*-logs-*?v'
# Letzte Fehler/Warnungen
curl -s 'http://localhost:9200/rookhub-logs-*/_search?q=level:Error&size=5&sort=@timestamp:desc'
# Kibana Data Views
curl -s 'http://localhost:5601/api/data_views' -H 'kbn-xsrf: true'
```

**Kibana-Init (`init-kibana.sh`):** Ein One-Shot-Container (`kibana-init`) legt beim `up` die Data Views + das Dashboard an. Seit v0.23.3 mit `allowNoIndex:true` — die Data Views entstehen auch dann, wenn beim init-Lauf noch kein Log-Index existiert (frischer Stack). Falls Kibana doch leer ist, init manuell nachziehen:

```bash
docker start -a rookhub-kibana-init
# oder ohne Docker-Zugriff, direkt gegen den Host-Port:
sed 's#http://kibana:5601#http://localhost:5601#g' init-kibana.sh | sh
```

## API-Uebersicht

### Oeffentlich (kein JWT)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/auth/register` | Registrierung |
| POST | `/api/auth/login` | Login (gibt JWT zurueck) |
| GET | `/api/profile/{username}` | Oeffentliches Profil |

### Authentifiziert (JWT Bearer)
| Bereich | Endpoints |
|---------|-----------|
| Profil | `GET/PUT /api/profile` |
| Freunde | `/api/friends/*` (Liste, Requests, Suche, Accept/Decline) |
| Repertoires | `/api/repertoires/*` (CRUD, PGN-Upload bis 10 MB) |
| Turniere | `/api/tournaments/*` (Proxy zum Crawler) |
| Abos | `/api/subscriptions/*` (Turnier-Benachrichtigungen) |

Vollstaendige API-Dokumentation: http://localhost:5001/swagger

## Entwicklung

### Angular Frontend (standalone)

```bash
cd src/frontend/app
npm install
npx ng serve    # http://localhost:4200 — braucht API auf :5001
```

### .NET API (standalone, braucht MariaDB auf :3307)

```bash
cd src/api/RookHub.Api
dotnet run
```

### Tests

```bash
cd tests/RookHub.Api.Tests
dotnet test
```

### EF Core Migrations

```bash
cd src/api/RookHub.Api
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

Auto-Migration ist aktiv — beim Container-Start werden Migrations automatisch angewendet.

## Projektstruktur

```
rookhub/
  compose.dev.yml           Dev-Stack (MariaDB + Crawler + API + Frontend)
  compose.vpn.yml           Prod-Stack mit Gluetun VPN
  .env.dev                  Dev-Environment (committed)
  .env.vpn.example          Prod-Environment-Template
  init-db.sh                Erstellt beide DBs + User beim ersten MariaDB-Start
  src/
    api/RookHub.Api/
      Controllers/          Auth, Profile, Friends, Repertoire, TournamentProxy, Subscriptions
      Services/             Auth (JWT+BCrypt), CrawlerProxy (HttpClient), Profile, Friends, Repertoire
      Models/               AppUser, UserProfile, Friendship, Repertoire, RepertoireFile, ...
      Data/                 AppDbContext, Migrations
      Program.cs            Startup: DB, JWT, CORS, Swagger, Auto-Migration
      Dockerfile
    frontend/
      app/                  Angular 19 Projekt (standalone components, lazy loading)
      nginx.conf            Proxy /api/ -> api:8080, SPA-Fallback
      Dockerfile            Multi-stage Build (Node -> nginx)
  tests/
    RookHub.Api.Tests/      xUnit-Tests (Auth, Profile, Friends, Repertoire)
```

## Zusammenspiel mit dem Crawler

Die RookHub API leitet Turnier-Anfragen als Proxy an den ChessResults Crawler weiter:

- `Services/CrawlerProxyService.cs` — HTTP-Client zum Crawler
- `Controllers/TournamentProxyController.cs` — Mappt RookHub-Routen auf Crawler-Routen

Crawler-Responses werden als `JsonElement` durchgereicht (kein festes DTO-Mapping). Bei Aenderungen an Crawler-Endpoints muessen diese beiden Dateien angepasst werden.

## Lizenz

Privates Projekt — kein oeffentliches Repository.
