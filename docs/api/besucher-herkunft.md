# „RookHub weltweit“ (Besucher-Herkunft) — Weltkarte nach Ländern (0.982.0)

Seite `/visitors` (☰ → Community → „RookHub weltweit“ — bis 0.982.0 „Besucher-Herkunft“; Menüschlüssel `visitors`, Vorgabe „Angemeldete“): Weltkarte mit
**eindeutigen Benutzern je Land** in fünf Stufen, daneben die Rangliste. Gezählt wird über alle fünf Oberflächen
(RookHub, LeagueHub, KidHub, ClubHub, Turnierseite).

## Was gespeichert wird

Tabelle `VisitorDays` — **eine Zeile je Tag (UTC), Besucher und Oberfläche**:

| Spalte | Inhalt |
|---|---|
| `Day` | Kalendertag (UTC), keine Uhrzeit |
| `VisitorHash` | Pseudonym: HMAC-SHA256 der Besucher-Kennung (`u:<Benutzername>` bzw. `a:<Hash der anonymen Sitzungs-Id>`, `VisitorIdResolver`) mit einem Schlüssel des Servers, 32 Hex-Zeichen. Über die Tage gleich — sonst ließe sich „eindeutig im Zeitraum“ nicht zählen |
| `Surface` | `rookhub` \| `leaguehub` \| `kidhub` \| `clubhub` \| `turnier` — aus dem Host der Anfrage (`VisitorOriginRecorder.SurfaceOf`; das Frontend-nginx jeder Oberfläche reicht `Host` durch) |
| `Country` | ISO-3166-Land (zwei Buchstaben) aus der IP-Adresse, leer = unbekannt |
| `Hits` | gezählte Aufrufe an diesem Tag |

Eindeutiger Index `(Day, VisitorHash, Surface)`. **Nicht gespeichert:** IP-Adresse, Benutzername, Nutzernummer,
User-Agent, Uhrzeit, aufgerufene Adressen. Das Land kommt aus der lokalen DB-IP-Liste (`IpCountryService`, „IP to Country
Lite“, CC BY 4.0 — die Seite nennt „IP Geolocation by DB-IP“); keine Adresse geht an einen fremden Dienst.

Frist: `Retention:VisitorDays` (Vorgabe **400 Tage**), gelöscht im täglichen Aufräumlauf
(`VisitorOriginRetention` in `AnonymousDataRetentionService`, `docs/api/speicherdauer.md`).

## Erfassung (`Services/Accounts/VisitorOriginRecorder.cs`)

- Eingehängt in `RequestLogContextMiddleware` nach dem Rest der Pipeline: je beantworteter Anfrage EIN
  Wörterbuch-Zugriff im Speicher, **kein Datenbankzugriff**. `VisitorOriginFlushWorker` (PeriodicWorker, jede Minute,
  pausierbar, letzter Schub beim Herunterfahren) schreibt gebündelt weg: vorhandene Zeile → Aufrufe addieren, fehlendes
  Land nachtragen; sonst neue Zeile (`VisitorOriginService.FlushAsync`). Scheitert das Schreiben, gehen die Zähler
  zurück in den Sammler.
- **Nicht gezählt:** Anfragen ohne Besucher-Kennung; System-/Maschinen-Anfragen (`SystemCallClassifier`: Health,
  Heartbeats, Menü-/Zähler-Polls, Engine-Provider, Client-Log); erkennbare Bots und Anfragen ohne User-Agent
  (`DeviceClassifier`); private/interne Adressen (10.x, 172.16/12, 192.168.x, Loopback, CGNAT, Link-Local —
  `IpCountryService.IsPublic`; sie erscheinen gar nicht, auch nicht als „intern“); alles außerhalb `/api`; Antworten
  mit Status ≥ 400.
- **Deckel** (die anonyme Kennung ist ein frei wählbarer Header): je Adresse höchstens 50 neue Besucher am Tag
  (Zähler nur im Speicher), insgesamt höchstens 50 000 am Tag.
- Das Land wird ohne zu warten nachgeschlagen (`IpCountryService.TryCountryOf`): ist die DB-IP-Liste noch nicht
  geladen, zählt der Besucher zunächst mit leerem Land, und ein späterer Aufruf desselben Tages trägt es nach.
- Die Besucher-Kennung ohne Konto setzt der `visitorInterceptor` (RookHub, KidHub, Turnierseite). LeagueHub und
  ClubHub senden keine — dort zählen nur angemeldete Konten.

Konfiguration: `VisitorStats:Enabled` (Vorgabe an; aus = nichts wird gezählt, der Schreibdienst endet),
`VisitorStats:Salt` (optional; fehlt es, wird der Schlüssel aus `Jwt:Key` abgeleitet — ein Wechsel von Salz oder
JWT-Schlüssel lässt alle Besucher als neue erscheinen), `Retention:VisitorDays`.

## Endpunkt

| Methode | Route | Wer | Antwort |
|---|---|---|---|
| GET | `/api/visitor-origins?days=&surface=` | angemeldet | `VisitorOriginsDto` |

- **Normales Konto:** immer alles Erfasste über alle Oberflächen; `days`/`surface` werden **nicht gelesen**
  (`admin: false`, `days`/`surface`/`hits`/`since` leer, je Land nur `users`) — nichts Zeitbezogenes.
- **Admin:** `days` ∈ 7 / 30 / 90 / 365 (ohne = alles), `surface` ∈ die fünf Oberflächen (ohne = alle); dazu `hits`
  gesamt und je Land. Unbekannte Werte → 400.
- `uniqueUsers` = `COUNT(DISTINCT VisitorHash)` der Auswahl; je Land ebenso. Wer im Zeitraum aus zwei Ländern kam,
  zählt in beiden Ländern, in der Gesamtzahl einmal. `unknownUsers` = Besucher ohne Land; `since` = erster erfasster
  Tag überhaupt (nur Admin).

## Oberfläche (`features/visitors/`)

- Karte aus dem statischen Asset `/assets/geo/world-countries.json` (≈ 200 KB, gzip ≈ 55 KB; ISO-Land, Kontinent,
  SVG-Pfad; Ausschnitt je Kontinent) — erzeugt von `tools/visitor-map/build_map.py` aus world-atlas `countries-50m`
  (Natural Earth, gemeinfrei). Kein Karten-Paket zur Laufzeit.
- **Kontinent-Ansicht** (`?continent=eu|na|sa|af|as|oc`, Chips über der Karte oder Klick auf ein Land): Ausschnitt,
  Kennzahlen und Rangliste des Kontinents, Farbskala gegen sein eigenes Maximum; rein aus den gelieferten Länderzahlen
  gerechnet, kein eigener Abruf, für alle Konten. **„Eindeutige Benutzer“ eines Kontinents ist die Summe seiner
  Länder** — wer aus zwei Ländern desselben Kontinents kam, zählt dort zweimal (bewusste Entscheidung; nur die
  Welt-Zahl ist serverseitig eindeutig). Zuordnung fest im Erzeugerskript: Russland → Europa, Türkei/Kasachstan/
  Kaukasus → Asien, Zypern → Europa, Mittelamerika/Karibik → Nordamerika.
- Admin-Filter in der URL: `?days=30&surface=leaguehub`.
- Ländernamen über `Intl.DisplayNames` in der Oberflächensprache, Zahlen über `Intl.NumberFormat`.

Tests: `tests/RookHub.Api.Tests/Accounts/VisitorOriginTests.cs`, `features/visitors/*.spec.ts`.
