# LeagueHub — Vereine als Mandanten (0.698.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → LeagueHub — Vereine als Mandanten (0.698.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Wunsch 2026-10-07: „LeagueHub für mehrere Vereine — SK Weilheim (Bayern, Landesliga Süd im Ligamanager) bekommt dieselbe
Funktionalität, streng getrennt von Schwaz." Es bleibt EIN LeagueHub, EINE API, EINE Datenbank, EIN Konto. Was „wir" heißt,
hängt am Verein; die öffentlichen Liga-Daten (Spielpläne, Meldelisten, Spielerkarten samt der Vereinspartien darin,
Online-Konten, Übertragungen, Megabase) sind für alle Vereine dieselben.

* **Tabellen** `LeagueClubs` (Id, Name, TeamPrefix, AnonName, Region — bis 0.703.0 `Source`, siehe unten —, CreatedAt) und `LeagueClubMembers` (ClubId, GroupId; eine
  Gruppe gehört zu höchstens einem Verein). Migration `LeagueClubs`: 1 = SK Schwaz (`Schwaz`/`Schwaz`, chess-results), 2 = SK
  Weilheim (`SK Weilheim`/`Weilheim`, `ligamanager`); **alle Bestandszeilen** von `LeagueClubGames`, `LeagueClubDrafts`,
  `LeagueBatchUploads`, `LeagueShares` und die Liga-Einlesungen in `ScoresheetScans` bekommen `ClubId = 1`; **jede Gruppe, deren
  Rollen `league.view` tragen** (außer „Everyone"), gehört Verein 1 — aus dem Code nicht ableitbar, auf Dev und Prod ist das genau
  die Gruppe „Schwaz". Fremdschlüssel auf `LeagueClubs` mit Restrict. Weilheims Gruppe ordnet der Admin zu
  (`POST /api/league/admin/clubs/2/groups/{groupId}`).
* **Was am Verein hängt**: Vereinspartien, Entwürfe, Partieformulare (Liga-Einlesungen), Stapel-Uploads, Teilen-Links (ein Link
  gehört dem Verein, der ihn erzeugt — darüber laufen die anonymen Uploads), die Anonymisierung (`AnonName`), „einer von uns"
  (`LeagueClub.OwnsTeam`: Mannschaftsname = `TeamPrefix` oder beginnt mit `TeamPrefix` + Leerzeichen/„/", ohne Groß/klein —
  „SK Weilheim 1" ja, „SK Weilheimer" nein), der Taktik-Kurs (`tactics-club-{id}.pgn`), die Zeile „Vereins-Datenbank" der
  Quellen-Tabelle, die Paarungen gespielter Runden (Vereinspartie nur aus dem eigenen Verein), die Startseite (nur Ligen der Region,
  seit 0.710.0 nur die mit eigener Mannschaft), die Treffer-Statistik (nur Ligen von `Source`). **Global** bleiben: `LeagueNameAliases` (PGN-Name → Spieler ist
  eine Aussage über öffentliche Ligaspieler, sie verweist bewusst weder auf Partie noch auf Verein oder Nutzer —
  vereinsübergreifend harmlos), `LeagueSelfReports`/Online-Konten (hängen an FIDE-IDs), der Meldelisten-Index (je Verein nur mit
  eigener `OwnClub`-Regel, gecacht je Verein), Zuordnen nach dem Anmelden (der Schlüssel des Browsers sagt, was ER hochgeladen
  hat), „Daten aktualisieren" und die Admin-Importe.
* **Zugehörigkeit + Rechte**: über die Gruppen des Kontos (`UserGroups` → `LeagueClubMembers`); Admins gehören zu ALLEN Vereinen.
  Die Rechte `league.view/contribute/manage` bleiben Rollen wie bisher und wirken in den Vereinen des Kontos (kein Recht je
  Verein). Ein Konto mit Recht, aber ohne Vereinsgruppe, bekommt 403 `noClub` (auf Prod hatten die direkt vergebenen
  League-Rollen nur Admins). „Everyone" zählt nicht (trägt auch keine Rollen, `PermissionResolver`).
* **Der Verein einer Anfrage — EINE Stelle**: `Services/League/LeagueClubResolver.cs`, im Controller über
  `BaseApiController.LeagueClubAsync` aufgelöst und als `LeagueClub` in die Dienste gereicht (kein Dienst fragt selbst nach
  Zugehörigkeit). `?club=<id>` (LeagueHub schickt ihn an JEDEN `/api/league/…`-Aufruf außer `/me` und `/s/…`); ohne Parameter
  der einzige Verein des Kontos, bei einem Admin der einzige Verein SEINER Gruppen; sonst 400 `clubRequired`. Fremder Verein →
  403 `forbidden` (Admin: unbekannter → 404 `unknownClub`), keine Zahl → 400 `invalidClub`. Ausnahme `GET …/club/games/{id}` und
  `…/evals` (RookHubs Partie-Seite `/club-games/{id}` und die Taktik-Links kennen keinen Verein): ohne `?club=` und bei mehreren
  Vereinen gilt der der Partie, wenn das Konto dazugehört (`preferred`). Teilen-Link-Wege (`/api/league/s/{token}/…`): der
  Verein kommt aus `LeagueShares.ClubId` (`LeagueService.ShareContextAsync`), nie aus der Anfrage.
* **Kein zweiter globaler Query-Filter** für `ClubId` (Kommentar in `LeagueClubGameConfiguration`): EF Core kennt je Typ EINEN
  Filter, ein Filter auf einen Wert der Anfrage hinge am DbContext (Hintergrund-Dienste lesen alle Vereine), und ein
  `IgnoreQueryFilters` würde den Archiv-Filter mit abschalten. `LeagueClubService.Games(club)` ist der Einstieg jedes Wegs;
  `IgnoreQueryFilters` (Rückbau eines Links) hält den Verein ausdrücklich in der Bedingung. Trennung geprüft in
  `LeagueClubTenancyTests`.
* **Hintergrund über alle Vereine**: `MasterAnalysisScheduler`/`ClubSecondEngineScheduler` (alle Vereinspartien),
  `LeagueAnalysisQueue` (Gegner der nächsten Runde JEDES Vereins zuerst), `LeagueRefresh.StalePlayersAsync` (ebenso),
  `TacticHarvestService` (je Partie der Kurs ihres Vereins; Umbenennen alter Titel je Verein).
* **RookHub**: eine Kopie in „Meine Partien" wird nur mit einer Vereinspartie aus einem Verein ihres Besitzers verbunden und
  übernimmt nur deren Analyse (`SavedGameService.ClubGameForMovesAsync`/`ClubAnalysisForMovesAsync`/`LinkClubCopiesAsync`,
  `LeagueClubResolver.ClubIdsOfAsync`); korrigieren über die Kopie darf der Hochladende oder ein Verwalter DES Vereins der Partie
  (`LeagueClubResolver.CanManageAsync`). Stapel-Uploads zeigen im Admin-Tab den Verein (`club`), die manuelle Einlesung
  (`manual-scan`) gehört dem Verein des Stapels (Bilder zweier Vereine → 400 `mixedClubs`).
* **Verwaltung** (`Services/League/LeagueClubAdminService.cs`, nur Admins mit `league.manage`): Verein anlegen/ändern, Gruppe
  zuordnen (eine andere Zuordnung derselben Gruppe wird ersetzt; der Taktik-Kurs wird freigegeben bzw. beim Lösen entzogen),
  Kursname folgt dem Vereinsnamen.
* **Oberfläche (0.699.0)**: `ClubContextService` + `leagueClubInterceptor` in `src-leaguehub` (Details in
  `src/frontend/CLAUDE.md`): `GET /api/league/me` einmal je Konto, `?club=` an jeden Aufruf, Umschalter im Kopf bei mehreren
  Vereinen (gemerkt in `lh-club`, Wechsel lädt neu), alle Texte mit „Schwaz" lesen Name/`AnonName` des Vereins, Teilen-Seiten
  den Verein des Links.
* **Region statt Quelle (0.704.0)**, Wunsch 2026-10-07 („Weilheim muss Ligamanager UND Zugspitze sehen"): `LeagueClub.Region`
  (`tirol` | `bayern`, NOT NULL, Vorgabe `tirol`) ersetzt `Source`; Migration `LeagueClubRegion` benennt die Spalte um (kein
  Drop+Add) und schreibt null → `tirol`, `ligamanager` → `bayern`. Welche Quellen eine Region hat, steht NUR in
  `Services/League/LeagueRegions.cs` (`SourcesOf`, `InRegion` als SQL-Bedingung — Tirol `Source IS NULL`, Bayern `IN (…)`).
  Startseite (`IndexAsync`: laufende Saison = jüngste der Region, Ligen nach Stufe, dann Quelle, dann Gruppe) und
  Treffer-Statistik (`ForecastStatsAsync(region)`) filtern nach der Region; `ClubJson` trägt `region`. Verwaltung: Auswahl
  „Region" statt „Liga-Quelle", 400 `invalidRegion` (eine Quelle wie `ligamanager` ist KEINE Region). Geprüft in
  `LeagueClubTenancyTests.Index_BavarianClubSeesLigamanagerAndZugspitze_TyroleanClubNeither`, gegen MariaDB in
  `LeagueRegionSqlTests` und `MigrationsTests.LeagueClubRegion_MachtAusDerQuelleDieRegion`.
* **Nur Ligen mit eigener Mannschaft (0.710.0)**, Wunsch 2026-10-07 („Zeig bei der Ligaauswahl nur die Ligen, in denen der
  Verein vertreten ist"): `LeagueService.IndexAsync(club, ct, all)` filtert die Ligen der Region auf die mit einer Mannschaft
  des Vereins (`LeagueClub.OwnsTeam` über DISTINCT `LeagueMatches.Home/Away` + `LeaguePlayers.Team` — drei Abfragen
  für alle Ligen zusammen, nicht je Liga; das `teams` der Ansicht stammt aus demselben Spielplan, hieße aber jede Ansicht samt
  Prognosen zu laden). `?all=true` liefert für Verwalter (`league.manage` live über `PermissionResolver`, Admin) alle Ligen der
  Region; ohne das Recht wird der Schalter still übergangen statt 400 (ein gemerkter Schalter nach entzogenem Recht soll die
  Seite nicht leer machen). Antwort `filtered` + `total`. Oberfläche: Verwalter-Schalter „alle Ligen der Region"
  (`lh-all-leagues`, Vorgabe aus), gemerkte fremde Liga → erste eigene, ohne eigene Liga die Karte „Noch keine Liga mit einer
  Mannschaft von <Verein>" (Verwalter: „Alle Ligen zeigen"). Teilen-Links und die geteilten Liga-Daten (`/{tnr}`) bleiben
  ungefiltert — eine fremde Liga per `?liga=` öffnet die Seite nicht mehr, `GET /api/league/{tnr}` liefert sie aber weiter.
  Geprüft in `LeagueClubTenancyTests.Index_OnlyLeaguesWithAnOwnTeam_AllOnRequest`/`IndexEndpoint_AllOnlyForManagers_…` und
  gegen MariaDB in `LeagueRegionSqlTests`.
* **Vereinsverwaltung `/vereine` (0.700.0)**: Reiter „Vereine" in LeagueHub, nur Admins mit `league.manage` (sonst
  Sperrkarte; der Server verlangt beides). Liste (Name, `anonName`, `teamPrefix`, Quelle, Gruppen, Vereinspartien, angelegt),
  Anlegen/Ändern (400/409 `reason` → Klartext; Quelle umstellen mit Rückfrage — `PUT` darf alle vier Felder ändern, die
  Startseite filtert nach `Source`), Gruppen zuordnen über eine Suche in `GET /api/admin/groups` (`groups.manage`, Admins haben
  jedes Recht; „Everyone" und Gruppen ANDERER Vereine nicht wählbar, obwohl der Server eine andere Zuordnung ersetzen würde)
  und lösen mit Rückfrage. Nach jeder Änderung `ClubContextService.reload()` (neues `/api/league/me`, gewählter Verein
  bleibt). Seite `src-leaguehub/app/features/clubs/clubs-page.component.ts`.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/index` | `league.view` | Startseite `{ season, club, generated, filtered, total, leagues[{ tnr, name }] }` — seit 0.710.0 nur Ligen der Region, in denen der Verein eine Mannschaft hat (`OwnsTeam` über Spielplan + Meldelisten); `?all=true` = alle der Region, nur mit `league.manage` (sonst still übergangen, `filtered: true`, kein 400); `total` = Ligen der Region mit Ansicht |
| GET | `/api/league/me` | angemeldet | `{ clubs[{ id, name, anonName, teamPrefix, region }], current }` (`region` seit 0.704.0, vorher `source`) — `current` = Verein ohne `?club=` (`null` bei mehreren ohne Vorgabe) |
| GET | `/api/league/admin/clubs` | Admin + manage | Alle Vereine `[{ id, name, anonName, teamPrefix, region, createdAt, clubGames, groups[{ id, name, members }] }]` — `clubGames` ohne archivierte, `members` = Konten der Gruppe (0.700.0) |
| POST | `/api/league/admin/clubs` | Admin + manage | `{ name, teamPrefix, anonName, region }` (`tirol`/`bayern`, leer = `tirol`) → Verein; 400 `invalidName`/`invalidTeamPrefix`/`invalidAnonName`/`invalidRegion`, 409 `duplicate` |
| PUT | `/api/league/admin/clubs/{id}` | Admin + manage | Ändern (fehlende Felder bleiben, `region: ""` = `tirol`); 404 |
| POST | `/api/league/admin/clubs/{id}/groups/{groupId}` | Admin + manage | Gruppe zuordnen → 204; 404 `clubNotFound`/`groupNotFound`, 400 `everyone` |
| DELETE | `/api/league/admin/clubs/{id}/groups/{groupId}` | Admin + manage | Gruppe lösen → 204 / 404 |

Alle anderen angemeldeten LeagueHub-Endpunkte (`/api/league/index`, `/sources`, `/forecast-stats`, `/{tnr}/round/{r}/games`,
`/share`, `/api/league/club/…`) nehmen `?club=`; ohne Wirkung ist er bei den geteilten Liga-Daten (`/{tnr}`, `/player/…`,
`/accounts/…`, `/suggestions/…`, `/admin/…`). `GET /api/league/index` trägt zusätzlich `club` (der Verein der Anfrage),
`GET /api/league/s/{token}` trägt `club: { id, name, anonName }` (der Verein des Links).
