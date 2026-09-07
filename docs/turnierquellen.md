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
