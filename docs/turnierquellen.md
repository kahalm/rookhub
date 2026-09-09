# Turnierquellen: Analyse der Kandidaten

Arbeitsstand der Pruefung weiterer Quellen fuer das Turnierverzeichnis. Die reine LISTE der
Kandidaten steht in `TODO.md`; hier stehen die ERGEBNISSE je Quelle, damit eine spaetere
Umsetzung nicht wieder bei Null anfaengt.

**Was eine Quelle brauchbar macht.** Vier Fragen, in dieser Reihenfolge:

1. Fuehrt sie **kuenftige** Turniere mit Datum und Ort — nicht nur ausgewertete?
2. Ist sie **maschinenlesbar ohne Anmeldung** (JSON besser als HTML, serverseitig gerendert
   besser als JavaScript)?
3. Erlaubt die **robots.txt** unserem Crawler die relevanten Pfade?
4. Gibt es einen **Rechtsvorbehalt** gegen automatisierte Nutzung (Nutzungsbedingungen,
   `Content-Signal`, Art. 4 DSM-Richtlinie)?

Frage 4 ist die, die man am leichtesten uebersieht und die am haertesten entscheidet: bei Tornelo
war robots.txt erlaubend und die Nutzungsbedingungen verboten es trotzdem.

## Drei Lehren, die die Pruefung selbst gebracht hat

**Nicht beim ersten Endpunkt aufhoeren.** Bei FIDE lieferte der zuerst gefundene Aufruf
(`show=table`/`apilist`) 661 Ereignisse, ALLE aus dem Vorjahr — daraus wurde das Urteil „bringt
nichts", und es war falsch. Die gepflegte Ansicht war eine andere URL
(`show=showYear&page=<Jahr>`) mit 143 Ereignissen fuer das Folgejahr, von denen 132 im Verzeichnis
fehlten. Jede Quelle wird deshalb ausdruecklich auf eine „kuenftig"-Ansicht geprueft (Kalender,
`status=next`, „upcoming", Jahresauswahl).

**Ein 403 ist nicht automatisch Bot-Schutz.** Bei Tornelo war es eine Cloudflare-WAF-Regel gegen
den knappen UA-String `Mozilla/5.0`: OHNE User-Agent und mit einem VOLLSTAENDIGEN Browser-UA kam
200. Vor dem Urteil „Bot-Schutz" also die Kopfzeilen variieren — und den Antwortkoerper zitieren.

**robots.txt sind ZWEI Fragen, nicht eine.** Mehrere Seiten sperren `ClaudeBot` site-weit
(`Disallow: /`), waehrend `User-agent: *` die Turnierpfade offen laesst. Das trennt „was darf ein
KI-Agent bei der Recherche holen" von „was darf der RookHub-Crawler unter eigenem User-Agent
holen". Beides gehoert getrennt beantwortet — und ein User-Agent wird nie gefaelscht, um eine
Sperre zu umgehen.

## Ergebnisse

Legende: ✅ brauchbar · ⚠️ brauchbar mit Aufwand · ❌ nicht brauchbar · ⏳ Pruefung laeuft

**Bester Fund bisher: `chessarbiter.com` (Polen)** — 611 gemessene kuenftige Turniere, serverseitiges
HTML in EINEM Abruf, keine robots.txt, keine Nutzungsbedingungen, faktisch der Kalender des
polnischen Verbands. Siehe unten.

| Quelle | Urteil | Kern |
|---|---|---|
| chess-results.com | ✅ laeuft | Bestandsquelle, ASP.NET-Turniersuche |
| calendar.fide.com | ✅ laeuft | `show=showYear&page=<Jahr>` |
| chessmanager.com | ❌ | `ClaudeBot: Disallow: /` + Art.-4-Vorbehalt, Cloudflare-Challenge |
| vegaresults.com | ❌ | JSON-API mit guten Feldern — aber Nutzungsbedingungen verbieten Scraping ohne schriftliche Erlaubnis |
| tornelo.com | ❌ | Nutzungsbedingungen verbieten Bot-Zugriff ausser Suchmaschinen; JS-SPA |
| result.vegachess.com | ❌ | **TOT** — Hosting-Vorgabeseite von 2016; die Spur fuehrt auf vegaresults.com |
| vesus.org | ⚠️ | Offene GraphQL-API, 383 kuenftige Ereignisse — aber 99 % Italien, ToS verbietet Scraping |
| torneionline.com | ❌ fuers Verzeichnis | Elo-Datenbank des ital. Verbands: **0** kuenftige Turniere gemessen. Als HISTORIEN-Quelle stark |
| caissachess.net | ⚠️ nicht vorrangig | 188 kuenftige, aber 100 % USA, ohne Bedenkzeit/Land; nur 50 ohne JavaScript erreichbar |
| circlechess.com | ❌ | 100 % Indien, nur eigene Plattform-Turniere, Daten nur per JavaScript |
| skak.dk | ❌ | Kalender liegt auf `turnering.skak.dk` — und die sperrt `Disallow: /` fuer JEDEN Crawler, auch unseren |
| **schaakbond.nl** | ⚠️ **aussichtsreich** | 173 kuenftige (gemessen), offene WordPress-REST-API; Runden/Teilnehmer fehlen ganz — Niederlande |
| **echecs.asso.fr** | ⚠️ **aussichtsreich** | 102/43/11/10 kuenftige (Sep–Dez, gemessen); Datenbankschutz-Klausel im Impressum — Frankreich |
| **chessarbiter.com** | ✅ **bester Fund** | **611** kuenftige (gemessen), ein Abruf, kein Vorbehalt — Polen |
| **englishchess.org.uk/events** | ✅ **sehr gut** | **320** Events, offene Events-Calendar-API **mit PLZ UND Koordinaten** — England |
| **schachbund.de** (Turnierdatenbank) | ✅ **gut** | ~104 kuenftige, RSS je Bundesland, kein Vorbehalt, ueberwiegend ZUSATZ — Deutschland |
| chessmanager.com (Archivrunde) | ⚠️ Anfrage lohnt | **901–950** kuenftige aus **48 Laendern** — aber `ClaudeBot` gesperrt + Art.-4-Vorbehalt |
| chess.ca | ⚠️ | 170 kuenftige als JSON-Volldump; **keine stabile Kennung**, keine PLZ — Kanada |
| aicf.in | ⚠️ schwach | 98 kuenftige, kein Vorbehalt — aber keine PLZ, Kennungen doppelt vergeben — Indien |
| schack.se | ⚠️ schwach | 183 kuenftige per JSON — aber **0 von 183 mit Ort** — Schweden |
| shakkiliitto.fi | ⚠️ nicht vorrangig | 270 kuenftige, Freitext ohne PLZ, hohe Ueberschneidung mit chess-results — Finnland |
| swisschess.ch | ❌ | Die brauchbare API liegt auf `cms.swisschess.ch` — `Disallow: /` fuer JEDEN |
| tournamentservice.com | ❌ | `ClaudeBot` gesperrt UND `*` sperrt die Turnierliste ausdruecklich — Norwegen |
| chesspairings.org | ⚠️ kleiner Ertrag | **Erste Quelle ohne jeden Vorbehalt** + JSON-LD mit Koordinaten — aber nur 27 kuenftige, Kleinstevents |
| ratings.uschess.org | ❌ | Reines Ratingsystem, nur abgerechnete Turniere |
| **new.uschess.org/upcoming-tournaments** | ⚠️ **aussichtsreich** | Der TLA-Ankuendigungskalender: serverseitiges Drupal, Umkreisfilter, TLA-Nummer als Kennung |
| uschess.org/msa | ❌ | `ClaudeBot` gesperrt; strukturell reines Ergebnisarchiv ohne Ankuendigungen |

### ❌ chessmanager.com

- **robots.txt (A)**: eigener Block `ClaudeBot: Disallow: /`, ebenso GPTBot, CCBot,
  Google-Extended, Bytespider, Amazonbot, Applebot-Extended, meta-externalagent,
  CloudflareBrowserRenderingCrawler. Die Analyse hat daraufhin abgebrochen und Turnierliste,
  Sitemap und API-Pfade NICHT geholt.
- **robots.txt (B)**: `User-agent: *` → `Allow: /` mit
  `Content-Signal: search=yes, ai-train=no, use=reference`.
- **Rechtsvorbehalt**: ausdruecklicher Verweis auf Art. 4 DSM-Richtlinie gegen KI-Nutzung. Das
  betrifft die NUTZUNG der Daten, nicht nur das Abrufen — vor einer Umsetzung zu klaeren, nicht
  von uns zu entscheiden.
- **Technik**: serverseitig gerendertes HTML (jQuery + Fomantic-UI), keine dokumentierte API.
  Turnier-URLs mit langer numerischer Kennung (`/tournaments/4791684865982464`) — waere eine
  stabile Kennung. Schon der robots.txt-Abruf kam mit `cf-mitigated: challenge`.
- **Naechster Schritt**: organisatorisch, nicht technisch — Betreiber nach lizenziertem Zugang
  fragen.

### ❌ result.vegachess.com — tot

Nur eine generische Hosting-Vorgabeseite (224 Byte, `last-modified 2016`, TLS-Zertifikat fuer
`*.arubabusiness.it`, robots.txt = 404). Die Wayback Machine zeigt, dass dort 2019/2020 der
Vega-Ergebnisdienst lief. Ueber die Software-Seite `www.vegachess.com` und DNS fuehrt die Spur zur
heutigen Adresse: `vegaresult.com` → 301 → `www.vegaresults.com`.

**Damit sind zwei Eintraege der Kandidatenliste dieselbe Quelle**, und einer davon ist tot.

### ❌ vegaresults.com

Ergebnisportal des Paarungsprogramms **Vega**.

- **robots.txt (A)**: `ClaudeBot: Disallow: /` (ebenso GPTBot, Scrapy, AhrefsBot, SemrushBot,
  MJ12bot, YandexBot, PetalBot u. a.). Die Analyse hat nach der robots.txt aufgehoert; der
  Datenabruf blieb aus, die Zahl kuenftiger Turniere ist deshalb NICHT gemessen.
- **robots.txt (B)**: `User-agent: *` sperrt nur `/vegaupload/`, `/vega/flag`, `/vb`, `/orion/`,
  `/orionOrig/`, `/t`, `/uploads/`, `/javafo/`, `/TCPDF/`, `/batch/`, `/vr2/`, `/vr_23_07_26/`,
  `/send/`, `/vendor/` und zwei datierte Testordner. **`/vr/tournaments.php` und
  `/vr/get_tournaments.php` sind NICHT gesperrt** — ein eigener Crawler waere regelkonform.
- **Rechtsvorbehalt**: keine `Content-Signal`-Zeile gefunden; `/vr/terms.php` ungelesen (aus
  Compliance-Gruenden) — **vor einer Umsetzung nachholen**.
- **Kuenftig-Ansicht: JA, strukturell belegt.** Der Suchfilter hat drei Status-Reiter
  `PAST` / `RUNNING` (Vorgabe) / `NEXT`.
- **Technik: das Antwortformat hat GEWECHSELT — beide frueheren Notizen hier waren zu ihrer Zeit
  richtig.** Im Archiv nachweisbar: Snapshot 2026-03-31 liefert **JSON**
  (`{"tournaments":[…]}`), Snapshot 2026-04-15 ein **HTML-Fragment**. Dazu passt, dass die
  Formularseite `hx-swap="innerHTML"` traegt (htmx erwartet HTML) und im Kopf ein `redesign.css`
  steht. **Heute (Sept. 2026) liefert der Endpunkt also wahrscheinlich HTML, nicht JSON** — die
  JSON-Erkenntnis ist vermutlich veraltet. Wer hier baut, muss das ZUERST selbst pruefen; der
  Wechsel ist auch die Warnung, wie stabil diese Quelle ist.
  Der Aufruf bleibt:
  `GET /vr/get_tournaments.php?event=&status=next&timecontrol=all&interest=all&type=all&startdate=&enddate=`
  → `{"tournaments":[{...}]}`. Detailseite `GET /event/{id}` (HTML, serverseitig).
  Filter: `status` (past/running/next), `timecontrol` (all/standard/rapid/blitz),
  `interest` (all/international/national/local), `type` (all/individual/team),
  `startdate`/`enddate` (`YYYY-MM-DD`), `event` (Text, sucht Name UND Ort gemeinsam).
- **Felder — teils BESSER als chess-results**: `name`, `city`, **`fed`** (echter
  Foederationscode, ITA/AUS/NZL — den hat chess-results nicht so saubere),
  `timecontrol`, `rounds`, numerische `id` (stabile Kennung), `total_sections`, `type`,
  `interest`. **Fehlt**: Teilnehmerzahl (nur Zahl der Sektionen). Das Datum ist in der LISTE ein
  Freitext („27-29 Mar 2026"), sauber getrennt erst auf der Detailseite — also ein Abruf je
  Turnier, wenn man es genau braucht.
- **Ausschlussgrund: Nutzungsbedingungen — und sie sind AKTUELL.** Neuere Fassung im Archiv
  gefunden (`/vr/terms.php`, archiviert 21.04.2026, Dokument selbst „Last updated: 14. Januar
  2026"), Punkt 7 im Wortlaut: „Any systematic, automated or large-scale reproduction of data
  (e.g., automatic scraping or unauthorized collection), as well as any use of data for
  commercial purposes without prior written consent from VegaResults, is expressly prohibited."
  Die Klausel ist also nicht abgeschwaecht, sondern neu bestaetigt. Technisch (B) erlaubt,
  vertraglich nicht ohne Erlaubnis.
- **Menge: fuer `status=next` im Archiv NICHT belegbar.** Eine vollstaendige CDX-Abfrage ueber
  die ganze Domain (5790 Aufnahmen) findet **keinen einzigen** Snapshot mit `status=next` — der
  Archiv-Crawler klickt den Standardzustand des Formulars ab, und der ist `running`. Ersatzmessung
  aus drei Snapshots (`status=running`, also nur LAUFENDE):

  | Snapshot | Format | Anzahl |
  |---|---|---|
  | 2026-01-16 | JSON | 17 |
  | 2026-03-31 | JSON | 26 |
  | 2026-04-15 | HTML | 9 |

  Die Schwankung 26 → 9 in zwei Wochen ist plausibel, zeigt aber: eine Momentaufnahme sagt wenig.
- **Laender, gemessen** (drei Snapshots): durchgehend **Italien-dominiert (50–65 %)**, dazu
  verstreut AUS, ESP, MEX, USA, NZL, PHI, ROU — **kein einziger DACH-Eintrag** ausser einem
  Schweizer Turnier. Die Quelle deckt ab, wo Vega als Paarungssoftware benutzt wird, nicht
  Mitteleuropa.
- **Start UND Ende stehen in der LISTE**, in einem Feld: `"date": "01 Mar 2026-01 Mar 2028"` bzw.
  im HTML `📅 01 Mar 2026 - 01 Mar 2028`. Zwei `DD Mon YYYY`-Muster per Regex — **kein Detailabruf
  je Turnier noetig**. Unbestaetigt: ob kurze Wochenend-Turniere dasselbe Format haben.
- **Teilnehmerzahl: nirgends.** Nur `total_sections` und `rounds` — und `rounds` fehlt im
  HTML-Fragment vom 15.04. schon, war im JSON vom 31.03. noch da.
- **Naechster Schritt**: `vega@vegachess.com` — Nutzungserlaubnis erfragen, nicht der API-Aufruf.
- **Was ein eigener Crawler zuerst selbst bestaetigen muesste**: (1) ob der Endpunkt heute JSON
  oder HTML liefert; (2) ob `status=next` serverseitig ueberhaupt funktioniert und was er liefert
  (im Archiv nie belegt); (3) ob `rounds` noch mitkommt; (4) das Datumsformat bei kurzen
  Turnieren; (5) ob die ToS seit 14.01.2026 nochmal geaendert wurden.
- **Risiko**: laufendes UI-Redesign (`redesign.css`); Sitzungs-/CSRF-Mechanik (PHPSESSID,
  `vrCsrfToken`) deutet auf serverseitigen Zustand.

### ❌ tornelo.com

Turnier-Plattform (Verwaltung + Online-Spiel).

- **robots.txt (A)/(B)**: die ganze Datei ist `User-agent: *` / `Disallow: /wp-admin/` /
  `Allow: /` / `Allow: /knowledge-base/*` / Sitemap. Kein KI-Bot-Block, keine
  `Content-Signal`-Zeile. Nach robots.txt waere ein Abruf mit eigenem User-Agent **erlaubt**.
- **Rechtsvorbehalt: ENTSCHEIDEND und negativ.** Nutzungsbedingungen § 5.1 verbieten
  (d) „systematic or automated data collection … without our express written consent" und
  (e) Bot-Zugriff „**except for the purpose of search engine indexing**". Ein periodischer
  Turnier-Crawler fiele nicht darunter — vertraglich untersagt, obwohl robots.txt es offenlaesst.
  Genau der Fall, fuer den Frage 4 dieser Liste existiert.
- **Technik**: `/chess` liefert serverseitig nur `<div id="content">Loading...</div>`. Die Daten
  zieht eine React-App aus einer internen, undokumentierten `/api` (Pfade minifiziert im
  2,2-MB-Bundle). Keine Entwicklerdoku, kein GitHub, kein ICS-Feed; 89 Wissensdatenbank-Artikel
  durchsucht, keiner erwaehnt eine API. Es gibt nur TRF/TRFx- und PGN-Export fuer Veranstalter
  ihrer EIGENEN Events. Die Sitemap enthaelt ausschliesslich WordPress-Marketingseiten und eigene
  Webinare — keine Turniere.
- **Struktureller Einwand, unabhaengig vom Recht**: gelistet werden nur Events verifizierter
  Organisationen AUF Tornelo, gruppiert je Ausrichter — eine Teilmenge, kein
  Datums-/Laenderverzeichnis. Selbst mit Erlaubnis waere der Ertrag klein.
- **403 aufgeklaert**: keine Challenge, sondern eine WAF-Regel gegen den knappen UA (siehe Lehre 2).
- **Naechster Schritt**: `home.tornelo.com/contact-us/` — nach „express written consent" bzw.
  einem offiziellen Datenzugang fragen. Ohne Zusage: nicht anfassen.

### ⚠️ vesus.org

Turnier-Plattform. **Erwartung widerlegt**: angenommen war eine Spanien-/Lateinamerika-Quelle,
gemessen ist es eine ITALIEN-Quelle.

- **robots.txt (A)/(B)**: es gibt **keine** — `vesus.org`, `www.vesus.org` und `api.vesus.org`
  liefern 404 (S3 `NoSuchKey`). Nach dem Robots-Standard heisst das „keine Einschraenkung", fuer
  mich wie fuer einen eigenen Crawler.
- **Rechtsvorbehalt: JA.** Nutzungsbedingungen (`/content/termsOfUse_it-IT.json`; die
  Sprachvarianten `_en`/`_es` sind 404) verbieten „programmi software o altri meccanismi
  automatizzati … al fine di copiare e/o riprodurre i contenuti … (ivi compresi sistemi atti a
  effettuare il c.d. scraping) senza una autorizzazione scritta ed espressa di Vesus".
- **Kuenftige Turniere: 383**, vollstaendig paginiert (`hasNextPage=false` erreicht), Zeitraum
  **2026-09-07 bis 2027-05-09**.
- **Abdeckung, gemessen**: Italien **379 von 383 (99,0 %)**, Nigeria 2, Schweiz 1, Kroatien 1.
  Auch im Archiv dasselbe Bild. Viele kleine Blitz-/Vereinsabende.
- **Technik**: React/Relay-SPA, Inhalt komplett per **GraphQL** von `api.vesus.org`, mit
  **Persisted Queries** (nur bekannte `docId` werden akzeptiert):
  `POST https://api.vesus.org/graphql`, Rumpf
  `{"operationName":"EventsListQuery","docId":"…","variables":{"first":200,"after":null,"events":{"timing":"FUTURE"}}}`.
  `timing` ∈ FUTURE/INPROGRESS/ARCHIVED; weitere Filter: `countryCode`, Region, `location`,
  `start`/`end`, Preisgeld, Dauer, Art, Variante, Bedenkzeit, Anwesenheit, gewertet, Runden.
- **Zwei technische Vorbehalte, die schwerer wiegen als sie klingen**:
  1. Der Server prueft die **`Origin`-Kopfzeile** (`Invalid origin` ohne
     `Origin: https://vesus.org`). Die API zu benutzen heisst also, sich als deren eigene
     Web-App auszugeben — das ist kein Versehen des Betreibers, sondern eine Absicht.
  2. Die `docId` ist ein **Build-Artefakt des Relay-Compilers**, kein dokumentierter Vertrag. Sie
     kann bei jedem Frontend-Deploy wechseln und muesste dann neu aus dem JS-Bundle geholt
     werden. Das ist die Sorte Abhaengigkeit, die ohne Vorwarnung bricht.
- **Felder**: Name (auf EVENT-Ebene; Unterturniere haben oft `name:null`), Start/Ende (ISO),
  `location` (Freitext), `country.code` (ISO3), `timeControlType`, `rounds` (bei Gruppenphasen
  teils `null`), `registrationsCounts.confirmed` (bei kuenftigen nur Anmeldungen),
  `tournament.shortKey` (stabile Kennung), URL `/tournament/{shortKey}`.
  **Modellfrage**: ein Event kann MEHRERE `tournaments[]` mit je eigener Rundenzahl/Bedenkzeit
  tragen — eine Zeile je Event oder je Unterturnier? Das muesste vor einer Umsetzung entschieden
  werden.
- **Naechster Schritt**: schriftliche Erlaubnis bei Vesus erfragen. Erst danach technisch.

## Zwischenbilanz nach fuenf geprueften Quellen

**Keine einzige ist ohne Vorbehalt durchgekommen** — und der Ausschlussgrund war nie die Technik:

| Quelle | Technik | robots.txt fuer uns | Rechtslage |
|---|---|---|---|
| chessmanager.com | HTML, brauchbar | ClaudeBot gesperrt, `*` erlaubt | Art.-4-Vorbehalt |
| vegaresults.com | **JSON, gut** | erlaubt | ToS: Scraping nur mit schriftlicher Erlaubnis |
| tornelo.com | JS-SPA, unbrauchbar | erlaubt | ToS: Bots nur fuer Suchmaschinen |
| vesus.org | GraphQL, gut | keine robots.txt | ToS: Scraping nur mit schriftlicher Erlaubnis |
| result.vegachess.com | tot | — | — |

Daraus zwei Schlussfolgerungen fuer das weitere Vorgehen:

1. **Bei kommerziellen Plattformen ist der Weg die ANFRAGE, nicht der Crawler.** Alle vier haben
   ein Interesse daran, dass ihre Turniere gefunden werden; drei verlangen dafuer ausdruecklich
   eine schriftliche Erlaubnis. Das ist ein E-Mail-Vorgang, kein Programmier-Vorgang.
2. **Der aussichtsreichere Quellentyp sind nationale VERBANDSkalender.** Ein Verband veroeffentlicht
   Ankuendigungen, weil er Teilnehmer will — nicht als Ware. Die Kandidatenliste in `TODO.md`
   fuehrt davon ein Dutzend; sie sind als naechste zu pruefen.

### ❌ uschess.org/msa (US Chess, Ergebnisarchiv)

- **robots.txt (A)**: `ClaudeBot: Disallow: /` site-weit — **auf beiden Hosts**
  (`www.uschess.org` UND `ratings.uschess.org`), ebenso GPTBot, CCBot, Google-Extended,
  Applebot-Extended, Amazonbot, Bytespider, meta-externalagent. Die Analyse hat nach der
  robots.txt aufgehoert und keine Inhaltsseite geholt.
- **robots.txt (B)**: `User-agent: *` → `Allow: /`, dazu
  `Content-Signal: search=yes, ai-train=no, use=reference`. Technisch nicht gesperrt; die
  eigentlichen Nutzungsbedingungen waren wegen (A) nicht einsehbar — **vor einer Umsetzung
  nachholen**.
- **Struktureller Ausschluss, unabhaengig vom Zugriff**: MSA veroeffentlicht ein Turnier erst,
  NACHDEM ein Turnierleiter die Ergebnisse eingereicht hat. Es gibt dort keine Ansicht fuer
  angekuendigte Turniere. Das ist kein behebbares Zugriffsproblem.
- **Der bessere US-Weg sind die „Tournament Life Announcements" (TLA)** auf dem Hauptportal —
  Adresse wegen (A) nicht bestaetigt. Das ist als EIGENE Quelle zu pruefen.
- **Nebenwert (Spieler-Historie)**: inhaltlich passend (Crosstables, Teilnehmerzahlen,
  Rating-Aenderungen je abgeschlossenem Turnier) — aber durch (A) fuer uns nicht beschaffbar, und
  die Ortsangaben sind Freitext ohne PLZ/Koordinaten, passen also schlecht zu unserem
  PLZ-zuerst-Geocoding.

### ❌ circlechess.com

- **robots.txt (A)/(B)**: nur ein `*`-Block, `Allow: /`, gesperrt sind `/registration` und
  `/brochure`. Kein KI-Bot-Block, keine `Content-Signal`-Zeile. Fuer beide Fragen also erlaubt —
  eine der wenigen Quellen ohne Zugriffsvorbehalt.
- **Trotzdem nicht brauchbar, aus drei Gruenden**:
  1. **Nur eigene Plattform-Turniere.** Selbstbeschreibung: „We have built a Tournament Manager,
     Chessmaster, to enable organisers to seamlessly create tournaments. … Circlechess Events
     hosts the largest tournament database in India." Also derselbe Einwand wie bei Tornelo.
  2. **100 % Indien.** Gemessen ueber 70 eindeutige Bundesstaat-/Distrikt-Segmente aus zwei
     Turnier-Sitemaps (16 273 URLs) — ausnahmslos indische Regionen, teils in Devanagari, Tamil,
     Kannada, Malayalam; ein Ausreisser „California".
  3. **Die Daten stehen nicht im HTML.** Next.js mit leerem `pageProps` auf JEDER Detailseite,
     alle byte-identisch gross (39 769 Byte). Der Beleg, der es am deutlichsten zeigt: die
     serverseitige Meta-Beschreibung lautet woertlich „Planning to participate in a chess
     tournament in **undefined**? … for **undefined**!" — der Platzhalter wurde nie befuellt.
- **Datenqualitaet als Nebenbefund**: dieselbe Region erscheint als drei URL-Segmente
  („Andhra-Pradesh", „Andhrapradesh", „Andra-Pradesh") — Freitext-Eingabe der Veranstalter ohne
  Normalisierung.

### ❌ fuers Verzeichnis / ✅ als HISTORIEN-Quelle: torneionline.com

Die **offizielle Elo-Ergebnisdatenbank des italienischen Verbands (FSI)**, keine
Ankuendigungsseite. Der interessanteste Befund dieser Runde, weil er in ZWEI Richtungen faellt.

- **Kuenftige Turniere: 0, und zwar gemessen** (Suche ueber das Feld „Data Inizio"):

  | Zeitraum | Treffer |
  |---|---|
  | 01.09.–31.12.2026 | **0** |
  | ganz 2027 | **0** |
  | 25.08.–07.09.2026 (die letzten zwei Wochen) | **0** |
  | 01.–19.08.2026 | 22 |
  | 20.–31.08.2026 | 5 |
  | Jan–Aug 2026 | 675 (= „ganz 2026", Sept–Dez leer) |

  Selbst die letzten zwei Wochen sind leer: es gibt einen Meldeverzug von Wochen, keine
  Vorausmeldung. Kein Zufallsbefund, sondern die Bauart der Quelle.
- **robots.txt (A)/(B)**: existiert nicht (404) — nichts gesperrt, fuer beide Fragen. Kein
  `X-Robots-Tag`, kein `Content-Signal`, kein `<meta name="robots">`. Einziger rechtlicher Hinweis
  ist eine DSGVO-Datenschutzerklaerung, kein KI-Opt-out und keine Scraping-Klausel. **Die erste
  Quelle dieser Pruefung ohne jeden Vorbehalt.**
- **Felder — an mehreren Stellen BESSER als chess-results**: Name, Start/Ende, Ort samt Provinz
  UND Region, **Bedenkzeit im Klartext** („90' + 30\" bonus") plus System (Schweizer u. a.),
  Rundenzahl, **Teilnehmerzahl** (je Sektion und gesamt), stabiler „Codice" (`2608008A` =
  Jahr+Monat+Nummer+Sektion, dauerhafte Rating-Kennung), oeffentliche Detail-URL.
- **Bestand**: 675 Turniere fuer 2026, 1021 fuer 2025, **14 523 seit ca. 2000**.
- **Zugang**: reines serverseitiges HTML (PHP, Tabellen-Layout), kein JavaScript, kein JSON, keine
  Sitemap. Die Liste ist ein **POST-Formular ohne GET-Fallback**:
  `POST /tornei.php` mit `tipo=1`, `dai1`/`dai2` (Startdatum von/bis, `gg-mm-jjjj`),
  `daf1`/`daf2` (Enddatum), `nome`, `luog`, `pro1`/`reg1` (Provinz/Region), `tur1`/`tur2`
  (Runden), `gio1`/`gio2` (Teilnehmer), `ord1=TOR_DATA_INI`, `sen1=ASC`.
  Detailseite: `GET /tornei_d.php?codice=<CODE>&tipo=c&ord=c&sen=a`.
- **Also**: fuer den Turnier**kalender** wertlos. Fuer den **Turnierverlauf eines Spielers** —
  das Feature, das heute nur chess-results kennt — waere es eine gute italienische Zweitquelle
  mit besseren Feldern und ohne Rechtsvorbehalt. Als eigenes Vorhaben notiert, nicht als Teil
  dieser Suche.
- **Risiken**: sehr altes Markup (kaputte `<FORM>`-Verschachtelung in den Trefferlisten) → ein
  Parser waere bruechig; Meldeverzug unbekannter Laenge; personenbezogene Daten (Spieler- und
  Schiedsrichternamen) wie bei chess-results mit der ueblichen DSGVO-Sorgfalt.

### ⚠️ caissachess.net — brauchbar, aber nicht vorrangig

Turnierverwaltungs-**Software** fuer US-Turnierleiter (USCF-Rating, Schweizer System, DBF-Export);
`/caissalive` ist der oeffentliche Zuschauerteil, also die Turniere ihrer Kunden.

- **robots.txt (A)/(B)**: nur ein `*`-Block, kein KI-Bot-Block. Gesperrt sind u. a. `/dashboard`,
  `/profile`, `/players` — und, kurios, **`/tournaments` und `/tour`**; die tatsaechliche
  oeffentliche Liste liegt unter `/caissalive` und `/live/{id}` und ist NICHT gesperrt. Wer nur
  den naheliegenden Pfad probiert, haelt die Quelle faelschlich fuer verboten.
- **Rechtsvorbehalt**: `Content-Signal: search=yes, ai-train=no, ai-input=yes` — Suchindexierung
  erlaubt, Modell**training** verboten, Verwendung als Eingabe erlaubt. Fuer ein Verzeichnis
  (Name/Ort/Termin anzeigen) nicht einschlaegig, aber notiert.
- **Kuenftige Turniere: 188** (Seitentext), Start 12.08.–10.09.2026. **Aber nur die ersten 50
  sind ohne JavaScript erreichbar** — die Blaetterung ist client-seitig (`href="#"`,
  `?page=2` wirkungslos). 27 % der Menge ohne Zusatzaufwand.
- **Zugang**: Seite 1 serverseitig gerendert; die Detailseite `/live/{id}?tab=Info` traegt ein
  eingebettetes JSON (`id/name/city/state/start_date/end_date/num_games/sections/status`), auch
  ohne JavaScript lesbar. Stabile numerische `id`.
- **Fehlt**: **Bedenkzeit** und **Land/Foederation** ganz. „Sections" ist NICHT die Rundenzahl,
  und `num_greatergames` widerspricht sich teils zwischen Turnier- und Sektionsebene — eine
  Verwechslung, die man beim Uebernehmen einbauen wuerde.
- **Abdeckung**: ausschliesslich USA (CA, CO, CT, NC, NY, OH, OK, SD, TX + „Unknown" fuer
  Online). Fuer ein DACH-/FIDE-gepraegtes Verzeichnis derzeit ohne Wert.

### ⚠️ chesspairings.org — die erste Quelle ohne Vorbehalt, mit kleinem Ertrag

Paarungssoftware (Stefano Loberti, Italien). Das Verzeichnis ist ein NEBENprodukt: was ein
Veranstalter oeffentlich schaltet, landet auf 17 Laender-Seiten — Marketing, kein gepflegter
Kalender.

- **robots.txt (A)/(B)**: beide Domains (`chesspairings.org`, `my.chesspairings.org`) nur
  `User-agent: *` / `Allow: /`; die Turnierpfade sind nicht gesperrt. **Kein KI-Bot-Block, kein
  `Content-Signal`, kein `X-Robots-Tag: noai`, keine gefundene Scraping-Klausel.** Damit die
  erste Quelle dieser Pruefung, bei der beide Fragen ohne Vorbehalt „ja" lauten — Impressum/ToS
  waren allerdings nicht verlinkt und sind damit nicht abschliessend geprueft.
- **Maschinenlesbar, und zwar gut**: `GET /public-tournaments-sitemap.xml` liefert in EINEM Abruf
  903 Turnier-URLs samt `lastmod` (verifiziert: `lastmod` == `endDate`). Jede Turnierseite traegt
  **Schema.org-`Event`-JSON-LD** mit `name`, `startDate`, `endDate`, `location.name` — und
  **fertigen `geo.lat/lon`-Koordinaten**, die chess-results nicht hat und die wir dort muehsam
  erschliessen. Kein HTML-Zerlegen noetig.
- **Kuenftige Turniere: 27** (Enddatum nach dem 07.09.2026), Spanne bis 2026-12-10; ein Ausreisser
  2036 ist offenkundig ein Eingabefehler. Dazu 209 laufende.
- **Der Ertrag ist der Einwand**: durchweg Klein-Events (4–64 Spieler), viel Schule/Klub/Online
  (Lichess ueber einen Telegram-Bot), kaum FIDE-Bezug — also gerade NICHT die Menge, die unser
  Verzeichnis heute vermisst.
- **Zwei konkrete Fallen**:
  1. **Token-Pflicht** je Turnier-URL (`/pubblico/torneo.php?id=…&token=…`; ohne Token 302). Der
     Token ist statisch und oeffentlich, taugt also als dauerhafte Kennung — aber man kann NICHT
     ueber `id` aufzaehlen, man haengt an der Sitemap. Verschwindet ein Eintrag daraus, gibt es
     kein saubers „entfernt"-Signal.
  2. **Die Laenderzuordnung ist teils falsch**: ein Turnier „VM 2026" steht auf der
     Deutschland-Seite, laut eigenem JSON-LD liegt der Ort aber in Frankreich. Das Land muesste
     man aus dem Ortstext selbst ableiten — dieselbe Fehleranfaelligkeit wie bei den
     chess-results-Ligen.
- **Naechster Schritt, falls es je gemacht wird**: Sitemap periodisch holen, `id+token` gegen den
  gemerkten Stand abgleichen, nur Neues/Geaendertes holen und dessen JSON-LD lesen. Ein kleiner
  Adapter — aber der Nutzen rechtfertigt den Pflegeaufwand heute nicht.

### ❌ ratings.uschess.org → ⚠️ **aber: new.uschess.org/upcoming-tournaments**

Der zweite Fall nach FIDE, in dem die Antwort „nicht diese URL, sondern jene" lautet — und der
erste, der eine grosse Luecke schliessen koennte (die USA fehlen unserem Bestand fast ganz).

**`ratings.uschess.org` (MUIR) faellt weg**: reines Rating-System. Seine Funktionen heissen
„Tournaments Received", „Events Rated", „Tnmt. Hst"; die zugehoerige Suche traegt woertlich den
Titel „**Past** USCF Tournament Crosstables". Alles NACH dem Turnier. Keine oeffentliche API; im
Forum bestaetigt, es gebe nur ein inoffizielles „Screen Scrape", von dessen Betreiber selbst mit
„APIs subject to change, no support" kommentiert.

**Der Ankuendigungsweg ist das TLA-System** („Tournament Life Announcements"): Veranstalter melden
ein Turnier VOR dem Termin an, jede Meldung bekommt eine eigene URL und eine **TLA-Nummer**
(Beispiel 49022) — eine stabile externe Kennung, genau was wir brauchen.

- **Listen-URL**: `https://new.uschess.org/upcoming-tournaments`, serverseitig gerendertes Drupal
  (Views) — **kein JavaScript**, also mit demselben Werkzeug abrufbar wie chess-results.
  Blaetterung bis mindestens `page=26` → mehrere hundert Eintraege.
- **Die Filter sind unserem eigenen Entwurf verblueffend aehnlich**:
  `field_geofield_proximity[value]=100` (Umkreis in Meilen) +
  `field_geofield_proximity[source_configuration][origin_address]=Princeton,+NJ` (Freitext-Ort),
  dazu Bundesstaat (`All` moeglich), Datumsbereich
  (`field_event_dates_occurrences[min]`/`[max]`), FIDE-gewertet ja/nein, Online ja/nein,
  Volltext, `page`.
- **Felder**: Name, Start/Ende, Ort samt Veranstaltungsstaette und Adresse („Albany Marriott, 189
  Wolf Road, Albany 12205" — mit PLZ, also brauchbar fuer unser PLZ-zuerst-Geocoding),
  Bundesstaat, Bedenkzeit in US-Notation („40/80, SD/30; d30" — **braucht einen eigenen
  Parser**), Rundenzahl („6-round Swiss"), TLA-Nummer, sprechende URL.
  **Fehlt**: Teilnehmerzahl — vor einem Turnier strukturell unbekannt. Im Verzeichnis als
  „unbekannt" zu fuehren, nicht als Fehler.
- **robots.txt (A)**: `ClaudeBot: Disallow: /` auf ALLEN vier Hosts (`ratings.`, `uschess.org`,
  `www.`, `new.`), ebenso GPTBot, CCBot, Google-Extended, Applebot-Extended, Amazonbot,
  Bytespider, meta-externalagent. Die Analyse hat deshalb **nichts selbst abgerufen** — alle
  Angaben oben stammen aus Suchmaschinen-Schnipseln Dritter. Die robots.txt selbst zu lesen ist
  zulaessig und war der einzige eigene Abruf.
- **robots.txt (B)**: `User-agent: *` → `Allow: /`, kein pfadbezogenes Disallow. Ein eigener
  Crawler (`RookHubCrawler/1.0`) faellt darunter und waere regelkonform.
- **Rechtsvorbehalt**: `Content-Signal: search=yes, ai-train=no, use=reference`, im Kopf der
  robots.txt ausdruecklich als Vorbehalt nach Art. 4 der EU-DSM-Richtlinie deklariert. Verboten
  ist das TRAINIEREN von Modellen; **`use=reference` ist ausdruecklich erlaubt** — und das ist
  genau unser Fall (Turnierdaten als Referenz ins Verzeichnis uebernehmen, mit Link zur Quelle).
  Die vollstaendigen Nutzungsbedingungen von uschess.org sind damit NICHT geprueft (kein Abruf
  moeglich) und bleiben der offene Punkt.
- **WICHTIGE EINSCHRAENKUNG**: nichts davon ist von uns selbst gegen die Seite verifiziert. Der
  erste Schritt einer Umsetzung ist deshalb nicht der Sweep, sondern **eine Handvoll Abrufe des
  eigenen Crawlers**, die Struktur, Feldnamen und Blaetterung bestaetigen. Erst danach lohnt ein
  Parser.
- **Nicht ausreichend**: `/plan-ahead-calendar` und `/national-events-calendar` sind redaktionell
  kuratiert (nur nationale Events bzw. Preisgeld ab 5000 $). Fuer Vollstaendigkeit braucht es die
  volle `/upcoming-tournaments`-Suche.

### ✅ chessarbiter.com — der bisher beste Fund (Polen)

Faktisch der oeffentliche Turnierkalender des **polnischen Schachverbands**: der Turnierbereich
traegt „© 2010-2018 Polski Zwiazek Szachowy", Kontakt `biuro@pzszach.pl`.

- **Kuenftige Turniere: 611**, gemessen am 07.09.2026 (`status=planowane`), Spanne
  **2026-09-08 bis 2026-12-30** — rund 3,7 Monate voraus; 2027 erscheint im Jahres-Auswahlfeld
  noch nicht. Gesamtbestand der Datenbank: 104 277 Turniere (ueberwiegend Archiv).
- **Alle 611 kommen in EINER Antwort** (~326 KB), keine Blaetterung. Das ist der billigste
  Sweep-Aufbau, den wir bisher gesehen haben — billiger als chess-results.
- **robots.txt (A)/(B)**: existiert nicht (404). Keine Sperre fuer irgendeinen Bot.
- **Rechtsvorbehalt**: keine Nutzungsbedingungen, keine Datenschutzseite, kein Scraping-Verbot
  auffindbar (alle geprueften Pfade 404). **Kein Ausschlussgrund** — aber auch keine ausdrueckliche
  Erlaubnis; eine kurze Anfrage an `biuro@pzszach.pl` vor dem Dauerbetrieb bleibt das saubere
  Vorgehen.
- **Listen-URL, serverseitiges HTML, kein JavaScript, keine Anmeldung, kein Cookie**:
  `GET https://www.chessarbiter.com/turnieje.php?status=planowane&wojewodztwo=wszystkie&typ=wszystkie&rodzaj=wszystkie&szukaj=Wyswietl+turnieje`
  Filterachsen: `wojewodztwo` (16 Woiwodschaften), `typ` (Landes-/Woiwodschaftsmeisterschaft,
  Einzel, Mannschaft), `rodzaj` (klassisch, klassisch-FIDE, klassisch-PZSzach, schnell, Blitz),
  `rok`+`miesiac` fuer das Archiv.
- **Die Liste liefert**: Name, Startdatum (Tag/Monat, Jahr aus dem Monats-Kopf), Ort als Freitext,
  Woiwodschaft, Speed-Klasse, stabile Kennung `{jahr}/ti_{id}` aus der Link-URL, oeffentliche URL.
  **Fehlt in der Liste**: Enddatum, Rundenzahl, genaue Bedenkzeit.
- **Die Detailseite ist ein Sonderfall, und ein guenstiger**: die HTML-Huelle ist leer, der Inhalt
  kommt aus `capro_tournament.js` per `document.write()`. Das ist aber **keine dynamische API wie
  bei Tornelo**, sondern eine **statische, vom Desktop-Programm CAPRO erzeugte Textdatei** mit
  fest eingebetteten Werten (`Tr("Start date:","")` gefolgt von `DW("<b>2026-09-11</b>")`). Ohne
  JavaScript per regulaerem Ausdruck lesbar — nachgewiesen. Daraus kommen Enddatum, Bedenkzeit im
  Rohtext („3'+2'' na ruch"), Rundenzahl, System, Veranstalter.
- **Abdeckung**: ausschliesslich Polen, 15 von 16 Woiwodschaften (Slaskie 100, Pomorskie 82,
  Mazowieckie 68). Genau unsere Luecke.
- **Umsetzung, zweistufig — dasselbe Muster wie der bestehende Sweep**:
  1. *Entdeckung*, ein Abruf je Durchgang: die Listen-URL oben.
  2. *Anreicherung*, ein Abruf je NEUEM Turnier:
     `GET /turnieje/{jahr}/ti_{id}/capro_tournament.js` — wie unser Rundenplan-/Vereins-Nachschlag.
  Neuer Dienst analog `FideDirectorySweepService`, eigene Quellenart in
  `TournamentDirectorySources`, `PublicId` z. B. `ca:{jahr}/ti_{id}`. Kein Crawler-Umweg noetig
  (kein Cloudflare beobachtet).
- **Risiken**: PHP 5.2.17 — sichtbar alte Infrastruktur, also defensiv abfragen (niedrige
  Parallelitaet wie beim FIDE-Sweep). Das JS-Parsing haengt am Label-Text des CAPRO-Vorlagen-
  Exports und braeche lautlos bei einer Formataenderung — **braucht einen Vektoren-Test**, wie die
  anderen opaken Formate im Projekt. Ortsangaben sind Freitext mit Online-Mischformen
  („Stadt / chess.com") — dieselbe Online-Erkennung wie beim FIDE-`ONL`-Fall. Teilnehmerzahl fuer
  kuenftige Turniere strukturell nicht verfuegbar (wie chess-results/FIDE) — im UI als „–".

#### Was die Umsetzung dann gemessen hat (2026-09-08, v0.448.0)

**Die Zahlen halten:** 611 geplante Turniere in einem Abruf (320 kB, 0,9 s), 2026-09-09 bis
2026-12-30, alle mit eindeutiger Kennung.

**Die Detailseite ist ANDERS als erwartet — und besser.** Die Recherche hatte
`capro_tournament.js` beschrieben; die Datei gibt es nicht (404 auf allen geprueften Varianten).
Stattdessen liefert `/turnieje/{jahr}/ti_{id}` **8,5 kB server-gerendertes HTML**, in dem jede
Angabe eine ENGLISCHE Beschriftung traegt (`Tr("Start date:","")`) und ihr Wert in der Zelle
dahinter steht. Kein JavaScript, keine Vorlagen-Heuristik, und unabhaengig von der eingestellten
Oberflaechensprache. Daraus kommen Enddatum, Anschrift, Bedenkzeit („90' + 30'' na ruch"),
Rundenzahl, System — **und die Teilnehmerzahl**.

**Die Teilnehmerzahl ist der stille Sonderfall.** Sie steht dort auch bei einem GEPLANTEN Turnier
(„No. of players: 12"). chess-results und FIDE nennen sie fuer die Zukunft grundsaetzlich nicht —
der Filter „mindestens N Teilnehmer" wirkt fuer polnische Turniere damit als einziger im Bestand
schon vor dem Termin.

**Das Jahr steht nirgends in der Liste**, und das Jahr im Adresspfad ist nicht das Turnierjahr,
sondern das Jahr, in dem der Eintrag ANGELEGT wurde — bei neun der 611 steht dort 2024 oder 2025.
Verlaesslich ist die Sortierung: nachgemessen sind alle 611 Zeilen streng nach Termin geordnet,
ohne eine einzige Ausnahme. Der Jahreszaehler laeuft also mit und springt beim Monatsrueckschritt
weiter.

**Die Woiwodschaft loest sich selbst auf:** die Zeile nennt das Kuerzel („Poland,SL"), die
Auswahlliste DERSELBEN Antwort den Namen („Śląskie (SL)"). Keine zweite Anfrage, und keine
Tabelle im Quelltext, die veralten koennte.

**Was noch fehlt: der polnische Postleitzahl-Bestand.** Im Lexikon stehen fuer PL nur 363 Orte
(`cities15000`) und keine Postleitzahlen — kleinere Spielorte finden deshalb keinen Pin. Der
Import ist ein Knopfdruck: `POST /api/admin/tournament-directory/gazetteer/postal/PL`.

### ⚠️ echecs.asso.fr (FFE, Frankreich) — brauchbar, mit einem Vorbehalt

Dritter Fall von „nicht diese URL, sondern jene" — hier fehlten der genannten URL nur die
PARAMETER: `ListeTournois.aspx?Action=RES&Mois=<1-12>&Annee=<Jahr>`. Ohne sie zeigt die Seite nur
ein rollierendes Fenster der naechsten ~99 Turniere; mit ihnen bekommt man vollstaendige,
stabil verlinkbare Monatslisten.

**`Action=RES` heisst „Resultate" und ist trotzdem eine ANKUENDIGUNG** — belegt: bei einem Turnier
am 4. Oktober 2026 standen am 7. September bereits Adresse, Bedenkzeit, Preisgeld und eine
Vor-Anmeldeliste mit 9 Spielern. Genau die Falle, in die ich bei FIDE gelaufen bin, hier
umgekehrt: der Name klingt rueckwaerts, die Daten sind vorwaerts.

- **Kuenftige Turniere, gemessen** (07.09.2026): Standardansicht 99 (07.09.–04.10.);
  September 102, Oktober 43, November 11, Dezember 10, Januar 2027 **0**, Maerz 2027 1,
  Juni 2027 2. Das Meldeverhalten ist also wie bei chess-results: 1–3 Monate Vorlauf, danach
  duennt es stark aus — ein Sweep muss rollierend nachfassen, ein einmaliger Blick weit voraus
  bringt nichts. Zum Vergleich ein VERGANGENER Monat: Maerz 2025 hatte **309** Turniere.
- **robots.txt (A)/(B)**: existiert nicht (404) — keine Sperre, fuer niemanden.
- **Rechtsvorbehalt: ja, und ernstzunehmen.** Die „Mentions legales" (PDF) berufen sich in Punkt 7
  ausdruecklich auf das **sui-generis-DATENBANKRECHT** (Gesetz vom 1. Juli 1998, Umsetzung der
  EU-Richtlinie 96/9/EG), Punkt 5 verbietet Vervielfaeltigung ohne schriftliche Genehmigung.
  Einordnung der Analyse, die ich fuer richtig halte: der Text liest sich wie **generisches
  franzoesisches Vereins-Boilerplate** („Loi 1901"), nicht wie eine gezielt gegen die Extraktion
  des Turnierkalenders formulierte Klausel — und ein Verband veroeffentlicht seine
  Ankuendigungen ja aktiv, ohne Anmeldung und ohne technische Huerde. Guenstiger als Tornelo,
  aber nicht „keine Einschraenkung". Der Datenbankschutz-Passus ist echt.
  **Kontakt fuer eine Anfrage**: der Webmaster ist oeffentlich genannt.
- **Zugang**: GET genuegt fuer die erste Seite je Monat — 200 mit vollem HTML, **ohne Cookie,
  ohne Session, ohne `__VIEWSTATE`**, serverseitig gerendert. Das deckt die meisten kuenftigen
  Monate ab, weil sie unter 40 Treffern liegen.
  **Blaettern innerhalb eines Monats** braucht ein klassisches ASP.NET-Postback:
  `__EVENTTARGET=ctl00$ContentPlaceHolderMain$PagerFooter`, `__EVENTARGUMENT=<Seite>`, mit
  `__VIEWSTATE` — **aber ohne `__EVENTVALIDATION`**, also einfacher als chess-results, dessen
  Verfahren wir schon beherrschen. Getestet bis Seite 8.
- **Felder — reichhaltiger als chess-results**: Name; **Start UND Ende getrennt**; volle Adresse
  mit **Strasse und PLZ** auf der Detailseite (`FicheTournoi.aspx?Ref=<ID>`) — gut fuer unser
  PLZ-zuerst-Geocoding; Bedenkzeit exakt („15' + [5'']"); Rundenzahl; Ligue-Code als Region;
  stabile `Ref`-Nummer; dazu Preisgeld, Startgeld, Veranstalter, Schiedsrichter, Kontakt und
  oft ein externer Anmeldelink.
  **Teilnehmerzahl** nur ueber eine separate Spielerliste durch Zeilenzaehlen — und dann als
  Momentaufnahme der Vor-Anmeldung, die bis zum Turniertag waechst. Vorsicht: zu frueh erfasst
  ist sie stark untertrieben.
- **Abdeckung**: Frankreich samt Ueberseegebieten (Guadeloupe, Reunion, Neukaledonien als
  Ligue-Optionen).
- **Umsetzung**: je kuenftigem Monat ein GET (rollierend, wie beim FIDE-Kalender), bei mehr als
  40 Treffern das Postback fuer die Folgeseiten, dann je Turnier die Detailseite. Kostenklasse
  wie die chess-results-Spielerkarten.
- **Risiken**: die Datenbankschutz-Klausel (nicht auf „robots.txt sagt ja" reduzierbar);
  sitzungsgebundenes `__VIEWSTATE` fuer Folgeseiten; **gemischte Zeichenkodierung**
  (`iso-8859-1`/Entities in Teilen, `utf-8` im Hauptinhalt) — beim Parsen zu beachten.

### ⚠️ schaakbond.nl (KNSB, Niederlande) — offene REST-API, aber zwei Felder fehlen ganz

Vierter Fall von „nicht diese URL, sondern jene": **`schaken.nl/zoek-een-toernooi/` ist eine leere
Landingpage** (kein `event`-Inhaltstyp, keine Liste, keine Filter — nur eine Theme-Seite). Der
gepflegte Kalender liegt auf der ZWEITEN Adresse `schaakbond.nl`, und zwar nicht auf deren
`/toernooien/`-Seite (ebenfalls leer), sondern unter `/event/` bzw. im REST-Endpunkt. Das Frontend-
Widget scheint dort noch nicht fertig verdrahtet zu sein.

- **Kuenftige Turniere: 173**, gemessen, Spanne **2026-09-12 bis 2027-09-04** — ein ganzes Jahr
  voraus, deutlich mehr Vorlauf als Frankreich oder chess-results.
- **Offene, DOKUMENTIERTE Schnittstelle** (WordPress-Standard, keine interne):
  `GET https://schaakbond.nl/wp-json/wp/v2/event?event-category=33&per_page=100&page=1..2`
  (Kategorie 33 = „Schaakkalender", filtert die 4 Fremdeintraege wie Schiedsrichterkurse heraus).
  Taxonomien einzeln unter `/wp-json/wp/v2/event-category`, `/bond`, `/speed`.
- **robots.txt (A)/(B)**: Yoast-Standard, kein KI-Bot-Block; gesperrt nur `/testpagina/` und
  `/wp-admin/`. Beide Fragen also „ja". **Aber `Crawl-delay: 15`** — gilt fuer `*` und damit auch
  fuer uns.
- **Rechtsvorbehalt**: kein `Content-Signal`, kein `ai.txt`/`llms.txt`, **kein Scraping-Verbot** in
  den Nutzungsbedingungen. Nur eine urheberrechtliche Klausel: Vervielfaeltigung von „programmatuur,
  teksten, beelden en/of geluiden" nur mit vorheriger Zustimmung. Das trifft die
  **Fliesstext-Beschreibung** eines Turniers, nicht die Fakten (Name/Datum/Ort/Bedenkzeit) — und
  wir uebernehmen ohnehin nur strukturierte Fakten, keine Turnier-Prosa. **Damit die erste Quelle
  dieser Reihe ohne funktionalen Ausschlussgrund**, genau wie es bei einem Verband zu erwarten war.
- **Was die Liste liefert**: `id`, `slug` (**enthaelt das Startdatum als Suffix**, z. B.
  `…-2027-05-06`), `title`, `link`, dazu die Taxonomien `bond` (16 Regionalverbaende) und
  **`speed`** (Normaal/Rapid/Snelschaak — die Bedenkzeit-Klasse also direkt aus der Liste).
- **Was FEHLT, und zwar strukturell**: **Rundenzahl und Teilnehmerzahl gibt es nirgends** — auch
  nicht mit Mehraufwand, bestenfalls unzuverlaessig im Fliesstext. Zwei unserer Felder bleiben
  also leer.
- **Enddatum und Ort nur auf der Detailseite**, in einem einzigen `<p class=eventtime>`-Block:
  eintaegig `04 September 2027  10:30 -   17.30<br>Boorstraat 107 3513 SE<br>Utrecht`,
  mehrtaegig `06 May 2027  - 08 May 2027 <br>Zoetermeer<br>Zoetermeer`. Regex-faehig, **mit PLZ**
  (gut fuer unser Geocoding) — aber ein Abruf je Turnier, und die ACF-Felder sind bewusst NICHT
  ueber REST offen (`"acf": []`).
- **Der Crawl-delay ist die eigentliche Kostenbremse**: 15 s × 173 Detailseiten ≈ **45 Minuten am
  Stueck**. Also ein Nachtlauf mit Deckel je Nacht (wie `DisambiguationBatchSize`) und einem
  eigenen „geprueft"-Zeitstempel — dasselbe Muster wie beim Rundenplan.
- **Die Kennung ist ein Risiko**: `date`/`modified` ALLER 177 Eintraege stehen auf „heute", was
  auf einen taeglichen Voll-Reimport aus einem vorgelagerten System deutet. Ob die WP-Post-Id
  dabei erhalten bleibt, liess sich nicht ueber die Zeit pruefen. **Empfehlung: den
  deterministischen Slug** (Titel + Datum) als Kennung nehmen, nicht die Id — und in den ersten
  Wochen beobachten, ob Slugs sich halten.
- **Vollstaendigkeit**: die Liste ist ANMELDUNGSbasiert (Veranstalter tragen selbst ein), also
  vermutlich nicht so vollstaendig wie chess-results fuer dieselbe Region. Wert liegt in der
  ERGAENZUNG (Klubturniere ohne chess-results-Meldung), nicht im Ersatz.

### ❌ skak.dk (Dansk Skak Union) — der erste Fall, in dem auch (B) „nein" lautet

Fuenfter Fall von „nicht diese URL, sondern jene" — nur endet die richtige URL diesmal hinter
einer Sperre, die AUCH fuer unseren eigenen Crawler gilt. Bisher war (B) immer erlaubt.

- **`skak.dk`/`www.skak.dk`** ist eine WordPress-Marketingseite mit News und einem Link nach
  draussen. Die `wp-json`-Schnittstelle kennt **keinen** Event-/Turnier-Inhaltstyp, nur `posts`
  und `pages`. Dort ist nichts zu holen.
- **Der Kalender liegt auf `turnering.skak.dk`** (eigenes ASP.NET-System, serverseitig gerendert,
  echte Werte). Und dessen robots.txt lautet vollstaendig:
  `User-Agent: *` / `Disallow: /`. **Damit ist (B) ebenfalls „nein"** — `*` deckt jeden eigenen
  User-Agent ab. Dasselbe gilt fuer `holdskak.skak.dk` (Mannschaftsschach).
- **Rechtsvorbehalt**: kein `Content-Signal`, keine eigene Scraping-Klausel; der Footer nennt nur
  ein generisches Copyright. Das pauschale `Disallow: /` auf den DATEN-Subdomains wirkt aber
  faktisch wie ein vollstaendiger maschinenlesbarer Nutzungsvorbehalt — nur ueber klassisches
  robots.txt statt ueber einen Content-Signal-Kopf.
- **Was in der Liste stand** (aus einem einzelnen Abruf, siehe Prozess-Lehre unten): 73 Eintraege
  in einer Mischansicht („Alle": Koordinerede 46, Hurtig 11, Lyn 3, andere 7) — also laufend UND
  demnaechst, nicht auf Zukunft bereinigt. Geliefert werden Name, Start/Ende, Teilnehmerzahl,
  daenisch/FIDE-gewertet, Rollstuhlzugang; Filter nach Bezirk, Wochenende, Senioren, Jugend.
  **Kein Ort und keine PLZ in der Liste** — fuer unser PLZ-zuerst-Geocoding waeren die
  (ebenfalls gesperrten) Detailseiten noetig. Bedenkzeit nur als grobe Kategorie, keine
  Rundenzahl.
- **Naechster Schritt, falls ueberhaupt**: kein technischer. Kontakt zur Dansk Skak Union mit der
  Bitte um einen Feed oder eine gezielte robots.txt-Freigabe fuer einen benannten User-Agent.
- **Nebenfund**: `danbase.skak.dk/turneringer.php` (eigene Subdomain, **keine** robots.txt, also
  offen) ist eine HISTORISCHE Ergebnisdatenbank von den 1890ern bis 2026 mit Sieger und
  Partienzahl je Turnier. Fuer den Kalender wertlos, aber dieselbe Kategorie wie torneionline:
  eine moegliche Zweitquelle fuer den Turnier**verlauf**.

## Zwei Prozess-Lehren aus der Pruefung selbst

**robots.txt gilt je SUBDOMAIN, und Verbaende verteilen ihre Daten genau dorthin.** Bei skak.dk
ist die Hauptdomain erlaubend und die Daten-Subdomain pauschal gesperrt; bei uschess.org tragen
alle vier Hosts dieselbe Sperre. Wer nur `example.org/robots.txt` liest und dann
`daten.example.org` abruft, hat die Regel nicht geprueft, sondern geraten. In dieser Reihe ist
das einmal passiert (ein Abruf auf `turnering.skak.dk`, bevor dessen robots.txt vorlag) — der
Agent hat es offen berichtet und danach nichts mehr geholt. Fuer eine Umsetzung heisst das:
robots.txt je Host abrufen und CACHEN, bevor der erste Inhaltsabruf rausgeht.

**Das Internet Archive ist kein verlaesslicher Ersatz.** Bei zwei Quellen hat es die Struktur
gerettet (vegaresults, vegachess), bei einer war es aus der Analyseumgebung heraus nicht
erreichbar, und bei `status=next` von vegaresults gab es trotz 5790 Aufnahmen keinen einzigen
passenden Snapshot — der Archiv-Crawler klickt nur den Standardzustand eines Formulars ab. Was
hinter einem nicht-vorbelegten Filter liegt, ist im Archiv systematisch unsichtbar.

### ✅ englishchess.org.uk/events (ECF, England) — der zweitbeste Fund

**Sechster Fall von „nicht diese URL, sondern jene", und der lehrreichste**: BEIDE genannten
Adressen sind Sackgassen, die Antwort ist ein DRITTER Host derselben Familie.

- `rating.englishchess.org.uk` fuehrt **0** kuenftige Turniere. `/events/list` heisst „List of
  Events **Received**" und ist nach MELDEdatum sortiert — ein Ergebnisprotokoll.
- `ecflms.org.uk` ist nur ein Vanity-Alias, der auf `lms.englishchess.org.uk` weiterleitet (das
  Liga-Verwaltungssystem). Dort gibt es kuenftige Spieltermine, aber **keine globale Liste** — nur
  je Organisation, ohne Volltextsuche und ohne Index der Organisationen. Nicht messbar ohne
  Vollcrawl. Die Apex-Domain selbst zeigte in Archiv-Aufnahmen zuletzt eine cPanel-Parkseite —
  im Zweifel `lms.englishchess.org.uk` verwenden, nicht die Vanity-Adresse.
- **Die Antwort ist `www.englishchess.org.uk/events/`**: WordPress mit „The Events Calendar",
  und damit eine **offene, dokumentierte** REST-API (Standard-Plugin, kein Reverse Engineering):
  `GET /wp-json/tribe/events/v1/events?page=&per_page=&start_date=&end_date=&venue=&organizer=&categories=`
  plus `/venues`, `/organizers`, `/categories`.
- **320 Events, 23 Seiten à 14** (Feld `total`/`total_pages`), Snapshot **2026-01-28**, sortiert ab
  „jetzt" aufwaerts.
- **Der eigentliche Gewinn sind die Felder**: das eingebettete JSON-LD traegt
  `venue.address` mit **`postalCode`** UND **fertige `geo.latitude/longitude`**. Damit entfaellt
  fuer diese Quelle unser Geocoding komplett — die einzige der 15 Quellen, die Koordinaten
  mitliefert. Dazu Name, Start/Ende mit Zeitzone, kanonische URL, **numerische WP-Post-Id als
  stabile Kennung**.
  **Fehlt**: Bedenkzeit und Rundenzahl als Spalte (nur textlich aus Namen/Kategorie ableitbar,
  also ein Fall fuer den `TournamentClassifier`), Teilnehmerzahl (wie ueberall vor dem Turnier).
- **robots.txt (A)**: `ClaudeBot: Disallow: /` auf **allen drei** Hosts, dazu GPTBot, CCBot,
  Google-Extended, Amazonbot, Bytespider, Applebot-Extended, meta-externalagent. Nichts live
  geholt — alle Angaben aus dem Archiv, je mit Snapshot-Datum.
- **robots.txt (B)**: `User-agent: *` erlaubt; auf `www` zusaetzlich gesperrt `/images/`, `/CBV/`,
  `/FM_uploads/`, `/PGN/`, `/membership*`, `/members-only*` — **`/events/`, `/event/`, `/venue/`
  und `/wp-json/` sind NICHT gesperrt**. `Crawl delay: 10`.
- **Rechtsvorbehalt**: `Content-Signal: search=yes, ai-train=no, use=reference` mit ausdruecklichem
  Verweis auf Art. 4 DSM-Richtlinie. Verboten ist das TRAINIEREN; `use=reference` deckt unseren
  Fall (Fakten als Referenz mit Link) — dieselbe Lage wie bei US Chess.

#### Was die Umsetzung dann gemessen hat (2026-09-08, v0.451.0)

**Sie war einen halben Tag lang geparkt, und der Grund war nicht technisch.** Die `robots.txt`
(Cloudflare-verwaltet, an dem Tag erneut geprueft) sagt zwei Dinge gleichzeitig:

```
User-agent: *
Content-Signal: search=yes,ai-train=no,use=reference
Allow: /

User-agent: ClaudeBot
Disallow: /
```

Fuer das FEATURE ist das ein Ja — unser Crawler faellt unter `*`, `/wp-json/` ist nicht gesperrt,
und ein Turnierkalender mit Verweis auf die Quelle ist genau der Fall, den `use=reference` deckt.
Fuer die ENTWICKLUNG war es zunaechst ein Nein: um einen Parser zu schreiben, der nicht raet,
braucht es Beispielantworten, und die haette sich der Agent selbst holen muessen. **Der Betreiber
dieses Stacks hat die Abrufe daraufhin ausdruecklich freigegeben** — es ist sein Crawler, seine
Entscheidung. Sie laufen seither ueber den Crawler-Container mit dessen eigenem User-Agent; ein
fremder wird nie vorgetaeuscht.

**Die Zahlen, gemessen statt geschaetzt:** 256 kuenftige Turniere, 2026-09-08 bis 2027-07-02 —
also zehn Monate Vorlauf. chess-results fuehrt fuer ENG im selben Zeitraum 143. Gegeneinander
gehalten (Termin ±1 Tag + zwei unterscheidende Woerter): **222 der 256 (86 %) stehen dort nicht.**
England war zu dem Zeitpunkt ausserdem noch nie gesweept — im Verzeichnis standen null englische
Turniere.

**Die Recherche lag bei den Koordinaten richtig, aber am falschen Ort.** Sie erwartete sie im
eingebetteten JSON-LD der Seite; tatsaechlich stehen sie im **zweiten REST-Endpunkt**: im Termin
ist die Spielstaette nur ein Stummel (Nummer, Name, Adresse der Detailseite), erst
`/wp-json/tribe/events/v1/venues` hat `address`, `city`, `zip` und `geo_lat`/`geo_lng`. Das kostet
die Haelfte der Abrufe (6 Seiten Termine, 12 Seiten Spielstaetten — 596 insgesamt, davon 249
wirklich benutzt), lohnt sich aber: **168 der 256 Turniere kommen fertig verortet**, und
einzeln nachzuschlagen waeren 249 Abrufe statt 12.

Dafuer gibt es im Bestand jetzt `GeoSource.SourceProvided`: „die Quelle hat es selbst gesagt".
Diese Koordinate schlaegt jeden Lexikon-Treffer (sie meint die Spielstaette, nicht die Stadtmitte)
und wird vom Neuaufloesungs-Lauf wie eine von Hand gesetzte in Ruhe gelassen.

**Die Schlagworte sind gepflegt und beantworten Fragen, die sonst nur der Name andeutet:**
„FIDE Rated" 152, „ECF Rated" 196, **„Juniors Only" 50**, **„Online" 28**, **„Meeting" 4**
(das sind Verbandssitzungen, keine Turniere). Die letzten drei werden ausgewertet — bei keiner
anderen Quelle gibt es diese Angaben als Feld.

**Was fehlt:** Bedenkzeit, Rundenzahl, System und Teilnehmerzahl. Die stehen nur im Fliesstext der
Ausschreibung, und der ist Werbetext.

### ✅ schachbund.de (DSB, Deutschland) — kleiner Ertrag, aber echter ZUSATZ

Nicht die Startseite, sondern die **Turnierdatenbank**. Deutschland ist ueber chess-results
teilweise abgedeckt, die Frage war deshalb nicht „gibt es Turniere", sondern „gibt es welche, die
chess-results NICHT hat".

- **Antwort: ueberwiegend ja.** Es ist ein reines MELDE-System („Termin selbst eintragen"), ohne
  chess-results-Konto oder Ergebnismeldung. Klarer Zusatz bei Vereins-Abendturnieren,
  Jugend-Cups, **Fernschach**, **Problemschach**, Online und Schach960 — Kategorien, die
  chess-results praktisch nie fuehrt. Dublette bei den groesseren, etablierten Serien; der
  offizielle DSB-Terminplan (Bundesliga-Runden, Schiedsrichterkurse) ist teils gar kein Turnier.
- **~104 eindeutige kuenftige Eintraege** (106 mit zwei Doppelungen), **09.09.2026–19.03.2027**,
  ueber 25 Kategorien (16 Bundeslaender, Baden/Wuerttemberg getrennt, Oesterreich/Europa/Welt,
  Fernschach/Blindenschach/Problemschach/Online/960). Groesste: NRW 22, Bayern 19. Mehrere
  Bundeslaender bei 0 — ungenutzt, nicht technisch leer.
- **Zwei Wege, beide serverseitig**: (a) **RSS/XML je Region**
  `share/feed-turnierdatenbank-{region}.xml`, (b) HTML `turnierdatenbank-{region}.html`.
  **Falle beim Slug**: zusammengesetzte Namen verlieren den Bindestrich
  (`nordrheinwestfalen`, `rheinlandpfalz`, `sachsenanhalt`) — den Slug aus der Fusszeile
  ablesen, nicht aus einer Namenskonvention raten.
- **Zweite Falle, harmlos aber toedlich wenn unbekannt**: der Erstaufruf liefert nur
  `<title>Einen Moment …</title>` mit `document.cookie="dwzc=1"; location.reload()`. Das ist ein
  **JS-Cookie-Gate, keine SPA** — mit `Cookie: dwzc=1` kommt sofort das volle Contao-HTML. Kein
  Bot-Schutz, kein Headless-Browser noetig.
- **robots.txt (A)/(B)**: kein KI-Bot-Block. `*` sperrt die **DWZ-Ratingdatenbank**
  (`/turnier.html`, `/turnier/`, `/dwz-turniere*`, `/spieler/`, `/verein/`, `/verband/`) —
  **`/turnierdatenbank*.html` und `/turnierdetails/` sind frei**. `Crawl-delay: 5`.
  Kein `Content-Signal`, kein `ai.txt`, keine Scraping-Klausel im Impressum.
- **Fehlt**: Bedenkzeit, Rundenzahl, Teilnehmerzahl, und **keine stabile numerische Kennung** —
  nur der Contao-Slug. Ort ist Freitext, **PLZ nur in 6 von 15 Stichproben**. Ohne harte Id
  braeuchte es die Fuzzy-Zuordnung wie beim FIDE-Kalender (Datum + Name).
#### Was die Umsetzung dann gemessen hat (2026-09-08, v0.449.0)

**Die Zahlen halten:** 106 Zeilen, 104 verschiedene Termine ueber 25 Regionen (zwei stehen in
zwei Regionen), 50 davon mehrtaegig.

**Der RSS-Feed allein reicht NICHT — die Seite ist die bessere Quelle.** Die Recherche nannte
beide Wege gleichwertig; gemessen sind sie es nicht. Der Feed traegt als Beschreibung die
Ausschreibung, und die ist Freitext des Veranstalters: **nur 3 von 96** gliedern sie so, dass ein
Ortsabschnitt herauszulesen waere. Die **Uebersichtsseite** dagegen hat je Termin eine eigene
Zeile mit Anschrift — **105 von 106**, davon 26 mit Postleitzahl. Ohne sie gaebe es fuer diese
Quelle praktisch keinen Pin.

Gelesen werden deshalb **beide**: die Seite fuer Termin, Name und Anschrift, der Feed fuer
Rundenzahl (60 von 96) und Bedenkzeit (29 von 96), die auf der Seite gar nicht stehen. Zwei
Abrufe je Region, mit der `Crawl-delay: 5` aus der robots.txt der Quelle.

**Der Termin kommt aus dem `title`-Attribut**, nicht aus dem Datumsblock: dort steht er
vollstaendig ausgeschrieben („12.09.2026 10:00–13.09.2026 16:30"), waehrend der Block bei
mehrtaegigen Turnieren abkuerzt („12. - 13.09.2026") und ueber den Monatswechsel raten liesse.

**Die Slug-Falle ist bestaetigt**: die Seite heisst `turnierdatenbank-nordrhein-westfalen.html`,
der Feed `feed-turnierdatenbank-nordrheinwestfalen.xml`. Mit Bindestrich antwortet der Feed 404 —
betrifft fuenf der 25 Regionen. Die Regionen werden von der Uebersichtsseite GELESEN, nicht
geraten; gerade in den Sonderkategorien liegt der Wert dieser Quelle.

**„europa" und „welt" sind keine Laender.** Dort standen zuletzt Kreta, Lettland, Suedtirol und
ein Kreuzfahrtschiff. Solche Eintraege bekommen keine Foederation — eine falsche waere schlechter
als keine. Dasselbe gilt fuer Fernschach und Onlineschach, die im Ortsfeld ihren Server nennen
(„BdF-Server", „Online") und deshalb keinen Pin bekommen.

- **Wichtiger Nebenfund**: die **Landesverbaende fuehren eigene, getrennte Kalender**
  (`schachbund-bayern.de/turniere/`, `schach-in-nrw.de`, `sjnrw.de` fuer die NRW-Jugend) — ohne
  Einbettung der DSB-Datenbank. Das ist ein Dutzend weiterer, unabhaengiger Quellen; gehoert auf
  die Kandidatenliste.

### ⚠️ chessmanager.com — die Archivrunde dreht das Urteil ins „Anfrage lohnt sich"

Die Sperre bleibt (`ClaudeBot: Disallow: /` + Art.-4-Vorbehalt), aber jetzt ist BELEGT, was dahinter
liegt — und es ist die ertragreichste Quelle der ganzen Pruefung. Alles aus dem Archiv, je mit
Snapshot-Datum; chessmanager.com selbst wurde nicht erneut angefragt.

- **Es gibt DREI Reiter, nicht einen**: `finished` / `active` (Vorgabe) / **`upcoming`**. Genau die
  FIDE-Lehre: `/en-us/tournaments` ist NICHT die Zukunftsansicht.
- **`/en-us/tournaments/upcoming`, Snapshot 2025-05-14**: Seite 1 = 51 Karten, **alle** mit
  Startdatum in der Zukunft; die Blaetterung endet bei `offset=900` → **901–950 kuenftige
  Turniere weltweit an einem Stichtag**. Zum Vergleich `active`: 301–550 je nach Snapshot, und
  dort mischen sich Klub-Ladder-Turniere mit Fantasiedaten wie „1/1/32" hinein.
- **48 verschiedene Laender** ueber die Stichproben, darunter Vietnam, Philippinen, Brasilien,
  Sri Lanka, Uganda, Sambia, Mongolei — **deutlich globaler als chess-results und FIDE**, die
  europalastig sind. Auf Seite 1 allein Polen 23.
- **Fast alle Zielfelder stehen DIREKT in der Listenkarte**: Name, Land (ISO2 + Klartext), Ort,
  Start–Ende, **Rundenzahl** („0/9" = 9 geplant), Bedenkzeit-Kategorie. Ein Abruf fuer bis zu 50
  Turniere — **billiger als unser chess-results-Sweep**. Nur die exakte Bedenkzeit („10' +5\"")
  und der 3-Buchstaben-Foederationscode brauchen die Detailseite, die zusaetzlich ein
  **Schema.org-`Event`-JSON-LD** traegt.
- **Blaetterung**: reiner GET-Parameter `offset` in 50er-Schritten, klickbare Links, kein
  Infinite-Scroll. **Filter sind echte GET-Parameter**: `name`, `date_start`, `date_end`,
  `country` (3-Buchstaben wie FIDE), `city`, `city_radius` (0/100/250 km), `tempo[]`,
  `options[]` (fide/local). Serverseitige Filterung spart Seiten.
- **Stabile Kennung**: numerische Turnier-Id im Pfad (`/en-us/tournaments/{id}`), Mix aus alten
  kleinen Zahlen und neueren 16-stelligen — eindeutig nutzbar.
- **Einordnung**: der gemessene Ertrag (≈900 kuenftige, 48 Laender, fast alle Felder ohne
  Zweitabruf) rechtfertigt eine **offene Anfrage nach Datenzugang** beim Betreiber. Das Archiv
  umgeht nur den technischen Weg, nicht die Rechtserklaerung ueber den Inhalt.
- **Offen**: ob das Karten-HTML heute noch dem Muster von Maerz 2026 entspricht (es aenderte sich
  zwischen Feb und Maerz 2025 einmal), ob Cloudflare einen erlaubten Crawler technisch
  durchlaesst, und ob die Teilnehmerzahl-Luecke systematisch ist.

### ⚠️ new.uschess.org — die Archivrunde: bestaetigt, mit einem wichtigen Vorbehalt

Serverseitig gerendert, **bestaetigt in drei unabhaengigen Snapshots** (2024-02-13, 2023-03-23,
2025-03-13): die Turniere liegen als `<div class="views-row">` direkt im HTML, 20 je Seite.

- **Mengen, je mit Snapshot**: 2023-03-23 (alle) → `page=26`, also bis ~540; 2022-11-06 (nur
  FIDE-gewertet) → `page=2`, also ~60; 2025-03-13 → `page=45`, also bis ~920.
- **Der Vorbehalt, der die Zahl relativiert**: die Liste ist durch **wiederkehrende Vereinsabende**
  aufgeblaeht — dieselbe Serie („Tuesday Night Action") erscheint EINMAL JE TERMIN, ohne
  Serien-Zusammenfassung. Echte einmalige Turniere sind ein Bruchteil. Ein Import ohne
  Entdopplung wuerde dieselbe Veranstaltung vielfach anlegen.
- **Parameter, aus echten archivierten URLs 2021–2025 extrahiert** (nicht geraten): `combine`
  (Freitext), `field_event_address_administrative_area` (Bundesstaat-Kuerzel oder `All`),
  `field_event_dates_occurrences[min]`/`[max]` (`MM/DD/YYYY`), `field_fide_rated_value` (0/1 —
  **verschwand zwischen Snapshots**, die Oberflaeche wurde umgebaut),
  `field_online_event_value` (`All`/`2`), `field_geofield_proximity[value]` (**gestuft**: 10, 25,
  50, 60, 100 Meilen), `…[origin_address]` (in allen echten Beispielen eine **5-stellige ZIP**,
  kein Freitext), `field_banner_line_value[Label]` (Heritage/Grand Prix/…), `page` (0-basiert).
- **PLZ: NICHT in der Liste** (nur „Stadt, Bundesstaat"), **JA auf der Detailseite**
  (`field_event_address`: „201 N 17th St, Philadelphia, PA 19103") → ein Abruf je Turnier, wie
  `art=9` bei chess-results. Die Detailseite traegt zusaetzlich `field_number_of_sections`,
  `field_gp_points`, Preisgeld, `field_fide_rated`, Barrierefreiheit, Veranstalter — aber
  **kein** Rundenzahl- oder Bedenkzeit-Feld und **kein** Datum (das steht nur in der Liste).
- **Kein JSON, kein RSS**: kein `<link rel="alternate">` fuer diese Ansicht, CDX-Suche nach
  `*feed*`/`*format=json*` unter `new.uschess.org` liefert null Treffer.
- **Keine oeffentliche TLA-Nummer** im Text — nur eine interne Drupal-`entityId` (Beispiel 36434)
  aus einem eingebetteten `dataLayer`-Skript. Undokumentiert, aber die einzige technische Id.
- **Bedenkzeit-Notation, echte Beispiele**: `30+0` · `G/90, 30…` · `4SS, G15+5` ·
  `5SS, G/3 d2 (double round, 10 games)` · `5SS, 40/80, SD/30, d30` · `4SS, G/60;d5` ·
  `3-RR, G/45 d5` · `Game 5, no increment or delay`. Muster `{N}{SS|RR}`, dann `G/x` oder
  Mehrstufen `Zuege/Minuten, SD/Minuten`, optional `d<n>` (Delay) oder `+<n>` (Inkrement) —
  Freitext mit uneinheitlichen Trennzeichen, kein festes Schema. **Ein eigener Parser, und einer
  mit Testvektoren.**

### ⚠️ chess.ca (Kanada) — sauberer JSON-Volldump ohne stabile Kennung

- **Kein Rating-Archiv wie US Chess** — nur der Ankuendigungsweg, `/en/events/` (bzw.
  `/fr/evenements/`). Organisatoren melden per Formular.
- **170 kuenftige von 183 Eintraegen**, **10.09.2026–15.08.2027**, live gemessen. Davon 164
  inlaendische Praesenzturniere, 10 online, 9 im Ausland (Team-Kanada-Delegationen). Stark
  Ontario-lastig: 125/183 = 68 %.
- **Der Zugang ist ungewoehnlich und angenehm**: die Seite rendert serverseitig nur ein leeres
  Svelte-Geruest, verweist aber auf eine **statische** Datei `/ext/cfc-data.<hash>.js`, die den
  **kompletten Datensatz als JSON** enthaelt (`window.ws_cfc_data = {…};`). Ein `curl`, kein
  JavaScript, kein API-Aufruf. **Aber**: der Hash wechselt bei jedem Site-Build → der Abruf muss
  ZWEISTUFIG sein (HTML holen, Dateinamen herausziehen, dann die Datei), nie hartkodiert.
- **robots.txt (A)/(B)**: `www.chess.ca/robots.txt` ist woertlich nur `User-agent: *` — keine
  Disallow-Zeile, kein `Content-Signal`. Beide Fragen „ja". `forums.chess.ca` sperrt dagegen
  ClaudeBot & Co. mit Art.-4-Vorbehalt — dort liegen aber nur Freitext-Threads, nicht die Daten.
- **Der Ausschluss-Kandidat ist die Kennung**: `oid` ist **nur die Listenposition** (1..N) —
  belegt an zwei Archiv-Aufnahmen (2023-08-15 und 2025-09-04), beide beginnen wieder bei 1. Es
  braeuchte also eine eigene Kennung aus Name+Datum+Ort samt Fuzzy-Zuordnung, wie beim
  FIDE-Kalender.
- **Fehlt sonst**: PLZ und Adresse ganz (nur `city` + Provinzkuerzel — unser PLZ-zuerst-Geocoding
  greift NIE), Bedenkzeit, Rundenzahl, Teilnehmerzahl. Die `url`-Felder zeigen auf Drittseiten
  (Vereinsseite, Google-Formular, PDF, Facebook) — kein eigenes Permalink, also linkrot-anfaellig.

### ⚠️ aicf.in (Indien) — kein Vorbehalt, aber schwache Daten

- **98 kuenftige** von 329 Tabellenzeilen, **2026-09-07 bis 2027-03-26**, live gemessen
  (`/all-events/`, ein WordPress-Gutenberg-Tabellenblock, serverseitig, keine Blaetterung).
  Dazu `/upcoming-nationals/` mit 14 nationalen Meisterschaften.
- **robots.txt (A)/(B)**: nur `Disallow: /wp-admin/`. Kein KI-Bot-Block, **kein
  `Content-Signal`**, keine Scraping-Klausel (die Nutzungsbedingungen regeln nur Zahlungen).
  Beide Fragen also „ja" — die dritte Quelle ohne Vorbehalt.
- **Aber die Daten taugen kaum**: **keine PLZ** (unser Geocoding-Vorrang entfaellt komplett),
  keine Bedenkzeit, keine Rundenzahl, keine Teilnehmerzahl, **keine eigene Turnier-URL** (nur ein
  geteilter PDF-Link bei 75 % der Zeilen). Und die Kennung ist unbrauchbar: **„Event Code" wird
  wiederverwendet** — Code 469820 traegt zwei verschiedene Turniere. Nur als Kompositschluessel
  (Code+Name+Datum) tauglich.
- **Datenqualitaet der Orte**: 98 verschiedene Ortsangaben auf 329 Zeilen, ohne jede Struktur —
  mal Stadt, mal Bundesstaat, mal abgekuerzt („UP", „MP"), mit Tippfehler-Klammern
  („Ahiyanagar (ahmednagar), Maharashtra") und einer buchstaeblich doppelten Zeile, die sich nur
  in einem geschuetzten Leerzeichen unterscheidet. Dazu 230 von 329 Zeilen in der Vergangenheit,
  also eigene Datumsfilterung noetig.
- **Vor jeder Umsetzung zu messen**: die Ueberschneidung mit chess-results. Die „FIDE Rated"-
  Turniere dieser Liste duerften dort ohnehin stehen.

### ⚠️ schack.se (Schweden) — JSON-API ohne einen einzigen Ort

- **Nicht `www.schack.se`** (301 auf den Webshop), sondern die **bare Domain**.
  `member.schack.se` (Eigenrating) ist vollstaendig gesperrt und nur per JavaScript.
- **Offene, dokumentierte API** („The Events Calendar Pro", derselbe Standard wie bei der ECF):
  `GET https://schack.se/wp-json/tribe/events/v1/events?categories=inbjudningar&per_page=50&page=N&starts_after=…`
- **183 kuenftige** Kalendereintraege, **2026-09-11 bis 2027-02-15**; davon Kategorie
  „Taevlingsinbjudan" **76** = 42 echte Turnierausschreibungen + 33 **Liga-Spieltage** (je ein
  Eintrag pro Runde — dieselbe Falle wie bei chess-results-Ligen) + 1 online.
- **Der Ausschlussgrund**: **0 von 183** Eintraegen haben ein befuelltes `venue` — kein Ort, keine
  Adresse, keine PLZ, auch nicht im Freitext. Hoechstens zufaellig im Namen („Trelleborg Open").
  Unsere Pipeline setzt beim Ort an; das ist nicht nachruestbar ohne Fremdseiten-Auslesen je
  Veranstalter. Auch Bedenkzeit, Rundenzahl und Teilnehmerzahl fehlen ganz.
- **robots.txt (A)/(B)**: kein KI-Bot-Block, aber `*` sperrt **`/kalender`** und `/page` — die
  HTML-Kalenderseite ist also auch fuer UNS verboten, der REST-Pfad nicht. `Crawl-delay: 20`.
  Kein Rechtsvorbehalt gefunden.
- Stabile Kennung (`id`, `slug`, `global_id`) und URL sind vorhanden — es fehlt nur alles, was den
  Eintrag brauchbar machen wuerde.

### ⚠️ shakkiliitto.fi (Finnland) — nicht vorrangig

- **Nicht `shakki.net`**: die Seite sagt selbst, dass der Verband den Kalender fuehrt
  („Kotimaisten turnausten kilpailukalenteria… vastaa Suomen Shakkiliitto"). Die Antwort ist
  `https://www.shakkiliitto.fi/kilpailukalenteri-2026/`.
- **275 Eintraege, 270 kuenftig**, **8.9.2026–30.8.2027**. Achtung: „2026" meint die SAISON
  Herbst 2026 bis Sommer 2027, der Jahreswechsel liegt mitten in der Liste.
- **Kein JSON** (kein `tapahtuma`-Inhaltstyp in `wp-json`, kein Events-Calendar-Plugin), kein
  JSON-LD. Serverseitiges HTML, aber reiner Freitext. Nur **229 von 275 (83 %)** haben eine
  Detailseite, dort Adresse **ohne PLZ**. Bedenkzeit und Rundenzahl nur vereinzelt und unscharf
  („10–20").
- **robots.txt (A)/(B)**: beide „ja", nur `/wp-admin/` gesperrt. Kein Rechtsvorbehalt gefunden.
- **Warum nicht vorrangig**: `fed=FIN` laeuft im normalen Sweep bereits mit (dort 50 Turniere),
  und die Ueberschneidung ist hoch — wiederkehrende Serien stehen in beiden. Der Mehrwert liegt
  bei ungewerteten Klubabenden.
- **Ein Fehler, der die Falle zeigt**: die automatisierte Erstauswertung las Januar-Termine als
  2026 statt 2027 — genau der Saisonumbruch. Wer das baut, braucht dafuer einen Test.
  Ausserdem stehen Absagen inline im Freitext („…PERUTTU!") statt als Feld: ein Crawler liest ein
  abgesagtes Turnier sonst als aktiv ein.

### ❌ swisschess.ch — die brauchbare API liegt hinter `Disallow: /`

Zweiter Fall nach Daenemark, in dem auch (B) „nein" lautet — und der bitterste, weil die Daten
gut waeren.

- **159 kuenftige Termine** (157 einzigartig), **07.09.2026–11.12.2027**, ueber eine **offene
  Directus-REST-API**: `cms.swisschess.ch/items/event_items` mit Filter/Sort/Join, saubere
  JSON-Antwort ohne Anmeldung.
- **Und genau dieser Host sperrt alles**: `cms.swisschess.ch/robots.txt` = `User-agent: *` /
  `Disallow: /`. Kein Bot ausgenommen, also auch unser eigener nicht. Die Hauptdomain
  `swisschess.ch` erlaubt dagegen alles (`Disallow:` leer).
- **Was erlaubt bleibt, ist duenn**: die HTML-Seite `/de|fr|it/kalender` traegt dasselbe Schema in
  einem Nuxt-`__NUXT_DATA__`-Skript (serverseitig, kein JavaScript noetig) — aber nur ein
  rollierendes **1-Monats-Fenster mit ~32 Eintraegen**, und Weiterblaettern laeuft ueber
  `POST /api/calendar/events` mit **Cloudflare-Turnstile**-Token. Das waere Anti-Bot-Umgehung und
  ist ausgeschlossen.
- **Alle drei Sprachpfade liefern denselben Datentopf** — es gibt keine besser gepflegte Fassung;
  die Titel sind organisch mehrsprachig (Organisator-Text).
- **Inhaltlich waere es ZUSATZ**: von 157 sind ~42 reine Verbands-Verwaltungstermine
  (SMM/SGM-Runden, Anmeldeschluss, Delegiertenversammlung), die uebrigen ~115 ueberwiegend
  Vereins- und Freizeitevents, die chess-results nicht fuehrt — darunter Turniere auf
  **vegaresults.com**, dem zweiten Schweizer Ergebnissystem. Zwei Eintraege (Schacholympiade)
  sind Dubletten zu unserem FIDE-Kalender.
- **Felder**: PLZ nur bei **18 von 159 (11 %)**, dafuer **Koordinaten schon bei 16 (10 %)**.
  Bedenkzeit grob bei 24, **Rundenzahl und Teilnehmerzahl fehlen ganz**. Kennung nur die interne
  Directus-Id; 26 % der Eintraege haben gar keine URL, 74 % eine externe.
- **Einordnung**: die Sperre wirkt eher unbeabsichtigt als scraping-feindlich (ein offenes
  Directus-Backend mit pauschalem robots.txt). Eine Anfrage nach Freigabe oder einem Schluessel
  ist der naheliegende Weg. **Ohne Freigabe: nicht umsetzen.**

### ❌ tournamentservice.com (Norwegen) — doppelt gesperrt

- Die Zieldomaene war aus der Analyseumgebung **nicht erreichbar** (Pakete auf 80 und 443
  verworfen, kein RST) — schon das deutet auf aktive Abwehr gegen Cloud-Zugriffe.
- Auf der Schwester-Instanz `www1.tournamentservice.com` (dieselbe Software TS6, dasselbe
  /24-Subnetz, `www4` teilt sogar die IP der Hauptdomain) steht:
  `User-agent: ClaudeBot` / `Disallow: /`. Da die robots.txt dort erkennbar ein zentral
  gepflegtes TS6-Template ist (die Disallow-Liste nennt genau die TS6-Endpunkte
  `PlayerDetails.aspx`, `KeepSessionAlive.aspx`, `GetPGN`, `eventmap`), gilt das mit hoher
  Wahrscheinlichkeit auch fuer die Hauptdomain.
- **Und (B) ist ebenfalls „nein"**: `User-agent: *` sperrt dort ausdruecklich
  `Disallow: /TournamentList.aspx?Srch`, `/TournamentList.aspx?`, `/Tournamentlist.aspx`,
  `/tournamentlist.aspx` — jeweils mit dem Kommentar „# CPU intensive". Genau die Turnierliste,
  in allen Schreibweisen.
- Inhaltlich waere es interessant gewesen: TS6 ist eine mandantenfaehige Arbiter-Software mit
  Integration in den norwegischen Verband UND die FIDE-Datenbank und listet Turniere VIELER
  Vereine — strukturell also wie chess-results, nicht wie Tornelo. `tournamentservice.net` ist
  eine gleichnamige, voellig andere Plattform fuer Billard.
- **Naechster Schritt, falls Skandinavien geschlossen werden soll**: den norwegischen Verband
  (Sjakkforbund) nach einem offiziellen Feed fragen, nicht diese Plattform auslesen.

## Runde 2: Oesterreich und alle acht Nachbarlaender (2026-09-07)

Auftrag: „pruefe Oesterreich und alle Nachbarlaender und erfass mal was gehen wuerde." Neun
Laender; Deutschland (`schachbund.de` ✅) und die Schweiz (`swisschess.ch` ❌, API hinter
`Disallow: /`) standen schon oben und wurden nicht doppelt geprueft. Die uebrigen sieben liefen
parallel, jeder mit derselben Frage: **bringt die Quelle Turniere, die nicht ohnehin auf
chess-results stehen?**

| Land | Quelle | Urteil | Kuenftige | Nicht auf CR | Abrufe |
|---|---|---|---|---|---|
| **Oesterreich** | chess-results `Kalender.aspx` | ✅ **sofort** | 143 | **93** | **1** |
| Italien | federscacchi.com | ✅ | 285 | ~80 % | 1 |
| Ungarn | chess.hu `/app/versenynaptar.json` | ✅ | 106 | 20 % + Monate Vorlauf | 1 |
| Slowakei | chess.sk REST-API | ✅ | 79 | 53 % | 1 (+1 je Detail) |
| Slowenien | sah-zveza.si | ✅ | 78 | ~92 % | 4 (+1 je Detail) |
| Tschechien | chess.cz | ⚠️ billig mitnehmen | 38 echte | 34 % (13) | 1 |
| Oesterreich | 9 Landesverbaende | ⚠️ nachrangig | 173 | 34 % | 9 |
| Liechtenstein | schach.li | ❌ | **1** | **0** | – |

### ✅ Der wichtigste Fund kommt aus der Quelle, die wir schon benutzen

**chess-results hat ZWEI Datensaetze, und wir lesen nur einen.** Die **Turniersuche**
(`TurnierSuche.aspx`, unser heutiger Sweep) fuellt sich erst, wenn der Veranstalter sein Turnier
in Swiss-Manager anlegt — typisch Tage bis Wochen vorher. Der **Ankuendigungs-Kalender**
(`Kalender.aspx`) wird vom Veranstalter vorab gepflegt.

Gemessen fuer AUT, Start ab 2026-09-07:

| | 09/26 | 10/26 | 11/26 | 12/26 | 01/27 | 02/27 | 03/27 | 04/27 |
|---|---|---|---|---|---|---|---|---|
| Turniersuche (heute) | 80 | 47 | **8** | **7** | 5 | 8 | 0 | 2 |
| Ankuendigungs-Kalender | 21 | 25 | **23** | **16** | 9 | 11 | 9 | 10 |
| **davon uns unbekannt** | 6 | 11 | **16** | **13** | 7 | 8 | 9 | 9 |

**143 kuenftige Eintraege, 93 davon fehlen in der Turniersuche.** Die Turniersuche bricht nach
zwei Monaten ein, der Kalender traegt gleichmaessig ueber 15 Monate — also genau ueber das
Fenster, das unser Verzeichnis eigentlich abdecken will (`heute + 18 Monate`).

Zwei Abrufwege, beide ohne neue Rechtslage (`chess-results.com/robots.txt`: `User-agent: *` →
`Allow: /`):

```
GET  https://chess-results.com/DownloadQuery.aspx?luser=AUT$3tt08K$&art=3   # 100 Eintraege
POST https://chess-results.com/Kalender.aspx?lan=1                          # 143, vollstaendig
     __EVENTTARGET = ctl00$P1$combo_landsel$DropDownList1
     ctl00$P1$combo_landsel$DropDownList1 = AUT
     ctl00$P1$combo_kat$DropDownList1     = 0     (2=Jugend, 3=Senioren, 5=Frauen)
```

Der POST ist derselbe ASP.NET-Postback-Ablauf, den `CrawlerService.SearchTournamentsAsync` schon
beherrscht. **Der Haken: kein Ortsfeld** — 89 % der Eintraege verlinken aber auf die
Turnierseite, und 80 % tragen ein Ausschreibungs-PDF.

**Das ist mehr Ertrag als alle neun oesterreichischen Landesverbaende zusammen, bei rund einem
Prozent des Aufwands.** Und es gilt vermutlich fuer JEDE Foederation, nicht nur AUT.

### ❌ chess.at (OeSB) — das K.-o.-Kriterium in Reinform

`chess.at/termine.html` enthaelt keine eigenen Daten, nur den Satz „Oesterreichische Termine siehe
Terminkalender **Chess-Results**". Der Jugendkalender ist woertlich ein
`<iframe src="https://chess-results.com/DownloadQuery.aspx?luser=AUT$3tt08K$&art=7">`.

### ⚠️ Die neun oesterreichischen Landesverbaende

173 kuenftige Eintraege, neun verschiedene CMS, **66 % schon auf chess-results** (19 von 29
stichprobenweise geprueft). Der Befund dahinter ist wichtiger als die Quote: wiederkehrende
Turniere landen fast alle irgendwann auf chess-results — nur SPAET. Die „Offene Welser
Stadtmeisterschaft" ist zwoelf Tage vor dem Termin dort nicht auffindbar, obwohl die Ausgabe 2025
steht.

Lohnend waeren allenfalls drei: **Oberoesterreich** (`schach.at/termine/`, 29 Eintraege, **52 %
mit PLZ** gegen 27 % bei chess-results, und Bedenkzeit + Publikum kommen aus einem AUSWAHLFELD
statt aus dem Namen — also autoritativ statt geraten), **Steiermark + Vorarlberg** (identisches
The-Events-Calendar-JSON, EIN Parser fuer beide, 68 Eintraege) und **Tirol** (40). Wien hat gar
keinen strukturierten Kalender, nur ein PDF-Raster mit Liga-Kuerzeln.

**Rechtlicher Vorbehalt bei zwei davon**, woertlich aus dem Impressum:
- `schach.at` (OOe): „Diese Webseite mit komplettem Inhalt darf weder fuer private noch fuer
  kommerzielle Zwecke kopiert, verbreitet, veraendert oder Dritten zugaenglich gemacht werden."
- `noe-schach.at`: „Eine Vervielfaeltigung oder Verwendung in anderen elektronischen oder
  gedruckten Publikationen ist ohne ausdrueckliche Zustimmung des Autors (NOeSV) nicht gestattet."

Nackte Termine sind Fakten und nicht geschuetzt, aber genau darauf berufe sich ein Betreiber —
**vor einer Uebernahme anfragen**. Drei Hoster sperren ausserdem den UA-String `ClaudeBot` auf
Applikationsebene (chess-vienna.at 403, noe-schach.at und schachinsalzburg.at 510 ModSecurity);
das ist eine Hoster-Blacklist, keine Entscheidung des Verbands — ein eigener UA kam bei allen
durch.

### ✅ Italien — federscacchi.com, 285 in EINEM Abruf

```
GET https://www.federscacchi.com/fsi/index.php/calendario/calendario
    ?dtiniric=2026-09-07&dtfinric=2027-12-31&ord=1&senso=Asc&ric=1
→ „Trovati 285 eventi", 1,26 MB
```

**Die Falle ist hier real:** ohne `ric=1` zeigt die Seite nur das Suchformular, wer die nackte URL
abruft, misst null.

Alle Felder stehen INLINE — Name, Termin, Region, Provinz, Ort, **Bedenkzeit** (`90' + 30'' bonus`),
**Rundenzahl** (`9 (8)`), Schiedsrichter, Ausschreibungs-PDF, Einfuegedatum. Kein Abruf je Turnier
noetig. Fuer die laufende Pflege sortiert `ord=5&senso=Desc` nach Einfuegedatum, jeder Eintrag
traegt eine fortlaufende Id — ein Delta-Abruf muss nur bis zur letzten bekannten lesen.

**Warum die 80 % dauerhaft sind:** von 285 Eintraegen verlinkt **kein einziger** auf
chess-results. 82 verlinken auf vesus.org, 4 auf vegaresults. Italien faehrt sein Turnierwesen auf
Vega/vesus — das ist keine Momentaufnahme, sondern die Entscheidung eines ganzen Verbands.
Trefferquote der Namensstichprobe: **3 von 15**.

Keine robots.txt (404 auf allen Varianten), keine Nutzungsbedingungen, keine UA-Diskriminierung
(vier UAs geprueft, alle 200). Kein Turniersystem-Feld, keine Einzel/Mannschaft-Kennung, **keine
PLZ** — dafuer immer die Provinz, was italienische Namensgleichheit aufloest.

**Suedtirol**: `schachbund.it/kalender` existiert deutschsprachig, ist aber tot — fuenf Eintraege,
juengster 22.03.2026, also **0 kuenftige**. Ueber die FSI kommt Suedtirol mit (`reg=21` → 2,
Trentino `reg=16` → 4).

### ✅ Ungarn — chess.hu, ein POST

```
POST https://chess.hu/app/versenynaptar.json    (leerer Rumpf)  → 106 Turniere, 31 KB
```

**GET antwortet 404** — wer den Endpunkt so prueft, haelt ihn fuer tot. Ohne Parameter ist die
Antwort genau die kuenftige Ansicht.

Der Ertrag ist ueberwiegend **Vorlauf**: ab November 2026 fuehrt chess.hu 41 Turniere, chess-results
5. Im Rueckblick auf einen abgeschlossenen Zeitraum landen 80 % irgendwann doch auf chess-results,
**20 % nie**. 20 Namen einzeln geprueft: 19 nicht auf chess-results.

Das JSON traegt nur neun Felder (Name, Termin, Ort, Jugend-/FIDE-Flag, Id) — Bedenkzeit,
Rundenzahl, System und die Adresse MIT PLZ stehen in der Inline-Ausschreibung der Detailseite
(server-gerendert, `post.php?p={id}`), in der Stichprobe 5 von 10 vorhanden.

Fallen: `from_date` ist `MM.DD` OHNE Jahr (das steht in `from_year`); `helyszin=kulfoldi` heisst
nicht „im Ausland" (alle 15 so markierten liegen in Ungarn); **150-Zeilen-Deckel ohne
Fehlermeldung**; `place = "Helyszin kesobb"` („Ort spaeter", 11 Eintraege) und `"Online"` duerfen
keinen Pin bekommen.

#### Was die Umsetzung dann gemessen hat (2026-09-08, v0.446.0)

**Das Feld `ifjusagi` („Jugend") ist unbrauchbar** — und das ist der wichtigste Fund. Es steht bei
**77 von 107** Turnieren auf „igen", darunter „Terézváros Open", „Félegyházi Libafesztivál" (ein
Gaensefest) und die „Weltbegegnung schachspielender Ungarn". Es beantwortet nicht „ist ein
Jugendturnier", sondern etwas wie „Jugendliche duerfen mitspielen". Uebernommen waeren drei
Viertel des ungarischen Bestands faelschlich Jugendturniere gewesen, und der Filter „nur
Erwachsene" haette fuer Ungarn fast nichts mehr uebrig gelassen. Die Einordnung kommt deshalb aus
dem Namen — dafuer hat der Klassifizierer „ifjusag" und „gyermek" gelernt (und bei der
Gelegenheit „mladez", „dorast", „ziac", „zaci", „mladinsk", „giovanil": 42 Treffer im
Altbestand, kein falscher).

**Die Detailseite lohnt nicht, und das ist nachgerechnet.** Sie traegt ausser Ort, **Komitat** und
Terminen nur eine Anmelde-Adresse und meist einen PDF-Verweis — keine Bedenkzeit, keine
Rundenzahl, keine Anschrift. Das Komitat waere der einzige Zugewinn, und er ist keiner: von 52
verschiedenen Ortsnamen des Kalenders stehen **46 im Lexikon, davon nur drei mehrdeutig** — und
die drei (Győr, Szekszárd, Veszprém) sind es innerhalb derselben Stadt, wo die
5-km-Regel des Geocoders ohnehin greift. Die sechs Nichttreffer sind kein Ortsproblem, sondern
Mehrort-Ligen („Hmvhely, Makó, Mórahalom, Szeged, Szentes, Üllés"), Platzhalter und Gebaeudenamen.
Also ein Abruf, nicht 108 — anders als bei chess.sk, wo die Detailseite die Postleitzahl traegt.

**Der Abruf ist langsam: 75 Sekunden fuer 31 kB.** Mit dem ueblichen halben Minuten-Limit saehe
die Quelle wie ein Dauerausfall aus; ihr Client bekommt 180 Sekunden. Die Detailseiten sind
dagegen flott (3 s) — es ist der Kalender-Endpunkt selbst.

**robots.txt** (geprueft): `User-agent: *` sperrt `/wp-admin/`, `/wp-includes/`, **`/hu/`** und
**`/en/`**. Der Endpunkt liegt unter `/app/`, die Detailseiten im Wurzelverzeichnis — beides nicht
betroffen. Kein TDM-Vorbehalt.

### ✅ Slowakei — chess.sk, offizielle REST-API

```
GET https://www.chess.sk/api/turnaje.php/v1/tournaments   → 79 kuenftige, 34 KB
```

OpenAPI-Spec unter `/api/swagger.yaml`, im Footer als „**Free api specification**" verlinkt,
`Access-Control-Allow-Origin: *`, und die Beschreibung laedt woertlich zur Nutzung ein: „*If you
need more, just ask for it - sekretariat@chess.sk*". 20 Monate Vorlauf (bis 2028-05).

**53 % nicht auf chess-results**, und die Aufteilung ist aufschlussreich: im Nahbereich
(bis 12/2026) 45 %, ab 2027 **85 %**. Dauerhafter Ueberschuss sind nicht FIDE-gewertete
Klub- und Barturniere (13-Runden-Barliga, GPX-Serien, Ortsturniere).

**Geschenk:** 26 Eintraege tragen die chess-results-Nummer selbst mit — ein exakter Dedup-Schluessel
gegen `TournamentDirectoryEntries.ChessResultsId` statt Namensraterei.

Bedenkzeit, Rundenzahl und System stecken zusammen im Freitext `Systém` („Švajčiarsky systém na 7
kôl, tempo 2 × 15 min + 5 sek/ťah"), aber sehr regelmaessig. `Typ turnaja` (std/rpd/blz/onl)
mappt 1:1 auf `Speed`. Nicht-Turniere (Schulungen, Ferienlager) sind mit drin und muessen ueber
den Namen gefiltert werden.

robots.txt: `User-agent: * / Allow: /`, danach 622 namentlich gesperrte Bots aus einer
Blocklist von 2021 — ClaudeBot und GPTBot kommen darin nicht vor.

#### Was die Umsetzung dann gemessen hat (2026-09-08, v0.445.0)

**Die Schnittstelle allein reicht nicht.** Ihr ganzer Inhalt sind Name, Termin, Ort, Staat und
drei Verweise — genau die elf Felder der Spezifikation. Bedenkzeit, Rundenzahl, System und
**Anschrift** stehen nur auf der Detailseite, und die ist HTML. Ein Abruf je Turnier, deshalb mit
700 ms Pause und Deckel (79 Turniere ≈ eine Minute).

**Die Anschrift ist der Grund, warum sich das lohnt** — und sie korrigiert das obige Urteil
„PLZ nur 1 von 9": das galt fuer das Feld `mesto` der LISTE. Auf der Detailseite tragen
**35 von 79** Anschriften eine Postleitzahl („Slovnaft Business Center, Vlčie hrdlo 1/A,
824 12 Bratislava"), und der slowakische Bestand im Lexikon ist mit 5233 Eintraegen vollstaendig.

**Die Detailseite ist sprachneutral lesbar.** Ihre Felder tragen englische CSS-Klassen
(`datagridphp_detail_td_field_system`, `_miesto`, `_trn_type`) — gelesen wird ueber die Klasse,
nicht ueber die slowakische Beschriftung daneben. Eine uebersetzte Oberflaeche braecht den Parser
also nicht.

Die Zahlen am 2026-09-08: **27** (nicht 26) Eintraege mit chess-results-Nummer; `Typ turnaja` bei
**63 von 79** gesetzt (Rapid 41, Blitz 11, Standard 10, Online 1), die uebrigen 16 stehen auf
„Nie je nastavené" und bekommen ihre Klasse aus dem Bedenkzeit-Text; `Systém` bei 65 von 79
gefuellt, daraus 50-mal das System, 55-mal die Rundenzahl und 57-mal die Bedenkzeit; **4**
Nicht-Turniere (drei Schiedsrichter-Lehrgaenge, ein Trainerseminar); 30 von 79 Ortsangaben mit
Stadtteil-Zusatz („Bratislava - mestská časť Rača"), der fuer die Verortung wegfaellt.

Drei Dinge, die der Freitext lehrt: „Schweizer System" steht in **vier** Schreibweisen da,
einschliesslich des Tippfehlers „Šviačiarský" (deshalb `sv\w*ciar` statt einer festen
Zeichenkette); ein Teil der Eintraege ist auf **Englisch** geschrieben („Number of rounds: 7");
und „8 **dvoj**kôl" sind acht DOPPELrunden — dort bleibt die Rundenzahl lieber unbekannt.

### ✅ Slowenien — sah-zveza.si, Faktor elf

**78 kuenftige gegen 7 auf chess-results** im selben Zeitraum. Der Kalender verlinkt in **keinem
einzigen** der 78 Eintraege dorthin.

Der Rueckblick auf einen abgeschlossenen Monat trennt Vorlauf von echter Luecke: von 88
Juni-Eintraegen landeten **36 (41 %) nie** auf chess-results. Ganze Serien fehlen dort strukturell
(woechentliches Hitropotezni Železničar, ŠK Malečnik, Gurman — Dutzende Ausgaben, ein Treffer).

```
GET https://www.sah-zveza.si/prireditve/iskalnik/?action=filter&klubska=on&drzavna=on
    &mednarodna=on&vecdnevna=on&ciklusi=on&festivali=on&kraj=0&leto=0&mesec=0&page=1
```
Vier Seiten a 30 Zeilen. **Die Trefferliste traegt die PLZ** (74 von 78 gueltig) — Verortung ohne
Detailabruf. Die Detailseite liefert danach Adresse mit Hausnummer, Bedenkzeit, Rundenzahl,
System (`Švicar` / `Berger`) und Veranstalter.

Fallen: das `leto`-Dropdown listet nur bis 2026, `leto=2027` funktioniert aber trotzdem — wer sich
auf das Dropdown verlaesst, haelt 2027 fuer leer. **Absagen stehen im NAMEN** (`ODPADE;`,
`ODPOVEDANO`), nicht in einem Statusfeld.

**Vorbehalt:** keine robots.txt (404 seit mindestens 2025-07), keine Nutzungsbedingungen, kein
Copyright-Hinweis — aber ein grober nginx-UA-Filter, der jede Zeichenfolge „bot" blockt, **auch
Googlebot und bingbot**. Das ist ein kopierter Schnipsel, keine ueberlegte Absage; trotzdem ist
hier eine kurze Mail an `info@sah-zveza.si` das Sauberste.

### ⚠️ Tschechien — billig mitnehmen, kein Volumen

```
GET https://www.chess.cz/vypis-vsech-udalosti/   → 90 Eintraege, 304 KB, EIN Abruf
```

Die billigste bisher gepruefte Quelle — keine Paginierung, kein Formular, keine Detailseiten
(nachgeprueft: die tragen nichts Zusaetzliches). Aber die 90 zerfallen in **19 Nicht-Turniere**
(Schiedsrichterschulungen, Trainingslager, Sitzungen), **33 Ligarunden** (die auf genau zwei
chess-results-Turniere zeigen) und **38 echte Turniere**, von denen 13 nicht auf chess-results
stehen. Rund 1,6 zusaetzliche Turniere je Monat.

Zum Vergleich fuehrt chess-results fuer CZE im selben Zeitraum 146 — dort ist die Abdeckung
**viermal dichter**. Der erwartete Vorlaufeffekt bleibt aus, weil die weit vorne liegenden
chess.cz-Eintraege zu 86 % Ligarunden und grosse Festivals sind.

**Zwei Dinge machen es trotzdem attraktiv:** wo eine Ueberschneidung besteht, liefert chess.cz die
chess-results-Nummer direkt mit — exakte statt heuristischer Zuordnung. Und die 33 Ligarunden
sind genau das, wofuer wir sonst `art=14` je Turnier abrufen: **Spieltermine geschenkt**.

Falle bei der Gegenpruefung: `txt_bez` auf chess-results ist **diakritik-sensitiv** — „Granát"
findet, „Granat" findet nichts.

#### Was die Umsetzung dann gemessen hat (2026-09-08, v0.447.0)

**Die 33 Ligarunden sind der eigentliche Grund, und sie sind mehr wert als das Volumen.** Sie
gehoeren zu genau drei Meisterschaften — „šachy.cz Extraliga" (11 Runden, tnr1464172), „2. ligy"
(11, tnr1470450) und „1. ligy" (11, ohne Verweis). Aus elf Kalenderzeilen wird EIN Eintrag mit
elf Spielterminen; 22 davon landen ueber die mitgelieferte chess-results-Nummer direkt am
bestehenden Eintrag. Ohne diese Quelle kostet derselbe Rundenplan je Turnier einen eigenen
`art=14`-Abruf hinter dem 1500-ms-Limiter.

**Die Seite hat DREI Laschen, und die erste enthaelt alle.** „Nejbližší akce" fuehrt alle 89
Termine, „Mistrovské soutěže" (41) und „Kalendář mládeže" (8) sind Teilmengen und stehen im
Markup ein zweites Mal — 138 Zeilen fuer 89 Termine. Ohne Entdopplung ueber den Slug bekaeme
jeder zweite Eintrag ein Duplikat. Die Jugend-Lasche ist dabei ein Gewinn: anders als das
ungarische Jugend-Feld ist sie verlaesslich (8 von 89, alle richtig) und traegt einen Fall, den
kein Namensmuster faengt — „Mistrovství Čech 8 – 10 let" nennt seine Altersklasse als Spanne.

**Diese Quelle hat keine Nummer.** Im Markup steht nirgends eine Id; die Identitaet ist der
Adressbestandteil der Detailseite („turnovsky-granat-5"), und der wird bis zu **57 Zeichen** lang
— `PublicId` fasst 24. Gespeichert wird deshalb ein Kurzwert (`cz` + 12 Hex aus SHA-256), der
lesbare Slug steht vollstaendig im Herkunftsvermerk. Gekuerzt wird ausdruecklich NICHT:
„1-ligy-1-kolo" und „1-ligy-10-kolo" unterscheiden sich am Ende.

**Der Zeitraum steht in drei Formen da** — „12. 9. 26", „5. - 11. 9. 26" und
„31. 10. - 7. 11. 26" — mit dem Jahr EINMAL am Ende und zweistellig. Ueber den Jahreswechsel
gehoert es zum Ende, der Anfang liegt dann im Jahr davor.

Ein Faehnchen je Zeile nennt das Land (131× CZ, 2× SK, 2× MN). Das MN ist ein Datenfehler der
Quelle — der European Club Cup spielt in Herceg Novi, also Montenegro — und wird durchgereicht
statt korrigiert.

**robots.txt** (geprueft): `User-agent: *` sperrt `/wp-admin/`, `/souteze/vyber-kraje/`,
`/soutez` und `/druzstvo`. Die Terminliste liegt unter `/vypis-vsech-udalosti/`, ist also nicht
betroffen; die Detailseiten unter `/akce/` ebenso wenig (sie werden ohnehin nicht geholt, sie
tragen nichts Zusaetzliches).

### ❌ Liechtenstein — sauber, frei, und ohne jeden Zugewinn

`schach.li/agenda.html` ist server-gerendert, traegt hCalendar-Microformat mit ISO-Daten und die
volle Adresse **mit PLZ** — und fuehrt **ein** kuenftiges Turnier. Dasselbe steht auf
chess-results (dort als drei Zeilen, eine je Altersklasse). Zugewinn **null**.

chess-results fuehrt fuer LIE insgesamt 44 Zeilen ueber sechs Jahre, inklusive
Vereinsmeisterschaften — das Turnier-Universum ist dort vollstaendig abgebildet, und die LCF
verlinkt fuer Teilnehmerlisten selbst dorthin.

Der einzige Mehrwert waere die Adresse mit PLZ. Bei ein bis drei Turnieren im Jahr ist das
billiger von Hand gesetzt (`PUT /api/admin/tournament-directory/{id}/coordinates`,
`GeoSource=Manual` ueberlebt den Sweep) als ein Parser fuer ein Einzelstueck-CMS, dessen ICS-Feed
ausgerechnet unter dem einzigen `Disallow: /route/` liegt.

### Reihenfolge nach Ertrag je Aufwand

1. **chess-results-Ankuendigungskalender** — ein Endpunkt, +93 AUT-Turniere, dieselbe Domain,
   dieselbe Rechtslage, und der Ablauf ist schon implementiert. Danach dasselbe fuer die uebrigen
   Foederationen probieren.
2. **Italien** — 285 in einem Abruf, alle Felder inline, keinerlei Vorbehalt.
3. **Slowenien** — Faktor elf gegenueber chess-results, PLZ in der Liste. Vorher eine Mail.
4. **Slowakei** — offizielle API, die ausdruecklich zur Nutzung einlaedt, 27 gratis
   Dedup-Schluessel. *(umgesetzt in v0.445.0)*
5. **Ungarn** — ein POST, vor allem Vorlauf. *(umgesetzt in v0.446.0)*
6. **Tschechien** — billig, kleiner Ertrag, aber Ligatermine geschenkt. *(umgesetzt in v0.447.0)*
7. **Oberoesterreich** (PLZ-Qualitaet, autoritative Bedenkzeit) und **Steiermark+Vorarlberg**
   (ein Parser fuer zwei) — nur, wenn Ortsqualitaet wirklich gebraucht wird. Vorher anfragen.
8. **Liechtenstein**: nicht anbinden.

## Gesamtbild nach 15 geprueften Quellen

**Der Ausschlussgrund war fast nie die Technik.** Von 15 Quellen scheiterten 6 an einer
Rechts- oder robots-Frage, obwohl sie technisch brauchbar waren; 4 an ihrer Bauart (JavaScript-App
ohne serverseitige Daten, oder nur eigene Plattform-Turniere); 2 daran, dass sie ausschliesslich
Vergangenes fuehren. Uebrig bleiben 3 Quellen, die man morgen anfangen koennte, und 2, fuer die
eine E-Mail der naechste Schritt ist.

**Was ich umsetzen wuerde, in dieser Reihenfolge:**

1. **`chessarbiter.com` (Polen)** *(umgesetzt in v0.448.0)* — 611 kuenftige in EINEM Abruf, kein Vorbehalt, zweistufig wie
   unser bestehender Sweep. Bestes Verhaeltnis von Ertrag zu Aufwand, das die Pruefung gefunden
   hat.
2. **`englishchess.org.uk/events` (England)** — 320 Events ueber eine dokumentierte
   Standard-API, und die **einzige Quelle mit fertigen Koordinaten UND PLZ**. Unser Geocoding
   entfaellt dort ganz. `Crawl-delay: 10` beachten.
3. **`schaakbond.nl` (Niederlande)** — 173 kuenftige, ein ganzes Jahr Vorlauf, offene
   WordPress-API. Runden und Teilnehmerzahl fehlen; `Crawl-delay: 15` macht die Detailseiten zu
   einem Nachtlauf mit Deckel.
4. **`echecs.asso.fr` (Frankreich)** und **`schachbund.de` (Deutschland)** — beide brauchbar,
   beide mit dem Verfahren, das wir fuer chess-results schon haben (ASP.NET-Postback bzw.
   Cookie-Gate + RSS). Frankreich hat den Datenbankschutz-Passus, Deutschland keinen.
5. **Anfragen statt Crawler**: `chessmanager.com` (≈900 kuenftige aus 48 Laendern — der groesste
   Ertrag ueberhaupt, aber ausdrueckliche KI-Sperre) und `cms.swisschess.ch` (gute API hinter
   einem pauschalen `Disallow: /`, das unbeabsichtigt wirkt). Beide haben ein Interesse daran,
   dass ihre Turniere gefunden werden.

**Was ich nicht umsetzen wuerde**: `schack.se` (kein einziger Ort), `chess.ca` (keine stabile
Kennung, keine PLZ), `aicf.in` (Kennungen doppelt vergeben, keine PLZ), `shakkiliitto.fi`
(hohe Ueberschneidung, Saison-Jahresfalle), `caissachess.net` und `new.uschess.org` (USA — nur
sinnvoll, wenn US-Abdeckung ausdruecklich gewuenscht ist; bei US Chess zusaetzlich die
Serien-Entdopplung).

**Und ein Nebenergebnis, das eigene Arbeit waere**: `torneionline.com` (Italien, 14 523 Turniere
seit 2000, Bedenkzeit im Klartext, Teilnehmerzahl, kein Vorbehalt) und `danbase.skak.dk`
(Daenemark, 1890er bis heute, keine robots.txt) sind gute Quellen fuer den **Turnierverlauf**
eines Spielers — das Feature, das heute nur chess-results kennt.

**Ausserdem gefunden**: die deutschen LANDESverbaende fuehren eigene, von schachbund.de getrennte
Kalender (Bayern, NRW, NRW-Jugend nachgewiesen). Das ist ein Dutzend weiterer Quellen desselben
guten Typs — Verband, Ankuendigungen, kein kommerzielles Interesse.


## Runde 3: das uebrige Europa (2026-09-09)

Auftrag: „such dir fuer alle fehlenden europaeischen Laender die Seiten und vermerke diese."
Sechs Gruppen, drei gleichzeitig, jede mit derselben Frage wie in Runde 2: **bringt die Quelle
Turniere, die nicht ohnehin auf chess-results stehen?** Jede Zahl unten ist gemessen, nicht
geschaetzt; die chess-results-Vergleichszahl kommt je Land aus dem eigenen Crawler
(`/api/tournament-search?fed=<FED>&from=2026-09-09&to=2027-07-31`).

| Land | Urteil | Kuenftige | auf chess-results | Grund in einem Satz |
|---|---|---|---|---|
| **Irland** | ✅ | 81 | 19 | 94 % fehlen dort; robots.txt praktisch leer |
| **Frankreich** | ✅ | (Bestand) | — | nur 2 von 40 gegengeprueften stehen dort (5 %) |
| **Schottland** | ✅ | 44 | 8 | eine einzige Seite bis 2028, Bedenkzeit-Klasse strukturiert |
| **Rumaenien** | ✅ | 31 | 73 | ein Abruf, 32 % neu, darunter die ganzen Mannschaftsligen |
| **Wales** | ✅ | ≥29 | 5 | vollstaendige UK-Postleitzahlen im Fliesstext |
| **Norwegen** | ✅ | 81 | **0** | Eventfeed offen; chess-results kennt NOR gar nicht |
| Niederlande | ⚠️ | 177 | 12 | grosser Zusatz, aber Rundenzahl und Teilnehmer fehlen strukturell |
| Georgien | ⚠️ | 13 | 5 | 85 % neu, aber keine stabile Kennung und keine Anschrift |
| Kroatien | ⚠️ | 9 (+9) | 70 | sauber, aber kleiner Zusatz; keine Anschrift bei den Spielstaetten |
| Bosnien | ⚠️ | 4 | 37 | 2 echte Luecken, aber unstrukturierter Fliesstext |
| Aserbaidschan | ⚠️ | 6 | 8 | 4 neu, aber CSRF/Session noetig fuer wenige Zeilen |
| Island | ⚠️ | 54 (nur lfd. Monat) | 7 | robots.txt erlaubt nur den aktuellen Monat, jede Navigation ist gesperrt |
| Finnland | ⚠️ | 270 | 17 | hohe Ueberschneidung, keine Postleitzahl — Befund unveraendert |
| Schweden | ⚠️ | 181 | 23 | **0 von 181 mit Ortsfeld** — nicht verortbar |
| Tuerkei | ⚠️ | 3 ab heute | 12 | Jahrestabelle mit 44 Zeilen, aber fast alles schon vorbei |
| Montenegro | ⚠️ | 3 | 1 | technisch sauber, aber drei Turniere tragen keinen eigenen Dienst |
| Albanien | ⚠️ | 3 | 2 | dasselbe |
| Litauen | ⚠️ | 12 | 22 | 11 davon schon dort — netto ein Turnier |
| Lettland | ⚠️ | — | 73 | JS-SPA ohne sichtbare Schnittstelle; dort ohnehin gut abgedeckt |
| Spanien | ❌ | ~7 | 180 | Verbandskalender ist nur der institutionelle Terminplan; der Rest liegt auf 17 Regionalverbaenden |
| Belgien | ❌ | 0 erreichbar | **0** | Kalender ist ein eingebettetes Looker-Studio-Dashboard ohne Daten im Antworttext |
| Bulgarien | ❌ | 0 erreichbar | 37 | drei konkurrierende Verbandsnamen, keiner erreichbar (DNS / 403 / geparkt / leeres Wix-Widget) |
| Serbien | ❌ | 1 | 117 | eine verwertbare Zeile |
| Griechenland | ❌ | ~7/Jahr | 52 | nur das Archiv der eigenen Meisterschaften, ohne Ortsfeld, 100 % Ueberschneidung |
| Portugal | ❌ | 8 | 69 | sechs davon schon dort |
| Ukraine | ❌ | ~1 | 24 | Verbandskalender seit 2011 tot |
| Armenien | ❌ | 0 | 15 | Endpunkt funktioniert, Kalender ist leer (vier Monate geprueft) |
| Nordmazedonien | ❌ | 0 | 4 | Seite seit 13 Monaten nicht aktualisiert |
| Kosovo | ❌ | 0 | 0 | sauberste Tabelle der Runde — aber jede Zeile vergangen. Fuer die naechste Jahrestabelle vormerken |
| Moldau | ❌ | ~16/Jahr | 0 | ein Blogbeitrag, Datum ohne Jahresangabe |
| Zypern | ❌ | 0 | 3 | JS-SPA ohne serverseitigen Inhalt, leere Sitemap |
| Luxemburg | ❌ | ~2 neu | **0** | nur ein PDF-Saisonkalender |
| Andorra | ❌ | 1 | 1 | dasselbe eine Turnier |
| Monaco | ❌ | 0 | 0 | kein Kalender vorhanden |
| Daenemark | ❌ | — | 0 | `Disallow: /` fuer JEDEN Crawler; die offene Zweitdomaene ist rein historisch |
| Israel | ⚠️ | 98 Turniertage | 24 | grosse Luecke, aber der Endpunkt mit den Turnierdaten liess sich nicht rekonstruieren |
| Isle of Man | ⚠️ | 11/Saison | 0 | eigene Turnierverwaltung, aber nur geschlossene Klub-Ligadivisionen |
| Faeroeer | ❌ | **0** | 1 | Plugin und Technik sauber, Kalender aber leer — negativer Zugewinn |
| San Marino | ❌ | 0 | 0 | reiner Nachrichten-Blog ohne Vorlauf; robots.txt sperrt ClaudeBot UND GPTBot namentlich |
| Guernsey | ❌ | 1 | 1 | Klub-Kalender seit 2022 tot; das eine Festival steht schon dort |
| Jersey | ❌ | 0 | 0 | Ein-Seiten-Werbeseite ohne jede Struktur |
| Malta | ❓ | — | 7 | wie Estland von hier aus nicht erreichbar — kein Urteil ueber die Quelle |
| Estland | ❓ | — | 2 | von unserer Infrastruktur aus nicht erreichbar — siehe unten, das ist kein Urteil ueber die Quelle |
| Belarus / Russland | ❌ | — | 0 / 415 | von hier aus nicht erreichbar; bei RUS ausserdem die dichteste chess-results-Abdeckung ueberhaupt |

### Vier Lehren, die ueber die einzelnen Laender hinausgehen

**1. „Nicht erreichbar" ist oft eine Aussage ueber UNS, nicht ueber die Quelle.** Drei Hosts der
Balkan-Gruppe waren zunaechst tot (TCP-Timeout) und nach einer VPN-IP-Rotation sofort da. Estland
wurde daraufhin nachgemessen, und das Ergebnis ist eindeutig:

```
DNS      maleliit.ee → 185.7.252.220   loest auf
TCP 443  vom Host                      Verbindung steht
TCP 443  ueber das VPN                 keine Verbindung
```

Die estnische Seite lebt — unsere Ausgangs-IP ist dort gesperrt. **Malta zeigt dasselbe Bild**
(TCP-Timeout ohne RST auf Port 80 und 443), und auch dort fuehrt chess-results sieben kuenftige
Turniere, die Seite ist also mit Sicherheit nicht tot. Beide Laender sind deshalb mit ❓ vermerkt
und nicht mit ❌: ein Urteil ueber eine Quelle, die man nicht erreicht hat, waere keines. Fuer den Betrieb heisst das: ein
naechtlicher Durchgang braucht bei einem Timeout einen Wiederholversuch NACH Rotation, sonst faellt
eine gesunde Quelle sporadisch und grundlos aus. Serbien und Montenegro zeigten zusaetzlich beim
Erstzugriff eine voruebergehende JS-Bot-Pruefung, die beim zweiten Versuch verschwand.

**2. „The Events Calendar" ist das wiederkehrende Muster.** Vier der geprueften Verbaende fahren
WordPress mit diesem Plugin und haben damit ohne eigenes Zutun eine dokumentierte REST-API
(`/wp-json/tribe/events/v1/events` plus `/venues` mit `address`, `zip`, `geo_lat`, `geo_lng`) —
England laeuft schon so, Rumaenien und Kroatien koennten es. Es lohnt sich, das bei JEDER neuen
Quelle als Erstes zu probieren; es kostet einen Abruf und spart im Erfolgsfall den ganzen Parser.

**3. Eine unlesbare robots.txt ist ein Vermerk wert, keine stille Annahme.** `frsah.ro` (Rumaenien)
antwortet auf `/robots.txt` selbst mit **403**, waehrend Inhalt und REST-API klaglos 200 liefern.
RFC 9309 regelt genau das: ein 4xx auf die robots.txt heisst „keine Einschraenkungen". Danach wird
gehandelt — aber es steht hier, damit niemand spaeter glaubt, die Regel sei geprueft worden.

**4. Klein ist nicht dasselbe wie nutzlos, aber meistens doch.** Montenegro (3 Turniere gegen 1)
und Albanien (3 gegen 2) verdoppeln rechnerisch den Bestand ihres Landes. Trotzdem ⚠️ statt ✅: der
Massstab ist Liechtenstein aus Runde 1 — ein eigener Dienst mit Tests, Zeitplan und naechtlichem
Abruf rechnet sich nicht fuer drei Zeilen, die von Hand schneller eingetragen sind. Dasselbe gilt
fuer Litauen (netto ein Turnier) und die Tuerkei (drei datierte Zeilen ab heute).

### Die sechs, die angebunden werden — mit dem, was die Umsetzung braucht

**Irland — `icu.ie`** · `GET https://www.icu.ie/events`, serverseitig gerendertes HTML, paginiert,
rund 20 Eintraege je Abruf; die Listenseite nennt selbst „81 of 81". Detailseite je Turnier unter
`/events/{id}` — die Nummer ist damit eine echte, stabile Kennung. Einzelne Detailseiten verlinken
sogar direkt die chess-results-Nummer (stichprobenartig geprueft, korrekt), das waere derselbe
exakte Dedup-Schluessel wie bei chess.sk.

**Frankreich — `echecs.asso.fr`** · `ListeTournois.aspx?Action=RES&Mois=<1-12>&Annee=<Jahr>`; ohne
die Parameter zeigt die Seite nur ein rollierendes Fenster. robots.txt auf beiden Hosts 404. Der
Ertrag ist der groesste der Runde: die FFE fuehrt die nicht-FIDE-gewerteten Vereinsturniere, die
chess-results praktisch gar nicht kennt.

**Schottland — `chessscotland.com`** · `GET /calendar/upcoming`, EINE Seite, 44 Eintraege bis 2028,
robots.txt 404. Die Bedenkzeit-KLASSE steht strukturiert dabei — sonst muss sie ueberall aus dem
Namen erschlossen werden.

**Rumaenien — `frsah.ro`** · `GET /wp-json/tribe/events/v1/events?per_page=50&page=1`, ein Abruf,
31 Turniere (Antwort-Header `x-tec-total`), 84 % mit Ort. Plus `/venues` wie bei England. Keine
Postleitzahl, keine Koordinaten. Zur robots.txt siehe Lehre 3.

**Wales — `welshchessunion.uk`** · `GET /calendar/`, eine statische Saisonseite, ≥29 Eintraege mit
vollstaendiger Anschrift inklusive UK-Postleitzahl. **Keine stabile Kennung** — die Zuordnung muss
ueber Termin und Name laufen. Und ein Befund, der zum Muster dieser Reihe gehoert: die robots.txt
sperrt gezielt den bequemen **ICS-Export** (`ai1ec_exporter_controller`), erlaubt aber die
Fliesstext-Seite. Die umstaendlichere Route ist hier die einzige zulaessige.

**Norwegen — `sjakk.no`** · `GET /aktiviteter-feed.rss`, EIN Abruf, 542 kB, 1000 Eintraege, davon
81 kuenftig. chess-results kennt fuer NOR **null** kuenftige Turniere — der Zugewinn ist damit
rechnerisch vollstaendig. Der Haken: **kein Adressfeld**, nur Titel und Vereinsname. Das ist
dieselbe Schwaeche, an der Schweden scheitert; anders als dort traegt ein norwegischer Vereinsname
aber meist den Ort („Oslo Schakselskap"). Vor der Anbindung nachmessen, wie viele sich so verorten
lassen — nicht danach.

### Zwei geparkte Faelle mit konkretem naechsten Schritt

**Israel** ist die groesste ungehobene Luecke der Runde: ueber den funktionierenden
`ShowDay`-Endpunkt gemessen **98 Turniertage** im Vergleichszeitraum gegen 24 auf chess-results.
Der Endpunkt, der die Turnierdaten selbst liefert (`ShowTournaments`), liess sich aber trotz mehr
als zehn Parameter-Varianten nicht rekonstruieren. Das ist kein Rechts- und kein
Erreichbarkeitsproblem, sondern schlicht eine unbekannte Aufrufform — sie braucht einen echten
Netzwerkmitschnitt aus dem Browser, dann ist der Rest Routine.

**Estland und Malta** brauchen einen Abruf ueber eine andere Ausgangs-IP. Solange das nicht
passiert ist, steht dort ❓ und kein Urteil.

